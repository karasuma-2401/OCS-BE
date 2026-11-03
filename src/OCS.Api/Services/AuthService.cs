using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using OCS.Api.Common;
using OCS.Api.Consts;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Repositories;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Auth;

namespace OCS.Api.Services;

public sealed partial class AuthService : IAuthService
{
    private static readonly string DummyPasswordHash =
        new PasswordHasher().HashPassword("DummY!P@ssw0rd#2026$");

    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly IOtpService _otpService;
    private readonly ILoginRateLimiter _loginRateLimiter;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        IOtpService otpService,
        ILoginRateLimiter? loginRateLimiter = null,
        TimeProvider? timeProvider = null,
        ILogger<AuthService>? logger = null)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _otpService = otpService;
        _loginRateLimiter = loginRateLimiter ?? new NullLoginRateLimiter();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<AuthService>.Instance;
    }

    public async Task<Result<LoginResultDto>> LoginAsync(
        LoginRequestDto request,
        string? clientIp = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedEmail = EmailIdentity.Normalize(request.Email);
        if (normalizedEmail is null)
        {
            _passwordHasher.VerifyPassword(request.Password, DummyPasswordHash);
            await _loginRateLimiter.RecordFailedAttemptAsync(clientIp, cancellationToken);
            return Result.Failure<LoginResultDto>(
                ApplicationError.Unauthorized("AUTH.INVALID_CREDENTIALS", "Email hoặc mật khẩu không chính xác."));
        }

        if (!await _loginRateLimiter.AllowIpRequestAsync(clientIp, cancellationToken))
        {
            return Result.Failure<LoginResultDto>(
                ApplicationError.RateLimited("AUTH.RATE_LIMITED", "Quá nhiều lần thử đăng nhập từ địa chỉ IP của bạn. Vui lòng thử lại sau."));
        }

        var account = await _unitOfWork.AuthRepository.GetByEmailAsync(normalizedEmail, cancellationToken);
        if (account is null)
        {
            _passwordHasher.VerifyPassword(request.Password, DummyPasswordHash);
            await _loginRateLimiter.RecordFailedAttemptAsync(clientIp, cancellationToken);
            return Result.Failure<LoginResultDto>(
                ApplicationError.Unauthorized("AUTH.INVALID_CREDENTIALS", "Email hoặc mật khẩu không chính xác."));
        }

        var now = _timeProvider.GetUtcNow();

        // 1. Check active temporary lockout
        if (account.LockoutEndAt.HasValue)
        {
            if (account.LockoutEndAt.Value > now)
            {
                await _loginRateLimiter.RecordFailedAttemptAsync(clientIp, cancellationToken);

                var remainingSeconds = Math.Max(1, (int)Math.Ceiling((account.LockoutEndAt.Value - now).TotalSeconds));
                var extensions = new Dictionary<string, object?>
                {
                    ["lockoutEndAt"] = account.LockoutEndAt.Value,
                    ["retryAfterSeconds"] = remainingSeconds
                };

                return Result.Failure<LoginResultDto>(
                    ApplicationError.Forbidden(
                        "AUTH.ACCOUNT_LOCKED",
                        "Tài khoản tạm thời bị khóa do nhập sai mật khẩu nhiều lần. Vui lòng thử lại sau.",
                        extensions));
            }

            // Lockout period has elapsed: clear temporary lockout window
            account.LockoutEndAt = null;
            account.FailedLoginAttempts = 0;
        }

        // 2. Verify password
        var isPasswordValid = _passwordHasher.VerifyPassword(request.Password, account.PasswordHash ?? string.Empty);
        if (!isPasswordValid)
        {
            account.FailedLoginAttempts++;
            await _loginRateLimiter.RecordFailedAttemptAsync(clientIp, cancellationToken);

            if (account.FailedLoginAttempts >= 5)
            {
                account.FailedLoginAttempts = 5;
                account.LockoutEndAt = now.AddMinutes(15);

                try
                {
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Concurrent update; lockout state still holds
                }

                LogAccountLockedOut(_logger, account.Id, account.LockoutEndAt.Value);

                var remainingSeconds = Math.Max(1, (int)Math.Ceiling((account.LockoutEndAt.Value - now).TotalSeconds));
                var extensions = new Dictionary<string, object?>
                {
                    ["lockoutEndAt"] = account.LockoutEndAt.Value,
                    ["retryAfterSeconds"] = remainingSeconds
                };

                return Result.Failure<LoginResultDto>(
                    ApplicationError.Forbidden(
                        "AUTH.ACCOUNT_LOCKED",
                        "Tài khoản tạm thời bị khóa do nhập sai mật khẩu nhiều lần. Vui lòng thử lại sau.",
                        extensions));
            }

            try
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Handled safely
            }

            return Result.Failure<LoginResultDto>(
                ApplicationError.Unauthorized("AUTH.INVALID_CREDENTIALS", "Email hoặc mật khẩu không chính xác."));
        }

        // 3. Password verified successfully: check account status
        if (account.Status == AccountStatus.Deactivated)
        {
            return Result.Failure<LoginResultDto>(
                ApplicationError.Forbidden("AUTH.ACCOUNT_DEACTIVATED", "Tài khoản đã bị vô hiệu hóa."));
        }

        if (account.Status == AccountStatus.Locked)
        {
            return Result.Failure<LoginResultDto>(
                ApplicationError.Forbidden("AUTH.ACCOUNT_LOCKED", "Tài khoản đã bị khóa bởi quản trị viên."));
        }

        if (account.Status == AccountStatus.PendingActivation)
        {
            var otpResult = await _otpService.IssueRegistrationOtpAsync(account, clientIp, cancellationToken);
            if (!otpResult.IsSuccess)
            {
                return Result.Failure<LoginResultDto>(otpResult.Error!);
            }

            var extensions = new Dictionary<string, object?>
            {
                ["challengeId"] = otpResult.Data!.ChallengeId,
                ["purpose"] = otpResult.Data.Purpose,
                ["maskedDestination"] = otpResult.Data.MaskedDestination,
                ["expiresAt"] = otpResult.Data.ExpiresAt,
                ["resendAvailableAt"] = otpResult.Data.ResendAvailableAt,
                ["email"] = otpResult.Data.MaskedDestination
            };

            return Result.Failure<LoginResultDto>(
                ApplicationError.Forbidden(
                    "AUTH.ACCOUNT_PENDING_ACTIVATION",
                    "Tài khoản chưa được kích hoạt.",
                    extensions));
        }

        // 4. Active account: reset counters
        account.FailedLoginAttempts = 0;
        account.LockoutEndAt = null;

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Handled safely
        }

        // 5. Extract role codes
        var roles = account.AccountRoles
            .Select(ar => ar.Role?.Code)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (roles.Count == 0)
        {
            LogAccountMissingRoles(_logger, account.Id);
            return Result.Failure<LoginResultDto>(
                ApplicationError.Forbidden("AUTH.ROLE_NOT_CONFIGURED", "Tài khoản chưa được cấu hình quyền hợp lệ trong hệ thống."));
        }

        // 6. Generate session & access token
        var sessionId = Guid.NewGuid();
        const int expiresIn = 28800; // 8 hours
        var expiresAt = now.AddSeconds(expiresIn);

        var session = new AuthSession
        {
            Id = sessionId,
            AccountId = account.Id,
            Account = account,
            CreatedAt = now,
            ExpiresAt = expiresAt,
            RevokedAt = null
        };
        _unitOfWork.AuthRepository.AddSession(session);

        var token = _tokenService.GenerateAccessToken(account, roles, sessionId);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Handled safely
        }

        var userDto = new UserDto(
            account.Id,
            account.FullName,
            account.Email,
            account.PhoneNumber,
            roles,
            account.Username);

        LogLoginSuccess(_logger, account.Id);

        return Result.Success(new LoginResultDto(token, expiresIn, expiresAt, userDto));
    }

    public async Task<Result<OtpChallengeResponseDto>> RegisterAsync(
        RegisterRequestDto request,
        string? clientIp = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedEmail = EmailIdentity.Normalize(request.Email);
        if (normalizedEmail is null)
        {
            return Result.Failure<OtpChallengeResponseDto>(
                ApplicationError.Validation("VALIDATION_FAILED", "Địa chỉ email không đúng định dạng."));
        }

        var existingAccount = await _unitOfWork.AuthRepository.GetByEmailAsync(normalizedEmail, cancellationToken);
        if (existingAccount is not null)
        {
            if (existingAccount.Status != AccountStatus.PendingActivation)
            {
                // Active, locked or deactivated account: reject registration with uniform conflict error
                return Result.Failure<OtpChallengeResponseDto>(ApplicationError.Conflict(
                    "AUTH.EMAIL_EXISTS",
                    "Không thể hoàn tất đăng ký với địa chỉ email này. Nếu bạn đã có tài khoản, vui lòng đăng nhập hoặc yêu cầu hỗ trợ."));
            }

            // Account is PendingActivation: verify password before allowing challenge continuation
            if (!_passwordHasher.VerifyPassword(request.Password, existingAccount.PasswordHash ?? string.Empty))
            {
                // Different password provided for existing pending account: reject to prevent takeover
                return Result.Failure<OtpChallengeResponseDto>(ApplicationError.Conflict(
                    "AUTH.EMAIL_EXISTS",
                    "Không thể hoàn tất đăng ký với địa chỉ email này. Nếu bạn đã có tài khoản, vui lòng đăng nhập hoặc yêu cầu hỗ trợ."));
            }

            // Same credentials for pending account: re-issue registration OTP
            var reIssueResult = await _otpService.IssueRegistrationOtpAsync(existingAccount, clientIp, cancellationToken);
            if (!reIssueResult.IsSuccess)
            {
                return Result.Failure<OtpChallengeResponseDto>(reIssueResult.Error!);
            }

            return Result.Success(new OtpChallengeResponseDto(
                reIssueResult.Data!.ChallengeId,
                reIssueResult.Data.Purpose,
                reIssueResult.Data.MaskedDestination,
                reIssueResult.Data.ExpiresAt,
                reIssueResult.Data.ResendAvailableAt));
        }

        var accountId = Guid.NewGuid();
        var hashedPassword = _passwordHasher.HashPassword(request.Password);

        var account = new Account
        {
            Id = accountId,
            FullName = request.FullName.Trim(),
            PhoneNumber = request.PhoneNumber.Trim(),
            Email = request.Email.Trim(),
            PasswordHash = hashedPassword,
            AccountType = AccountType.Customer,
            Status = AccountStatus.PendingActivation
        };

        var profile = new CustomerProfile
        {
            Id = accountId,
            Account = account
        };
        account.CustomerProfile = profile;

        var accountRole = new AccountRole
        {
            AccountId = accountId,
            RoleId = Role.CustomerRoleId
        };
        account.AccountRoles.Add(accountRole);

        _unitOfWork.AuthRepository.AddAccount(account);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Result.Failure<OtpChallengeResponseDto>(ApplicationError.Conflict(
                "AUTH.EMAIL_EXISTS",
                "Không thể hoàn tất đăng ký với địa chỉ email này. Nếu bạn đã có tài khoản, vui lòng đăng nhập hoặc yêu cầu hỗ trợ."));
        }

        var otpResult = await _otpService.IssueRegistrationOtpAsync(account, clientIp, cancellationToken);
        if (!otpResult.IsSuccess)
        {
            // Account remains in PendingActivation. User can retry later.
            return Result.Failure<OtpChallengeResponseDto>(otpResult.Error!);
        }

        return Result.Success(new OtpChallengeResponseDto(
            otpResult.Data!.ChallengeId,
            otpResult.Data.Purpose,
            otpResult.Data.MaskedDestination,
            otpResult.Data.ExpiresAt,
            otpResult.Data.ResendAvailableAt));
    }

    public async Task<Result<VerifyOtpResponseDto>> VerifyRegistrationOtpAsync(
        VerifyOtpRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var verifyResult = await _otpService.VerifyOtpAsync(
            request.ChallengeId,
            request.Code,
            "RegistrationActivation",
            cancellationToken);

        if (!verifyResult.IsSuccess)
        {
            return Result.Failure<VerifyOtpResponseDto>(verifyResult.Error!);
        }

        var account = await _unitOfWork.AuthRepository.GetByIdAsync(verifyResult.Data!.AccountId, cancellationToken);
        if (account is null)
        {
            return Result.Failure<VerifyOtpResponseDto>(ApplicationError.NotFound(
                "AUTH.ACCOUNT_NOT_FOUND",
                "Tài khoản không tồn tại."));
        }

        // Activate account atomically; phone verification is never asserted by email OTP
        account.Status = AccountStatus.Active;
        account.EmailVerifiedAt = verifyResult.Data.VerifiedAt;
        account.ActivatedAt = verifyResult.Data.VerifiedAt;
        account.FailedLoginAttempts = 0;
        account.LockoutEndAt = null;

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<VerifyOtpResponseDto>(ApplicationError.Conflict(
                "AUTH.CONCURRENT_ACTIVATION",
                "Tài khoản đang được kích hoạt bởi một yêu cầu khác."));
        }

        return Result.Success(new VerifyOtpResponseDto(
            "Kích hoạt tài khoản thành công. Vui lòng đăng nhập để tiếp tục.",
            verifyResult.Data.VerifiedAt));
    }

    public async Task<Result<OtpChallengeResponseDto>> ResendRegistrationOtpAsync(
        ResendOtpRequestDto request,
        string? clientIp = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resendResult = await _otpService.ResendOtpAsync(request.ChallengeId, clientIp, cancellationToken);
        if (!resendResult.IsSuccess)
        {
            return Result.Failure<OtpChallengeResponseDto>(resendResult.Error!);
        }

        return Result.Success(new OtpChallengeResponseDto(
            resendResult.Data!.ChallengeId,
            resendResult.Data.Purpose,
            resendResult.Data.MaskedDestination,
            resendResult.Data.ExpiresAt,
            resendResult.Data.ResendAvailableAt));
    }

    public async Task<Result<CurrentUserResponseDto>> GetCurrentUserAsync(
        Guid accountId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _unitOfWork.AuthRepository.GetSessionByIdAsync(sessionId, cancellationToken);
        if (session is null || session.AccountId != accountId)
        {
            return Result.Failure<CurrentUserResponseDto>(
                ApplicationError.Unauthorized("UNAUTHORIZED", "Phiên đăng nhập không tồn tại."));
        }

        if (session.RevokedAt.HasValue)
        {
            return Result.Failure<CurrentUserResponseDto>(
                ApplicationError.Unauthorized("UNAUTHORIZED", "Phiên đăng nhập đã bị thu hồi."));
        }

        var now = _timeProvider.GetUtcNow();
        if (now >= session.ExpiresAt)
        {
            return Result.Failure<CurrentUserResponseDto>(
                ApplicationError.Unauthorized("UNAUTHORIZED", "Phiên đăng nhập đã hết hạn."));
        }

        var account = session.Account ?? await _unitOfWork.AuthRepository.GetByIdAsync(accountId, cancellationToken);
        if (account is null)
        {
            return Result.Failure<CurrentUserResponseDto>(
                ApplicationError.Unauthorized("UNAUTHORIZED", "Tài khoản không tồn tại."));
        }

        if (account.Status != AccountStatus.Active)
        {
            return Result.Failure<CurrentUserResponseDto>(
                ApplicationError.Unauthorized("UNAUTHORIZED", "Tài khoản không còn hiệu lực hoạt động."));
        }

        var roles = account.AccountRoles
            .Select(ar => ar.Role?.Code)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (roles.Count == 0)
        {
            return Result.Failure<CurrentUserResponseDto>(
                ApplicationError.Forbidden("AUTH.ROLE_NOT_CONFIGURED", "Tài khoản chưa được cấu hình quyền hợp lệ trong hệ thống."));
        }

        var remainingSeconds = Math.Max(0, (int)Math.Ceiling((session.ExpiresAt - now).TotalSeconds));

        var userDto = new UserDto(
            account.Id,
            account.FullName,
            account.Email,
            account.PhoneNumber,
            roles,
            account.Username);

        return Result.Success(new CurrentUserResponseDto(remainingSeconds, session.ExpiresAt, userDto));
    }

    public async Task<Result<bool>> LogoutAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _unitOfWork.AuthRepository.GetSessionByIdAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return Result.Success(true);
        }

        if (!session.RevokedAt.HasValue)
        {
            session.RevokedAt = _timeProvider.GetUtcNow();
            try
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Concurrently revoked
            }
        }

        return Result.Success(true);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private sealed class NullLoginRateLimiter : ILoginRateLimiter
    {
        public Task<bool> AllowIpRequestAsync(string? clientIp, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task RecordFailedAttemptAsync(string? clientIp, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [LoggerMessage(LogLevel.Warning, "Account {AccountId} has reached maximum failed login attempts and is temporarily locked until {LockoutEndAt}.")]
    private static partial void LogAccountLockedOut(ILogger logger, Guid accountId, DateTimeOffset lockoutEndAt);

    [LoggerMessage(LogLevel.Warning, "Account {AccountId} attempted login but has no configured roles.")]
    private static partial void LogAccountMissingRoles(ILogger logger, Guid accountId);

    [LoggerMessage(LogLevel.Information, "Account {AccountId} successfully logged in.")]
    private static partial void LogLoginSuccess(ILogger logger, Guid accountId);
}
