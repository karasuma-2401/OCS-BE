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
    ITokenService tokenService) : IAuthService
{
    public async Task<Result<AuthResponseDto>> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default)
    {
        var account = await unitOfWork.AuthRepository.GetByUsernameAsync(request.Username, cancellationToken)
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

    public async Task<Result<UserDto>> RegisterAsync(RegisterRequestDto request, CancellationToken cancellationToken = default)
    {
        if (await unitOfWork.AuthRepository.ExistsUsernameAsync(request.Username, cancellationToken))
        {
            return Result.Failure<UserDto>(
                ApplicationError.Conflict("AUTH.USERNAME_EXISTS", "Tên đăng nhập đã được sử dụng."));
        }

        if (await unitOfWork.AuthRepository.ExistsPhoneAsync(request.PhoneNumber, cancellationToken))
        {
            return Result.Failure<UserDto>(
                ApplicationError.Conflict("AUTH.PHONE_EXISTS", "Số điện thoại đã được đăng ký."));
        }

        var accountId = Guid.NewGuid();
        var hashedPassword = passwordHasher.HashPassword(request.Password);

        var account = new Account
        {
            Id = accountId,
            Username = request.Username,
            PhoneNumber = request.PhoneNumber,
            FullName = request.FullName,
            Email = request.Email,
            PasswordHash = hashedPassword,
            AccountType = AccountType.Customer,
            Status = AccountStatus.Active
        };

        var profile = new CustomerProfile
        {
            Id = accountId,
            Account = account
        };

        var accountRole = new AccountRole
        {
            AccountId = accountId,
            RoleId = Role.CustomerRoleId
        };

        unitOfWork.AuthRepository.AddAccount(account);
        account.CustomerProfile = profile;
        account.AccountRoles.Add(accountRole);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var userDto = new UserDto(
            accountId,
            account.Username,
            account.FullName,
            account.Email,
            account.PhoneNumber,
            ["Customer"]);

        return Result.Success(userDto);
    }
}
