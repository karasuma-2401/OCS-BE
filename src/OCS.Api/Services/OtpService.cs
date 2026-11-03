using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OCS.Api.Common;
using OCS.Api.Configuration;
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

    public async Task<Result<OtpChallengeResultDto>> IssueRegistrationOtpAsync(
        Account account,
        string? clientIp,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (string.IsNullOrWhiteSpace(account.Email) || string.IsNullOrWhiteSpace(account.NormalizedEmail))
        {
            return Result.Failure<OtpChallengeResultDto>(ApplicationError.Validation(
                "AUTH.EMAIL_REQUIRED",
                "Tài khoản chưa có địa chỉ email hợp lệ để nhận mã OTP."));
        }

        var now = _timeProvider.GetUtcNow();

        var recentSends = await _unitOfWork.AuthRepository.CountOtpChallengesInWindowAsync(
            account.NormalizedEmail,
            now.AddHours(-1),
            cancellationToken);

        if (recentSends >= _options.MaxSendsPerEmailPerHour)
        {
            return Result.Failure<OtpChallengeResultDto>(ApplicationError.RateLimited(
                "AUTH.RATE_LIMITED",
                "Bạn đã yêu cầu gửi mã OTP quá số lần cho phép trong 1 giờ. Vui lòng thử lại sau."));
        }

        if (!await _rateLimiter.AllowIpRequestAsync(clientIp, cancellationToken))
        {
            return Result.Failure<OtpChallengeResultDto>(ApplicationError.RateLimited(
                "AUTH.RATE_LIMITED",
                "Vượt quá giới hạn yêu cầu từ địa chỉ IP này. Vui lòng thử lại sau."));
        }

        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(CultureInfo.InvariantCulture);
        var continuationToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

        var challenge = new OtpChallenge
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Account = account,
            NormalizedEmail = account.NormalizedEmail,
            Purpose = "RegistrationActivation",
            CodeHash = ComputeHmac(code),
            ContinuationTokenHash = ComputeHmac(continuationToken),
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(_options.ExpiryMinutes),
            ResendAvailableAt = now.AddSeconds(_options.ResendCooldownSeconds),
            ContinuationExpiresAt = now.AddMinutes(_options.ContinuationExpiryMinutes),
            FailedAttempts = 0,
            DeliveryStatus = "Pending"
        };

        var emailBody = BuildActivationEmailBody(code, _options.ExpiryMinutes);
        var emailMessage = new EmailMessage(
            account.Email,
            "[OCS] Mã xác thực kích hoạt tài khoản",
            emailBody,
            true);

        var sendResult = await _emailSender.SendEmailAsync(emailMessage, cancellationToken);
        if (!sendResult.IsSuccess)
        {
            challenge.DeliveryStatus = "Failed";
            _unitOfWork.AuthRepository.AddOtpChallenge(challenge);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Failure<OtpChallengeResultDto>(sendResult.Error!);
        }

        challenge.DeliveryStatus = "Sent";
        challenge.SentAt = _timeProvider.GetUtcNow();
        _unitOfWork.AuthRepository.AddOtpChallenge(challenge);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _rateLimiter.RecordIpRequestAsync(clientIp, cancellationToken);

        LogOtpIssued(_logger, challenge.Id, account.Id);

        return Result.Success(new OtpChallengeResultDto(
            challenge.Id,
            challenge.Purpose,
            MaskEmail(account.Email),
            challenge.ExpiresAt,
            challenge.ResendAvailableAt,
            continuationToken));
    }

    public async Task<Result<OtpChallengeResultDto>> ResendOtpAsync(
        Guid challengeId,
        string? clientIp,
        CancellationToken cancellationToken = default)
    {
        var challenge = await _unitOfWork.AuthRepository.GetOtpChallengeByIdAsync(challengeId, cancellationToken);
        if (challenge is null || challenge.InvalidatedAt != null || challenge.ConsumedAt != null)
        {
            return Result.Failure<OtpChallengeResultDto>(ApplicationError.NotFound(
                "AUTH.CHALLENGE_NOT_FOUND",
                "Yêu cầu xác thực không tồn tại hoặc đã hết hiệu lực."));
        }

        var now = _timeProvider.GetUtcNow();
        if (now < challenge.ResendAvailableAt)
        {
            var retryAfter = (int)Math.Ceiling((challenge.ResendAvailableAt - now).TotalSeconds);
            return Result.Failure<OtpChallengeResultDto>(ApplicationError.RateLimited(
                "AUTH.RESEND_COOLDOWN",
                $"Vui lòng đợi {retryAfter} giây trước khi yêu cầu gửi lại mã OTP."));
        }

        var recentSends = await _unitOfWork.AuthRepository.CountOtpChallengesInWindowAsync(
            challenge.NormalizedEmail,
            now.AddHours(-1),
            cancellationToken);

        if (recentSends >= _options.MaxSendsPerEmailPerHour)
        {
            return Result.Failure<OtpChallengeResultDto>(ApplicationError.RateLimited(
                "AUTH.RATE_LIMITED",
                "Bạn đã yêu cầu gửi mã OTP quá số lần cho phép trong 1 giờ. Vui lòng thử lại sau."));
        }

        if (!await _rateLimiter.AllowIpRequestAsync(clientIp, cancellationToken))
        {
            return Result.Failure<OtpChallengeResultDto>(ApplicationError.RateLimited(
                "AUTH.RATE_LIMITED",
                "Vượt quá giới hạn yêu cầu từ địa chỉ IP này. Vui lòng thử lại sau."));
        }

        // Invalidate old challenge
        challenge.InvalidatedAt = now;

        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(CultureInfo.InvariantCulture);
        var continuationToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

        var newChallenge = new OtpChallenge
        {
            Id = Guid.NewGuid(),
            AccountId = challenge.AccountId,
            Account = challenge.Account,
            NormalizedEmail = challenge.NormalizedEmail,
            Purpose = challenge.Purpose,
            CodeHash = ComputeHmac(code),
            ContinuationTokenHash = ComputeHmac(continuationToken),
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(_options.ExpiryMinutes),
            ResendAvailableAt = now.AddSeconds(_options.ResendCooldownSeconds),
            ContinuationExpiresAt = now.AddMinutes(_options.ContinuationExpiryMinutes),
            FailedAttempts = 0,
            DeliveryStatus = "Pending"
        };

        var emailBody = BuildActivationEmailBody(code, _options.ExpiryMinutes);
        var email = challenge.Account.Email ?? challenge.NormalizedEmail;
        var emailMessage = new EmailMessage(
            email,
            "[OCS] Mã xác thực kích hoạt tài khoản",
            emailBody,
            true);

        var sendResult = await _emailSender.SendEmailAsync(emailMessage, cancellationToken);
        if (!sendResult.IsSuccess)
        {
            newChallenge.DeliveryStatus = "Failed";
            _unitOfWork.AuthRepository.AddOtpChallenge(newChallenge);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Failure<OtpChallengeResultDto>(sendResult.Error!);
        }

        newChallenge.DeliveryStatus = "Sent";
        newChallenge.SentAt = _timeProvider.GetUtcNow();
        _unitOfWork.AuthRepository.AddOtpChallenge(newChallenge);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _rateLimiter.RecordIpRequestAsync(clientIp, cancellationToken);

        LogOtpResent(_logger, newChallenge.Id, challengeId);

        return Result.Success(new OtpChallengeResultDto(
            newChallenge.Id,
            newChallenge.Purpose,
            MaskEmail(email),
            newChallenge.ExpiresAt,
            newChallenge.ResendAvailableAt,
            continuationToken));
    }

    public async Task<Result<OtpVerificationResultDto>> VerifyOtpAsync(
        Guid challengeId,
        string code,
        string purpose,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure<OtpVerificationResultDto>(ApplicationError.Validation(
                "AUTH.OTP_INVALID",
                "Mã OTP không được để trống."));
        }

        var challenge = await _unitOfWork.AuthRepository.GetOtpChallengeByIdAsync(challengeId, cancellationToken);
        if (challenge is null)
        {
            return Result.Failure<OtpVerificationResultDto>(ApplicationError.NotFound(
                "AUTH.CHALLENGE_NOT_FOUND",
                "Yêu cầu xác thực không tồn tại."));
        }

        if (challenge.ConsumedAt != null)
        {
            return Result.Failure<OtpVerificationResultDto>(ApplicationError.Validation(
                "AUTH.CHALLENGE_NOT_FOUND",
                "Mã OTP đã được sử dụng."));
        }

        if (challenge.InvalidatedAt != null)
        {
            return Result.Failure<OtpVerificationResultDto>(ApplicationError.Validation(
                "AUTH.OTP_EXPIRED",
                "Mã OTP đã hết hiệu lực do yêu cầu mới hoặc quá số lần thử."));
        }

        if (!string.Equals(challenge.Purpose, purpose, StringComparison.Ordinal))
        {
            return Result.Failure<OtpVerificationResultDto>(ApplicationError.Validation(
                "AUTH.OTP_INVALID",
                "Mục đích xác thực không hợp lệ."));
        }

        var now = _timeProvider.GetUtcNow();
        if (now > challenge.ExpiresAt)
        {
            return Result.Failure<OtpVerificationResultDto>(ApplicationError.Validation(
                "AUTH.OTP_EXPIRED",
                "Mã OTP đã hết hạn. Vui lòng yêu cầu mã mới."));
        }

        if (challenge.FailedAttempts >= _options.MaxFailedAttempts)
        {
            challenge.InvalidatedAt = now;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Failure<OtpVerificationResultDto>(ApplicationError.Validation(
                "AUTH.OTP_ATTEMPTS_EXCEEDED",
                "Bạn đã nhập sai mã OTP quá 5 lần. Vui lòng yêu cầu mã mới."));
        }

        var actualHash = ComputeHmac(code.Trim());
        var codeMatches = FixedTimeEquals(actualHash, challenge.CodeHash);

        if (!codeMatches)
        {
            challenge.FailedAttempts++;
            if (challenge.FailedAttempts >= _options.MaxFailedAttempts)
            {
                challenge.InvalidatedAt = now;
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var attemptsLeft = Math.Max(0, _options.MaxFailedAttempts - challenge.FailedAttempts);
            var (errCode, msg) = attemptsLeft > 0
                ? ("AUTH.OTP_INVALID", $"Mã OTP không chính xác. Bạn còn {attemptsLeft} lần thử.")
                : ("AUTH.OTP_ATTEMPTS_EXCEEDED", "Bạn đã nhập sai mã OTP quá 5 lần. Yêu cầu đã bị hủy, vui lòng lấy mã mới.");

            return Result.Failure<OtpVerificationResultDto>(ApplicationError.Validation(errCode, msg));
        }

        // Code matches -> mark consumed
        challenge.ConsumedAt = now;

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            LogConcurrentOtpConsumption(_logger, challengeId);
            return Result.Failure<OtpVerificationResultDto>(ApplicationError.Conflict(
                "AUTH.OTP_CONCURRENT_CONSUMPTION",
                "Yêu cầu xác thực đang được xử lý hoặc đã hoàn tất."));
        }

        LogOtpVerified(_logger, challengeId, challenge.AccountId);

        return Result.Success(new OtpVerificationResultDto(
            challenge.AccountId,
            challenge.NormalizedEmail,
            now));
    }

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
