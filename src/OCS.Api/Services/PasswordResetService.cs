using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OCS.Api.Common;
using OCS.Api.Configuration;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Repositories;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Auth;

namespace OCS.Api.Services;

public sealed class PasswordResetService(
    AppDbContext db, IUnitOfWork unitOfWork, IEmailSender sender, IOtpRateLimiter limiter,
    IPasswordHasher passwordHasher, IOptions<OtpOptions> options, IConfiguration configuration,
    TimeProvider clock)
{
    private const string Purpose = "PasswordReset";
    private const string GenericMessage = "Nếu email đủ điều kiện, mã xác thực sẽ được gửi đến email của bạn.";
    private OtpOptions Options => options.Value;

    private string Hash(string value)
    {
        var secret = Options.SecretKey;
        if (string.IsNullOrWhiteSpace(secret)) secret = configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(secret)) secret = "ocs-default-otp-hmac-secret-key-32-chars-long";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private bool Matches(string value, string expected) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Hash(value)), Encoding.UTF8.GetBytes(expected));

    private static Result<T> Invalid<T>() => Result.Failure<T>(ApplicationError.Validation(
        "AUTH.RESET_INVALID", "Mã hoặc phiên đặt lại mật khẩu không hợp lệ hoặc đã hết hạn."));

    private static bool Eligible(Account account) => account.Status == AccountStatus.Active && account.EmailVerifiedAt.HasValue;

    public async Task<Result<string>> RequestAsync(string email, string? ip, CancellationToken ct)
    {
        if (!await limiter.AllowIpRequestAsync(ip, ct))
            return Result.Failure<string>(ApplicationError.RateLimited("AUTH.RATE_LIMITED", "Bạn đã yêu cầu quá nhiều lần. Vui lòng thử lại sau."));

        var normalized = EmailIdentity.Normalize(email);
        var account = normalized is null ? null : await db.Accounts.SingleOrDefaultAsync(a => a.NormalizedEmail == normalized, ct);
        if (account is null || !Eligible(account)) return Result.Success(GenericMessage);

        var now = clock.GetUtcNow();
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(CultureInfo.InvariantCulture);
        OtpChallenge challenge;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await unitOfWork.AuthRepository.LockAccountAsync(account.Id, ct);
            await db.Entry(account).ReloadAsync(ct);
            if (!Eligible(account) || account.NormalizedEmail != normalized) return Result.Success(GenericMessage);

            var previous = await db.OtpChallenges.Where(c => c.AccountId == account.Id && c.Purpose == Purpose)
                .OrderByDescending(c => c.CreatedAt).ToListAsync(ct);
            if (previous.Any(c => c.DeliveryStatus != "Failed" && c.ResendAvailableAt > now)
                || await db.OtpChallenges.CountAsync(c => c.NormalizedEmail == normalized && c.CreatedAt >= now.AddHours(-1), ct) >= Options.MaxSendsPerEmailPerHour)
                return Result.Success(GenericMessage);

            foreach (var old in previous.Where(c => c.ConsumedAt == null && c.InvalidatedAt == null)) old.InvalidatedAt = now;
            challenge = new OtpChallenge
            {
                AccountId = account.Id, NormalizedEmail = normalized!, Purpose = Purpose,
                CodeHash = Hash(code), ContinuationTokenHash = Hash(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))),
                CreatedAt = now, ExpiresAt = now.AddMinutes(Options.ExpiryMinutes),
                ContinuationExpiresAt = now.AddMinutes(Options.ContinuationExpiryMinutes),
                ResendAvailableAt = now.AddSeconds(Options.ResendCooldownSeconds), DeliveryStatus = "Pending"
            };
            db.OtpChallenges.Add(challenge);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        var delivery = await sender.SendEmailAsync(new EmailMessage(account.Email!.Trim(), "[OCS] Mã đặt lại mật khẩu",
            $"<p>Bạn đã yêu cầu đặt lại mật khẩu OCS.</p><p>Mã xác thực: <strong>{code}</strong></p><p>Mã có hiệu lực trong {Options.ExpiryMinutes} phút. Không chia sẻ mã này với bất kỳ ai.</p><p>Nếu bạn không yêu cầu, hãy bỏ qua email này.</p>", true), ct);
        var sentAt = delivery.IsSuccess ? clock.GetUtcNow() : (DateTimeOffset?)null;
        await db.OtpChallenges.Where(c => c.Id == challenge.Id).ExecuteUpdateAsync(
            setters => setters.SetProperty(c => c.DeliveryStatus, delivery.IsSuccess ? "Sent" : "Failed")
                .SetProperty(c => c.SentAt, sentAt), ct);
        return Result.Success(GenericMessage);
    }

    public async Task<Result<PasswordResetTokenDto>> VerifyAsync(string email, string code, CancellationToken ct)
    {
        var normalized = EmailIdentity.Normalize(email);
        var account = normalized is null ? null : await db.Accounts.SingleOrDefaultAsync(a => a.NormalizedEmail == normalized, ct);
        if (account is null) return Invalid<PasswordResetTokenDto>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await unitOfWork.AuthRepository.LockAccountAsync(account.Id, ct);
        await db.Entry(account).ReloadAsync(ct);
        var challenge = await db.OtpChallenges.Where(c => c.AccountId == account.Id && c.Purpose == Purpose)
            .OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync(ct);
        var now = clock.GetUtcNow();
        if (!Eligible(account) || account.NormalizedEmail != normalized || challenge is null || challenge.NormalizedEmail != normalized
            || challenge.DeliveryStatus != "Sent" || challenge.ConsumedAt.HasValue || challenge.InvalidatedAt.HasValue
            || challenge.VerifiedAt.HasValue || now >= challenge.ExpiresAt)
            return Invalid<PasswordResetTokenDto>();

        if (!Matches(code, challenge.CodeHash))
        {
            challenge.FailedAttempts++;
            if (challenge.FailedAttempts >= Options.MaxFailedAttempts) challenge.InvalidatedAt = now;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Invalid<PasswordResetTokenDto>();
        }
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        challenge.VerifiedAt = now;
        challenge.ContinuationTokenHash = Hash(token);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Success(new PasswordResetTokenDto(token));
    }

    public async Task<Result<string>> CompleteAsync(string email, string token, string password, CancellationToken ct)
    {
        var normalized = EmailIdentity.Normalize(email);
        var account = normalized is null ? null : await db.Accounts.SingleOrDefaultAsync(a => a.NormalizedEmail == normalized, ct);
        if (account is null) return Invalid<string>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await unitOfWork.AuthRepository.LockAccountAsync(account.Id, ct);
        await db.Entry(account).ReloadAsync(ct);
        var challenge = await db.OtpChallenges.Where(c => c.AccountId == account.Id && c.Purpose == Purpose)
            .OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync(ct);
        var now = clock.GetUtcNow();
        if (!Eligible(account) || account.NormalizedEmail != normalized || challenge is null || challenge.NormalizedEmail != normalized
            || challenge.DeliveryStatus != "Sent" || !challenge.VerifiedAt.HasValue || challenge.ConsumedAt.HasValue
            || challenge.InvalidatedAt.HasValue || now >= challenge.ContinuationExpiresAt || !Matches(token, challenge.ContinuationTokenHash))
            return Invalid<string>();
        account.PasswordHash = passwordHasher.HashPassword(password);
        account.FailedLoginAttempts = 0;
        account.LockoutEndAt = null;
        challenge.ConsumedAt = now;
        await unitOfWork.AuthRepository.RevokeSessionsAsync(account.Id, now, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Success("Đặt lại mật khẩu thành công. Vui lòng đăng nhập lại.");
    }
}
