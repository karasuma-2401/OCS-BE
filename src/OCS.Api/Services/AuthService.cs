using Microsoft.EntityFrameworkCore;
using Npgsql;
using OCS.Api.Common;
using OCS.Api.Consts;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Repositories;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Auth;

namespace OCS.Api.Services;

public sealed class AuthService(
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IOtpService otpService) : IAuthService
{
    public async Task<Result<AuthResponseDto>> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default)
    {
        var account = await unitOfWork.AuthRepository.GetByEmailAsync(request.Username, cancellationToken)
                      ?? await unitOfWork.AuthRepository.GetByUsernameAsync(request.Username, cancellationToken)
                      ?? await unitOfWork.AuthRepository.GetByPhoneAsync(request.Username, cancellationToken);

        if (account is null)
        {
            return Result.Failure<AuthResponseDto>(
                ApplicationError.NotFound("AUTH.INVALID_CREDENTIALS", "Tên đăng nhập hoặc mật khẩu không chính xác."));
        }

        if (account.Status != AccountStatus.Active)
        {
            return Result.Failure<AuthResponseDto>(
                ApplicationError.Forbidden("AUTH.ACCOUNT_LOCKED", "Tài khoản hiện đang bị khóa hoặc ngừng hoạt động."));
        }

        if (!passwordHasher.VerifyPassword(request.Password, account.PasswordHash ?? string.Empty))
        {
            return Result.Failure<AuthResponseDto>(
                ApplicationError.Validation("AUTH.INVALID_CREDENTIALS", "Tên đăng nhập hoặc mật khẩu không chính xác."));
        }

        var roles = account.AccountRoles.Select(ar => ar.Role?.Name ?? "Customer").ToList();
        if (roles.Count == 0)
        {
            roles.Add("Customer");
        }

        var token = tokenService.GenerateAccessToken(account, roles);

        var userDto = new UserDto(
            account.Id,
            account.Username,
            account.FullName,
            account.Email,
            account.PhoneNumber,
            roles);

        return Result.Success(new AuthResponseDto(token, 28800, userDto));
    }

    public async Task<Result<OtpChallengeResponseDto>> RegisterAsync(
        RegisterRequestDto request,
        string? clientIp = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = EmailIdentity.Normalize(request.Email);
        if (normalizedEmail is null)
        {
            return Result.Failure<OtpChallengeResponseDto>(
                ApplicationError.Validation("VALIDATION_FAILED", "Địa chỉ email không đúng định dạng."));
        }

        var existingAccount = await unitOfWork.AuthRepository.GetByEmailAsync(normalizedEmail, cancellationToken);
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
            if (!passwordHasher.VerifyPassword(request.Password, existingAccount.PasswordHash ?? string.Empty))
            {
                // Different password provided for existing pending account: reject to prevent takeover
                return Result.Failure<OtpChallengeResponseDto>(ApplicationError.Conflict(
                    "AUTH.EMAIL_EXISTS",
                    "Không thể hoàn tất đăng ký với địa chỉ email này. Nếu bạn đã có tài khoản, vui lòng đăng nhập hoặc yêu cầu hỗ trợ."));
            }

            // Same credentials for pending account: re-issue registration OTP
            var reIssueResult = await otpService.IssueRegistrationOtpAsync(existingAccount, clientIp, cancellationToken);
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
        var hashedPassword = passwordHasher.HashPassword(request.Password);

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

        unitOfWork.AuthRepository.AddAccount(account);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Result.Failure<OtpChallengeResponseDto>(ApplicationError.Conflict(
                "AUTH.EMAIL_EXISTS",
                "Không thể hoàn tất đăng ký với địa chỉ email này. Nếu bạn đã có tài khoản, vui lòng đăng nhập hoặc yêu cầu hỗ trợ."));
        }

        var otpResult = await otpService.IssueRegistrationOtpAsync(account, clientIp, cancellationToken);
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
        var verifyResult = await otpService.VerifyOtpAsync(
            request.ChallengeId,
            request.Code,
            "RegistrationActivation",
            cancellationToken);

        if (!verifyResult.IsSuccess)
        {
            return Result.Failure<VerifyOtpResponseDto>(verifyResult.Error!);
        }

        var account = await unitOfWork.AuthRepository.GetByIdAsync(verifyResult.Data!.AccountId, cancellationToken);
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
            await unitOfWork.SaveChangesAsync(cancellationToken);
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
        var resendResult = await otpService.ResendOtpAsync(request.ChallengeId, clientIp, cancellationToken);
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

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
