using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OCS.Api.Common;
using OCS.Api.Configuration;
using OCS.Api.Consts;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Repositories;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Auth;

namespace OCS.Api.Services;

public sealed partial class OtpService : IOtpService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailSender _emailSender;
    private readonly IOtpRateLimiter _rateLimiter;
    private readonly OtpOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OtpService> _logger;
    private readonly byte[] _secretKeyBytes;

    public OtpService(
        IUnitOfWork unitOfWork,
        IEmailSender emailSender,
        IOtpRateLimiter rateLimiter,
        IOptions<OtpOptions> otpOptions,
        IConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<OtpService> logger)
    {
        _unitOfWork = unitOfWork;
        _emailSender = emailSender;
        _rateLimiter = rateLimiter;
        _options = otpOptions.Value;
        _timeProvider = timeProvider;
        _logger = logger;

        var secret = _options.SecretKey;
        if (string.IsNullOrWhiteSpace(secret))
        {
            secret = configuration["Jwt:Key"];
        }

        if (string.IsNullOrWhiteSpace(secret))
        {
            secret = "ocs-default-otp-hmac-secret-key-32-chars-long";
        }

        _secretKeyBytes = Encoding.UTF8.GetBytes(secret);
    }

    public Task<Result<OtpChallengeResultDto>> IssueRegistrationOtpAsync(
        Account account, string? clientIp, CancellationToken cancellationToken = default) =>
        IssueRegistrationOtpCoreAsync(account, clientIp, null, null, cancellationToken);

    private async Task<Result<OtpChallengeResultDto>> IssueRegistrationOtpCoreAsync(
        Account account, string? clientIp, Guid? priorChallengeId, string? continuationToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (string.IsNullOrWhiteSpace(account.Email) || string.IsNullOrWhiteSpace(account.NormalizedEmail))
            return Result.Failure<OtpChallengeResultDto>(ApplicationError.Validation("AUTH.EMAIL_REQUIRED", "Tài khoản chưa có email hợp lệ."));
        if (!await _rateLimiter.AllowIpRequestAsync(clientIp, cancellationToken))
            return Result.Failure<OtpChallengeResultDto>(ApplicationError.RateLimited("AUTH.RATE_LIMITED", "Vượt quá giới hạn gửi mã OTP."));

        var now = _timeProvider.GetUtcNow();
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(CultureInfo.InvariantCulture);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var challenge = new OtpChallenge();
        List<OtpChallenge> prior = [];
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.AuthRepository.LockAccountAsync(account.Id, cancellationToken);
            if (!await _unitOfWork.ReloadAccountAsync(account, cancellationToken)
                || account.Status is AccountStatus.Locked or AccountStatus.Deactivated
                || account.EmailVerifiedAt.HasValue || string.IsNullOrWhiteSpace(account.NormalizedEmail))
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<OtpChallengeResultDto>(ApplicationError.Forbidden("AUTH.ACCOUNT_UNAVAILABLE", "Không thể xác thực tài khoản này."));
            }

            var latest = await _unitOfWork.AuthRepository.GetLatestOtpChallengeAsync(account.Id, cancellationToken);
            if (latest is not null)
                await _unitOfWork.AuthRepository.ReloadChallengeAsync(latest, cancellationToken);
            if (priorChallengeId.HasValue && (latest is null || latest.Id != priorChallengeId.Value
                || latest.ConsumedAt.HasValue || now >= latest.ContinuationExpiresAt
                || (latest.InvalidatedAt.HasValue && latest.FailedAttempts < _options.MaxFailedAttempts)
                || !ValidContinuation(latest, continuationToken ?? string.Empty)
                || !string.Equals(latest.NormalizedEmail, account.NormalizedEmail, StringComparison.Ordinal)))
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<OtpChallengeResultDto>(ApplicationError.NotFound(
                    "AUTH.CHALLENGE_NOT_FOUND", "Yêu cầu xác thực không tồn tại hoặc đã hết hiệu lực."));
            }
            if (latest is not null && latest.ResendAvailableAt > now)
            {
                var seconds = Math.Max(1, (int)Math.Ceiling((latest.ResendAvailableAt - now).TotalSeconds));
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<OtpChallengeResultDto>(ApplicationError.RateLimited(
                    "AUTH.RESEND_COOLDOWN", "Vui lòng đợi trước khi yêu cầu mã mới.",
                    new Dictionary<string, object?> { ["retryAfterSeconds"] = seconds }));
            }
            var sends = await _unitOfWork.AuthRepository.CountOtpChallengesInWindowAsync(
                account.NormalizedEmail, now.AddHours(-1), cancellationToken);
            if (sends >= _options.MaxSendsPerEmailPerHour)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<OtpChallengeResultDto>(ApplicationError.RateLimited("AUTH.RATE_LIMITED", "Bạn đã yêu cầu quá nhiều mã OTP."));
            }

            prior = await _unitOfWork.AuthRepository.GetActiveOtpChallengesAsync(account.Id, cancellationToken);
            foreach (var previous in prior) previous.InvalidatedAt = now;
            challenge = new OtpChallenge
            {
                Id = Guid.NewGuid(),
                AccountId = account.Id,
                Account = account,
                NormalizedEmail = account.NormalizedEmail,
                Purpose = "RegistrationActivation",
                CodeHash = ComputeHmac(code),
                ContinuationTokenHash = ComputeHmac(token),
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(_options.ExpiryMinutes),
                ResendAvailableAt = now.AddSeconds(_options.ResendCooldownSeconds),
                ContinuationExpiresAt = now.AddMinutes(_options.ContinuationExpiryMinutes),
                DeliveryStatus = "Pending"
            };
            _unitOfWork.AuthRepository.AddOtpChallenge(challenge);
            // Persist reservation, cooldown and invalidation before external delivery.
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        var delivery = await _emailSender.SendEmailAsync(new EmailMessage(
            account.Email!.Trim(), "[OCS] Mã xác thực kích hoạt tài khoản",
            BuildActivationEmailBody(code, _options.ExpiryMinutes), true), cancellationToken);
        if (!delivery.IsSuccess)
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            try
            {
                await _unitOfWork.AuthRepository.LockAccountAsync(account.Id, cancellationToken);
                await _unitOfWork.AuthRepository.ReloadChallengeAsync(challenge, cancellationToken);
                var latest = await _unitOfWork.AuthRepository.GetLatestOtpChallengeAsync(account.Id, cancellationToken);
                challenge.DeliveryStatus = "Failed";
                // Restore the previous code only if no newer request replaced this one.
                if (latest?.Id == challenge.Id)
                    foreach (var previous in prior)
                    {
                        await _unitOfWork.AuthRepository.ReloadChallengeAsync(previous, cancellationToken);
                        if (!previous.ConsumedAt.HasValue) previous.InvalidatedAt = null;
                    }
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                throw;
            }
            return Result.Failure<OtpChallengeResultDto>(delivery.Error!);
        }

        challenge.DeliveryStatus = "Sent";
        challenge.SentAt = _timeProvider.GetUtcNow();
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        LogOtpIssued(_logger, challenge.Id, account.Id);
        return Result.Success(new OtpChallengeResultDto(challenge.Id, challenge.Purpose,
            MaskEmail(account.Email!.Trim()), challenge.ExpiresAt, challenge.ResendAvailableAt, token));
    }

    public async Task<Result<OtpChallengeResultDto>> ResendOtpAsync(
        Guid challengeId, string continuationToken, string? clientIp, CancellationToken cancellationToken = default)
    {
        var challenge = await _unitOfWork.AuthRepository.GetOtpChallengeByIdAsync(challengeId, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        if (challenge is null || challenge.ConsumedAt.HasValue
            || (challenge.InvalidatedAt.HasValue && challenge.FailedAttempts < _options.MaxFailedAttempts)
            || now >= challenge.ContinuationExpiresAt || !ValidContinuation(challenge, continuationToken))
            return Result.Failure<OtpChallengeResultDto>(ApplicationError.NotFound(
                "AUTH.CHALLENGE_NOT_FOUND", "Yêu cầu xác thực không tồn tại hoặc đã hết hiệu lực."));
        if (!string.Equals(challenge.NormalizedEmail, challenge.Account.NormalizedEmail, StringComparison.Ordinal))
            return Result.Failure<OtpChallengeResultDto>(ApplicationError.Forbidden("AUTH.ACCOUNT_UNAVAILABLE", "Email tài khoản đã thay đổi."));
        return await IssueRegistrationOtpCoreAsync(challenge.Account, clientIp, challengeId, continuationToken, cancellationToken);
    }

    public async Task<Result<OtpVerificationResultDto>> VerifyOtpAsync(
        Guid challengeId, string code, string purpose, string continuationToken,
        CancellationToken cancellationToken = default)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var challenge = await _unitOfWork.AuthRepository.GetOtpChallengeByIdAsync(challengeId, cancellationToken);
            if (challenge is null)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<OtpVerificationResultDto>(ApplicationError.NotFound("AUTH.CHALLENGE_NOT_FOUND", "Yêu cầu xác thực không tồn tại."));
            }
            await _unitOfWork.AuthRepository.LockAccountAsync(challenge.AccountId, cancellationToken);
            if (!await _unitOfWork.ReloadAccountAsync(challenge.Account, cancellationToken))
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<OtpVerificationResultDto>(ApplicationError.NotFound("AUTH.CHALLENGE_NOT_FOUND", "Yêu cầu xác thực không tồn tại."));
            }
            await _unitOfWork.AuthRepository.ReloadChallengeAsync(challenge, cancellationToken);
            var account = challenge.Account;
            var now = _timeProvider.GetUtcNow();
            if (challenge.ConsumedAt.HasValue || challenge.InvalidatedAt.HasValue || now >= challenge.ContinuationExpiresAt
                || !ValidContinuation(challenge, continuationToken) || !string.Equals(challenge.Purpose, purpose, StringComparison.Ordinal))
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<OtpVerificationResultDto>(ApplicationError.NotFound("AUTH.CHALLENGE_NOT_FOUND", "Yêu cầu xác thực không còn hiệu lực."));
            }
            if (account.Status is AccountStatus.Locked or AccountStatus.Deactivated || account.EmailVerifiedAt.HasValue
                || !string.Equals(account.NormalizedEmail, challenge.NormalizedEmail, StringComparison.Ordinal))
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<OtpVerificationResultDto>(ApplicationError.Forbidden("AUTH.ACCOUNT_UNAVAILABLE", "Không thể xác thực tài khoản này."));
            }
            if (now >= challenge.ExpiresAt || challenge.DeliveryStatus != "Sent")
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<OtpVerificationResultDto>(ApplicationError.Validation("AUTH.OTP_EXPIRED", "Mã OTP đã hết hạn hoặc chưa được gửi."));
            }
            if (!FixedTimeEquals(ComputeHmac(code), challenge.CodeHash))
            {
                challenge.FailedAttempts++;
                if (challenge.FailedAttempts >= _options.MaxFailedAttempts) challenge.InvalidatedAt = now;
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);
                var remaining = Math.Max(0, _options.MaxFailedAttempts - challenge.FailedAttempts);
                return Result.Failure<OtpVerificationResultDto>(ApplicationError.Validation(
                    remaining == 0 ? "AUTH.OTP_ATTEMPTS_EXCEEDED" : "AUTH.OTP_INVALID",
                    remaining == 0 ? "Mã đã bị khóa. Vui lòng yêu cầu mã mới." : $"Mã OTP không chính xác. Bạn còn {remaining} lần thử."));
            }
            challenge.ConsumedAt = now;
            account.Status = AccountStatus.Active;
            account.EmailVerifiedAt = now;
            account.ActivatedAt ??= now;
            // Challenge consumption and account activation are committed together.
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            LogOtpVerified(_logger, challengeId, challenge.AccountId);
            return Result.Success(new OtpVerificationResultDto(account.Id, challenge.NormalizedEmail, now));
        }
        catch (DbUpdateConcurrencyException)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            return Result.Failure<OtpVerificationResultDto>(ApplicationError.Conflict(
                "AUTH.OTP_CONCURRENT_CONSUMPTION", "Yêu cầu đang được xử lý hoặc đã hoàn tất."));
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    private bool ValidContinuation(OtpChallenge challenge, string token) =>
        !string.IsNullOrWhiteSpace(token) && FixedTimeEquals(ComputeHmac(token), challenge.ContinuationTokenHash);
    private string ComputeHmac(string data)
    {
        using var hmac = new HMACSHA256(_secretKeyBytes);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    public static string MaskEmail(string email)
    {
        var atIndex = email.IndexOf('@');
        if (atIndex <= 1)
        {
            return email;
        }

        var local = email[..atIndex];
        var domain = email[atIndex..];
        if (local.Length <= 2)
        {
            return local[0] + "***" + domain;
        }

        return local[0] + "***" + local[^1] + domain;
    }

    private static string BuildActivationEmailBody(string code, int expiryMinutes)
    {
        return $"""
            <!DOCTYPE html>
            <html lang="vi">
            <head>
              <meta charset="utf-8">
              <title>Mã xác thực OCS</title>
            </head>
            <body style="font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;">
              <h2 style="color: #2b6cb0;">Xác thực kích hoạt tài khoản OCS</h2>
              <p>Xin chào,</p>
              <p>Bạn đang thực hiện đăng ký tài khoản tại hệ thống Ký gửi Thời trang OCS. Dưới đây là mã xác thực OTP của bạn:</p>
              <div style="background-color: #edf2f7; padding: 15px; border-radius: 8px; text-align: center; font-size: 28px; font-weight: bold; letter-spacing: 6px; color: #2d3748; margin: 20px 0;">
                {code}
              </div>
              <p>Mã này có hiệu lực trong vòng <strong>{expiryMinutes.ToString(CultureInfo.InvariantCulture)} phút</strong>. Tuyệt đối không chia sẻ mã này với bất kỳ ai để bảo vệ an toàn cho tài khoản của bạn.</p>
              <p style="color: #718096; font-size: 13px; margin-top: 30px;">Nếu bạn không thực hiện yêu cầu này, vui lòng bỏ qua email.</p>
            </body>
            </html>
            """;
    }

    [LoggerMessage(LogLevel.Information, "Issued OTP challenge {ChallengeId} for account {AccountId}.")]
    private static partial void LogOtpIssued(ILogger logger, Guid challengeId, Guid accountId);

    [LoggerMessage(LogLevel.Information, "Resent OTP challenge {NewChallengeId} replacing {OldChallengeId}.")]
    private static partial void LogOtpResent(ILogger logger, Guid newChallengeId, Guid oldChallengeId);

    [LoggerMessage(LogLevel.Warning, "Concurrent OTP consumption detected for challenge {ChallengeId}.")]
    private static partial void LogConcurrentOtpConsumption(ILogger logger, Guid challengeId);

    [LoggerMessage(LogLevel.Information, "Successfully verified OTP challenge {ChallengeId} for account {AccountId}.")]
    private static partial void LogOtpVerified(ILogger logger, Guid challengeId, Guid accountId);
}
