using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OCS.Api.Common;
using OCS.Api.Consts;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Repositories;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Accounts;

namespace OCS.Api.Services;

public sealed class AccountManagementService(IUnitOfWork unitOfWork, IPasswordHasher passwordHasher)
    : IAccountManagementService
{
    private const string DefaultInternalPassword = "Ocs@123456";
    private const string PasswordPattern = @"^(?=.*[a-z])(?=.*[A-Z])(?=.*[\d\W]).{8,70}$";

    public async Task<Result<bool>> ResetPasswordAsync(Guid requesterAccountId, Guid accountId,
        ResetAccountPasswordRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (requesterAccountId == accountId)
            return Result.Failure<bool>(ApplicationError.Conflict(
                "ACCOUNT.SELF_PASSWORD_RESET", "Dùng chức năng đổi mật khẩu để thay mật khẩu của chính mình."));
        if (request.UseDefaultPassword && request.Password is not null
            || !request.UseDefaultPassword && (string.IsNullOrEmpty(request.Password)
                || Encoding.UTF8.GetByteCount(request.Password) > 72
                || !new RegularExpressionAttribute(PasswordPattern).IsValid(request.Password)))
            return Result.Failure<bool>(ApplicationError.Validation(
                "ACCOUNT.INVALID_PASSWORD", "Chọn mật khẩu mặc định hoặc nhập mật khẩu tự đặt hợp lệ."));

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await unitOfWork.AuthRepository.LockAccountAsync(accountId, cancellationToken);
            var requester = await unitOfWork.AuthRepository.GetByIdAsync(requesterAccountId, cancellationToken);
            if (requester is null || requester.Status != AccountStatus.Active || requester.EmailVerifiedAt is null)
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<bool>(ApplicationError.Unauthorized(
                    "ACCOUNT.UNAUTHORIZED", "Tài khoản không còn hiệu lực."));
            }
            if (!requester.AccountRoles.Any(ar => ar.RoleId == Role.AdminRoleId))
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<bool>(ApplicationError.Forbidden(
                    "ACCOUNT.FORBIDDEN", "Chỉ Admin được đặt lại mật khẩu tài khoản khác."));
            }
            var account = await unitOfWork.AuthRepository.GetByIdAsync(accountId, cancellationToken);
            if (account is null)
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<bool>(ApplicationError.NotFound(
                    "ACCOUNT.NOT_FOUND", "Không tìm thấy tài khoản."));
            }
            if (IsRootAdmin(account))
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<bool>(ApplicationError.Conflict(
                    "ACCOUNT.PROTECTED_ADMIN", "Không thể đặt lại mật khẩu Admin gốc."));
            }
            if (request.UseDefaultPassword && account.AccountType != AccountType.Internal)
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<bool>(ApplicationError.Validation(
                    "ACCOUNT.INVALID_PASSWORD", "Mật khẩu mặc định chỉ dành cho tài khoản nội bộ."));
            }

            var password = request.UseDefaultPassword ? DefaultInternalPassword : request.Password!;
            account.PasswordHash = passwordHasher.HashPassword(password);
            var now = DateTimeOffset.UtcNow;
            unitOfWork.AuthRepository.AddAuditLog(new AuditLog
            {
                ActorAccountId = requesterAccountId,
                ActorType = AuditActorType.User,
                Action = "PasswordReset",
                EntityType = nameof(Account),
                EntityId = accountId.ToString("D"),
                Source = "Accounts.ResetPassword",
                OccurredAt = now,
                RecordedAt = now
            });
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.AuthRepository.RevokeSessionsAsync(accountId, now, cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
            return Result.Success(true);
        }
        catch (DbUpdateConcurrencyException)
        {
            await unitOfWork.RollbackTransactionAsync(cancellationToken);
            return Result.Failure<bool>(ApplicationError.Conflict(
                "ACCOUNT.CONCURRENT_UPDATE", "Tài khoản vừa được thay đổi. Vui lòng tải lại."));
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    public async Task<Result<PagedResponse<AccountAuditLogDto>>> GetAccountHistoryAsync(
        Guid requesterAccountId, PagedRequest paging, Guid? accountId,
        CancellationToken cancellationToken = default)
    {
        var requester = await unitOfWork.AuthRepository.GetByIdAsync(requesterAccountId, cancellationToken);
        if (requester is null || requester.Status != AccountStatus.Active || requester.EmailVerifiedAt is null)
            return Result.Failure<PagedResponse<AccountAuditLogDto>>(ApplicationError.Unauthorized(
                "ACCOUNT.UNAUTHORIZED", "Tài khoản không còn hiệu lực."));
        if (!requester.AccountRoles.Any(ar => ar.RoleId == Role.AdminRoleId))
            return Result.Failure<PagedResponse<AccountAuditLogDto>>(ApplicationError.Forbidden(
                "ACCOUNT.FORBIDDEN", "Chỉ Admin được xem lịch sử tài khoản."));
        if (accountId.HasValue && await unitOfWork.AuthRepository.GetByIdAsync(accountId.Value, cancellationToken) is null)
            return Result.Failure<PagedResponse<AccountAuditLogDto>>(ApplicationError.NotFound(
                "ACCOUNT.NOT_FOUND", "Không tìm thấy tài khoản."));

        var page = await unitOfWork.AuthRepository.GetPagedAccountAuditLogsAsync(
            paging, accountId, cancellationToken);
        var items = page.Items.Select(log => new AccountAuditLogDto(log.Id, log.ActorAccountId,
            log.Action, log.EntityType, log.EntityId,
            JsonSerializer.Deserialize<JsonElement>(log.Changes), log.OccurredAt, log.Source)).ToList();
        return Result.Success(new PagedResponse<AccountAuditLogDto>(
            items, page.Page, page.PageSize, page.TotalCount));
    }

    public async Task<Result<AccountSummaryDto>> UpdateAccountInfoAsync(Guid requesterAccountId,
        Guid accountId, UpdateAccountInfoRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var fullName = request.FullName?.Trim();
        var email = request.Email?.Trim();
        var phoneNumber = request.PhoneNumber?.Trim();
        var username = request.Username?.Trim();
        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(phoneNumber)
            || EmailIdentity.Normalize(email) is null)
            return Result.Failure<AccountSummaryDto>(ApplicationError.Validation(
                "ACCOUNT.INVALID_INPUT", "Thông tin tài khoản chưa đầy đủ hoặc email không hợp lệ."));

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await unitOfWork.AuthRepository.LockRoleAsync(Role.AdminRoleId, cancellationToken);
            await unitOfWork.AuthRepository.LockAccountAsync(accountId, cancellationToken);
            var requester = await unitOfWork.AuthRepository.GetByIdAsync(requesterAccountId, cancellationToken);
            if (requester is null || requester.Status != AccountStatus.Active || requester.EmailVerifiedAt is null)
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<AccountSummaryDto>(ApplicationError.Unauthorized(
                    "ACCOUNT.UNAUTHORIZED", "Tài khoản không còn hiệu lực."));
            }
            if (!requester.AccountRoles.Any(ar => ar.RoleId == Role.AdminRoleId))
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<AccountSummaryDto>(ApplicationError.Forbidden(
                    "ACCOUNT.FORBIDDEN", "Chỉ Admin được sửa thông tin tài khoản."));
            }

            var account = await unitOfWork.AuthRepository.GetByIdAsync(accountId, cancellationToken);
            if (account is null)
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<AccountSummaryDto>(ApplicationError.NotFound(
                    "ACCOUNT.NOT_FOUND", "Không tìm thấy tài khoản."));
            }
            if (account.AccountType == AccountType.Internal && string.IsNullOrWhiteSpace(username)
                || account.AccountType == AccountType.Customer && !string.IsNullOrEmpty(username))
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<AccountSummaryDto>(ApplicationError.Validation(
                    "ACCOUNT.INVALID_USERNAME", "Tài khoản nội bộ cần username; Customer không sử dụng username."));
            }

            var emailChanged = !string.Equals(EmailIdentity.Normalize(account.Email),
                EmailIdentity.Normalize(email), StringComparison.Ordinal);
            var usernameChanged = !string.Equals(account.Username, username, StringComparison.Ordinal);
            if (IsRootAdmin(account) && (emailChanged || usernameChanged))
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<AccountSummaryDto>(ApplicationError.Conflict(
                    "ACCOUNT.PROTECTED_ADMIN", "Không thể đổi email hoặc username của Admin gốc."));
            }
            if (emailChanged && account.Status is AccountStatus.Locked or AccountStatus.Deactivated)
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<AccountSummaryDto>(ApplicationError.Conflict(
                    "ACCOUNT.INVALID_TRANSITION", "Cần mở khóa hoặc kích hoạt tài khoản trước khi đổi email."));
            }
            if (emailChanged && account.Status == AccountStatus.Active
                && account.AccountRoles.Any(ar => ar.RoleId == Role.AdminRoleId)
                && await unitOfWork.AuthRepository.CountActiveAdminsAsync(cancellationToken) <= 1)
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<AccountSummaryDto>(ApplicationError.Conflict(
                    "ACCOUNT.LAST_ADMIN", "Không thể đổi email của Admin đang hoạt động cuối cùng."));
            }

            account.FullName = fullName;
            account.Email = email;
            account.PhoneNumber = phoneNumber;
            account.Username = account.AccountType == AccountType.Internal ? username : null;
            if (emailChanged)
            {
                account.EmailVerifiedAt = null;
                account.Status = AccountStatus.PendingActivation;
            }
            await unitOfWork.SaveChangesAsync(cancellationToken);
            if (emailChanged || usernameChanged)
                await unitOfWork.AuthRepository.RevokeSessionsAsync(accountId, DateTimeOffset.UtcNow, cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
            return Result.Success(MapToSummary(account));
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await unitOfWork.RollbackTransactionAsync(cancellationToken);
            return Result.Failure<AccountSummaryDto>(ApplicationError.Conflict(
                "ACCOUNT.IDENTITY_EXISTS", "Email, username hoặc số điện thoại đã được sử dụng."));
        }
        catch (DbUpdateConcurrencyException)
        {
            await unitOfWork.RollbackTransactionAsync(cancellationToken);
            return Result.Failure<AccountSummaryDto>(ApplicationError.Conflict(
                "ACCOUNT.CONCURRENT_UPDATE", "Tài khoản vừa được thay đổi. Vui lòng tải lại."));
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    public Task<Result<AccountSummaryDto>> CreateInternalAccountAsync(Guid requesterAccountId,
        CreateInternalAccountRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var roleId = request.Role?.Trim().ToUpperInvariant() switch
        {
            "ADMIN" => Role.AdminRoleId,
            "MANAGER" => Role.ManagerRoleId,
            "STAFF" => Role.StaffRoleId,
            _ => Guid.Empty
        };
        if (roleId == Guid.Empty)
            return Task.FromResult(Result.Failure<AccountSummaryDto>(
                ApplicationError.Validation("ACCOUNT.INVALID_ROLES", "Vai trò phải là Admin, Manager hoặc Staff.")));

        var staffRequest = new CreateStaffAccountRequest(request.FullName, request.Email,
            request.PhoneNumber, request.Username, request.Password, request.JobTitle, request.HiredOn,
            request.UseDefaultPassword);
        return CreateInternalCoreAsync(requesterAccountId, staffRequest, roleId, true, cancellationToken);
    }

    public async Task<Result<AccountSummaryDto>> ChangeStatusAsync(Guid requesterAccountId,
        Guid accountId, ChangeAccountStatusRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var reactivate = string.Equals(request.Status?.Trim(), "Active", StringComparison.OrdinalIgnoreCase);
        var deactivate = string.Equals(request.Status?.Trim(), "Deactivated", StringComparison.OrdinalIgnoreCase);
        if (!reactivate && !deactivate)
            return Result.Failure<AccountSummaryDto>(ApplicationError.Validation(
                "ACCOUNT.INVALID_STATUS", "Chỉ nhận trạng thái Active hoặc Deactivated."));
        if (requesterAccountId == accountId)
            return Result.Failure<AccountSummaryDto>(ApplicationError.Conflict(
                "ACCOUNT.SELF_STATUS_CHANGE", "Không thể tự vô hiệu hóa hoặc kích hoạt lại tài khoản."));

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await unitOfWork.AuthRepository.LockRoleAsync(Role.AdminRoleId, cancellationToken);
            await unitOfWork.AuthRepository.LockAccountAsync(accountId, cancellationToken);
            var requester = await unitOfWork.AuthRepository.GetByIdAsync(requesterAccountId, cancellationToken);
            if (requester is null || requester.Status != AccountStatus.Active || requester.EmailVerifiedAt is null)
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<AccountSummaryDto>(ApplicationError.Unauthorized(
                    "ACCOUNT.UNAUTHORIZED", "Tài khoản không còn hiệu lực."));
            }
            var isAdmin = requester.AccountRoles.Any(ar => ar.RoleId == Role.AdminRoleId);
            var isManager = requester.AccountRoles.Any(ar => ar.RoleId == Role.ManagerRoleId);
            if (!isAdmin && !isManager)
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<AccountSummaryDto>(ApplicationError.Forbidden(
                    "ACCOUNT.FORBIDDEN", "Bạn không có quyền thay đổi trạng thái tài khoản."));
            }

            var account = await unitOfWork.AuthRepository.GetByIdAsync(accountId, cancellationToken);
            if (account is null || (!isAdmin && !IsStaffOnly(account)))
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<AccountSummaryDto>(ApplicationError.NotFound(
                    "ACCOUNT.NOT_FOUND", "Không tìm thấy tài khoản."));
            }
            if (IsRootAdmin(account))
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<AccountSummaryDto>(ApplicationError.Conflict(
                    "ACCOUNT.PROTECTED_ADMIN", "Không thể thay đổi trạng thái Admin gốc."));
            }
            if (reactivate && account.Status != AccountStatus.Deactivated
                || deactivate && account.Status == AccountStatus.Deactivated)
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<AccountSummaryDto>(ApplicationError.Conflict(
                    "ACCOUNT.INVALID_TRANSITION", "Trạng thái tài khoản không phù hợp với thao tác."));
            }
            if (deactivate && account.Status == AccountStatus.Active
                && account.AccountRoles.Any(ar => ar.RoleId == Role.AdminRoleId)
                && await unitOfWork.AuthRepository.CountActiveAdminsAsync(cancellationToken) <= 1)
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<AccountSummaryDto>(ApplicationError.Conflict(
                    "ACCOUNT.LAST_ADMIN", "Không thể vô hiệu hóa Admin cuối cùng."));
            }

            account.Status = deactivate ? AccountStatus.Deactivated
                : account.EmailVerifiedAt is null ? AccountStatus.PendingActivation : AccountStatus.Active;
            if (reactivate)
            {
                account.FailedLoginAttempts = 0;
                account.LockoutEndAt = null;
            }
            await unitOfWork.SaveChangesAsync(cancellationToken);
            if (deactivate)
                await unitOfWork.AuthRepository.RevokeSessionsAsync(accountId, DateTimeOffset.UtcNow, cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
            return Result.Success(MapToSummary(account));
        }
        catch (DbUpdateConcurrencyException)
        {
            await unitOfWork.RollbackTransactionAsync(cancellationToken);
            return Result.Failure<AccountSummaryDto>(ApplicationError.Conflict(
                "ACCOUNT.CONCURRENT_UPDATE", "Tài khoản vừa được thay đổi. Vui lòng tải lại."));
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    public async Task<Result<EmployeeProfileDto>> GetEmployeeProfileAsync(Guid requesterAccountId,
        Guid accountId, CancellationToken cancellationToken = default)
    {
        var requester = await unitOfWork.AuthRepository.GetByIdAsync(requesterAccountId, cancellationToken);
        if (requester is null || requester.Status != AccountStatus.Active || requester.EmailVerifiedAt is null)
            return Result.Failure<EmployeeProfileDto>(ApplicationError.Unauthorized(
                "ACCOUNT.UNAUTHORIZED", "Tài khoản không còn hiệu lực."));
        var isAdmin = requester.AccountRoles.Any(ar => ar.RoleId == Role.AdminRoleId);
        if (!isAdmin && !requester.AccountRoles.Any(ar => ar.RoleId == Role.ManagerRoleId))
            return Result.Failure<EmployeeProfileDto>(ApplicationError.Forbidden(
                "ACCOUNT.FORBIDDEN", "Bạn không có quyền xem nhân viên."));
        var account = await unitOfWork.AuthRepository.GetByIdAsync(accountId, cancellationToken);
        if (account?.EmployeeProfile is null || (!isAdmin && !IsStaffOnly(account)))
            return Result.Failure<EmployeeProfileDto>(ApplicationError.NotFound(
                "ACCOUNT.NOT_FOUND", "Không tìm thấy nhân viên."));
        return Result.Success(MapEmployeeProfile(account));
    }

    public async Task<Result<EmployeeProfileDto>> UpdateEmployeeProfileAsync(Guid requesterAccountId,
        Guid accountId, UpdateEmployeeProfileRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.FullName) || string.IsNullOrWhiteSpace(request.PhoneNumber)
            || string.IsNullOrWhiteSpace(request.JobTitle) || request.HiredOn is null)
            return Result.Failure<EmployeeProfileDto>(ApplicationError.Validation(
                "ACCOUNT.INVALID_INPUT", "Thông tin nhân viên chưa đầy đủ."));

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await unitOfWork.AuthRepository.LockAccountAsync(accountId, cancellationToken);
            var requester = await unitOfWork.AuthRepository.GetByIdAsync(requesterAccountId, cancellationToken);
            if (requester is null || requester.Status != AccountStatus.Active || requester.EmailVerifiedAt is null)
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<EmployeeProfileDto>(ApplicationError.Unauthorized(
                    "ACCOUNT.UNAUTHORIZED", "Tài khoản không còn hiệu lực."));
            }
            var isAdmin = requester.AccountRoles.Any(ar => ar.RoleId == Role.AdminRoleId);
            if (!isAdmin && !requester.AccountRoles.Any(ar => ar.RoleId == Role.ManagerRoleId))
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<EmployeeProfileDto>(ApplicationError.Forbidden(
                    "ACCOUNT.FORBIDDEN", "Bạn không có quyền sửa nhân viên."));
            }
            var account = await unitOfWork.AuthRepository.GetByIdAsync(accountId, cancellationToken);
            if (account?.EmployeeProfile is null || (!isAdmin && !IsStaffOnly(account)))
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<EmployeeProfileDto>(ApplicationError.NotFound(
                    "ACCOUNT.NOT_FOUND", "Không tìm thấy nhân viên."));
            }
            account.FullName = request.FullName.Trim();
            account.PhoneNumber = request.PhoneNumber.Trim();
            account.EmployeeProfile.JobTitle = request.JobTitle.Trim();
            account.EmployeeProfile.HiredOn = request.HiredOn.Value;
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
            return Result.Success(MapEmployeeProfile(account));
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await unitOfWork.RollbackTransactionAsync(cancellationToken);
            return Result.Failure<EmployeeProfileDto>(ApplicationError.Conflict(
                "ACCOUNT.PHONE_EXISTS", "Số điện thoại đã được sử dụng."));
        }
        catch (DbUpdateConcurrencyException)
        {
            await unitOfWork.RollbackTransactionAsync(cancellationToken);
            return Result.Failure<EmployeeProfileDto>(ApplicationError.Conflict(
                "ACCOUNT.CONCURRENT_UPDATE", "Tài khoản vừa được thay đổi. Vui lòng tải lại."));
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    public async Task<Result<AccountSummaryDto>> UpdateRolesAsync(
        Guid requesterAccountId,
        Guid accountId,
        UpdateAccountRolesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Roles is null || request.Roles.Count == 0
            || request.Roles.Any(string.IsNullOrWhiteSpace))
        {
            return Result.Failure<AccountSummaryDto>(
                ApplicationError.Validation("ACCOUNT.INVALID_ROLES", "Cần chọn ít nhất một vai trò."));
        }

        var roleIds = new HashSet<Guid>();
        foreach (var code in request.Roles)
        {
            var roleId = code.Trim().ToUpperInvariant() switch
            {
                "ADMIN" => Role.AdminRoleId,
                "MANAGER" => Role.ManagerRoleId,
                "STAFF" => Role.StaffRoleId,
                _ => Guid.Empty
            };
            if (roleId == Guid.Empty || !roleIds.Add(roleId))
            {
                return Result.Failure<AccountSummaryDto>(
                    ApplicationError.Validation("ACCOUNT.INVALID_ROLES", "Vai trò không hợp lệ hoặc bị lặp."));
            }
        }

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            // Use the same lock order as account locking to protect the last active Admin.
            await unitOfWork.AuthRepository.LockRoleAsync(Role.AdminRoleId, cancellationToken);
            await unitOfWork.AuthRepository.LockAccountAsync(accountId, cancellationToken);
            var requester = await unitOfWork.AuthRepository.GetByIdAsync(requesterAccountId, cancellationToken);
            if (requester is null || requester.Status != AccountStatus.Active
                || requester.EmailVerifiedAt is null
                || !requester.AccountRoles.Any(ar => ar.RoleId == Role.AdminRoleId))
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<AccountSummaryDto>(
                    ApplicationError.Forbidden("ACCOUNT.FORBIDDEN", "Bạn không có quyền gán vai trò."));
            }

            var account = await unitOfWork.AuthRepository.GetByIdAsync(accountId, cancellationToken);
            if (account is null)
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<AccountSummaryDto>(
                    ApplicationError.NotFound("ACCOUNT.NOT_FOUND", "Không tìm thấy tài khoản."));
            }
            if (account.AccountType != AccountType.Internal)
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<AccountSummaryDto>(
                    ApplicationError.Conflict("ACCOUNT.ROLE_TYPE_MISMATCH", "Chỉ tài khoản nội bộ được gán vai trò Admin, Manager hoặc Staff."));
            }
            if (IsRootAdmin(account))
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<AccountSummaryDto>(ApplicationError.Conflict(
                    "ACCOUNT.PROTECTED_ADMIN", "Không thể thay đổi vai trò Admin gốc."));
            }

            var currentRoleIds = account.AccountRoles.Select(ar => ar.RoleId).ToHashSet();
            if (currentRoleIds.SetEquals(roleIds))
            {
                await unitOfWork.CommitTransactionAsync(cancellationToken);
                return Result.Success(MapToSummary(account));
            }
            if (currentRoleIds.Contains(Role.AdminRoleId) && !roleIds.Contains(Role.AdminRoleId)
                && account.Status == AccountStatus.Active && account.EmailVerifiedAt is not null
                && await unitOfWork.AuthRepository.CountActiveAdminsAsync(cancellationToken) <= 1)
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<AccountSummaryDto>(
                    ApplicationError.Conflict("ACCOUNT.LAST_ADMIN", "Không thể gỡ vai trò Admin cuối cùng."));
            }

            unitOfWork.AuthRepository.ReplaceAccountRoles(account, roleIds);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.AuthRepository.RevokeSessionsAsync(accountId, DateTimeOffset.UtcNow, cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
            return Result.Success(MapToSummary(account));
        }
        catch (DbUpdateConcurrencyException)
        {
            await unitOfWork.RollbackTransactionAsync(cancellationToken);
            return Result.Failure<AccountSummaryDto>(
                ApplicationError.Conflict("ACCOUNT.CONCURRENT_UPDATE", "Tài khoản vừa được thay đổi. Vui lòng tải lại."));
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    public async Task<Result<AccountSummaryDto>> ChangeLockStatusAsync(
        Guid requesterAccountId,
        Guid accountId,
        ChangeAccountLockRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Locked is null)
        {
            return Result.Failure<AccountSummaryDto>(
                ApplicationError.Validation("ACCOUNT.INVALID_STATUS", "Cần chọn khóa hoặc mở khóa tài khoản."));
        }

        var requester = await unitOfWork.AuthRepository.GetByIdAsync(requesterAccountId, cancellationToken);
        if (requester is null || requester.Status != AccountStatus.Active || requester.EmailVerifiedAt is null)
        {
            return Result.Failure<AccountSummaryDto>(
                ApplicationError.Unauthorized("ACCOUNT.UNAUTHORIZED", "Tài khoản không còn hiệu lực."));
        }

        var isAdmin = requester.AccountRoles.Any(ar => ar.RoleId == Role.AdminRoleId);
        var isManager = requester.AccountRoles.Any(ar => ar.RoleId == Role.ManagerRoleId);
        if (!isAdmin && !isManager)
        {
            return Result.Failure<AccountSummaryDto>(
                ApplicationError.Forbidden("ACCOUNT.FORBIDDEN", "Bạn không có quyền thay đổi trạng thái tài khoản."));
        }

        if (requesterAccountId == accountId)
        {
            return Result.Failure<AccountSummaryDto>(
                ApplicationError.Conflict("ACCOUNT.SELF_LOCK", "Không thể thay đổi trạng thái khóa của chính mình."));
        }

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            // Serialize admin lock changes before checking whether an active admin remains.
            await unitOfWork.AuthRepository.LockRoleAsync(Role.AdminRoleId, cancellationToken);
            await unitOfWork.AuthRepository.LockAccountAsync(accountId, cancellationToken);
            var account = await unitOfWork.AuthRepository.GetByIdAsync(accountId, cancellationToken);
            if (account is null || (!isAdmin && !IsStaffOnly(account)))
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<AccountSummaryDto>(
                    ApplicationError.NotFound("ACCOUNT.NOT_FOUND", "Không tìm thấy tài khoản."));
            }
            if (IsRootAdmin(account))
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<AccountSummaryDto>(ApplicationError.Conflict(
                    "ACCOUNT.PROTECTED_ADMIN", "Không thể khóa hoặc mở khóa Admin gốc."));
            }

            if (request.Locked.Value && account.Status != AccountStatus.Active
                || !request.Locked.Value && account.Status != AccountStatus.Locked)
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<AccountSummaryDto>(
                    ApplicationError.Conflict("ACCOUNT.INVALID_TRANSITION", "Trạng thái tài khoản không phù hợp với thao tác."));
            }

            if (request.Locked.Value && account.AccountRoles.Any(ar => ar.RoleId == Role.AdminRoleId)
                && await unitOfWork.AuthRepository.CountActiveAdminsAsync(cancellationToken) <= 1)
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<AccountSummaryDto>(
                    ApplicationError.Conflict("ACCOUNT.LAST_ADMIN", "Không thể khóa Admin cuối cùng."));
            }

            if (!request.Locked.Value && account.EmailVerifiedAt is null)
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<AccountSummaryDto>(
                    ApplicationError.Conflict("ACCOUNT.EMAIL_UNVERIFIED", "Tài khoản chưa xác thực email."));
            }

            account.Status = request.Locked.Value ? AccountStatus.Locked : AccountStatus.Active;
            if (!request.Locked.Value)
            {
                account.FailedLoginAttempts = 0;
                account.LockoutEndAt = null;
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            if (request.Locked.Value)
            {
                await unitOfWork.AuthRepository.RevokeSessionsAsync(
                    accountId, DateTimeOffset.UtcNow, cancellationToken);
            }
            await unitOfWork.CommitTransactionAsync(cancellationToken);
            return Result.Success(MapToSummary(account));
        }
        catch (DbUpdateConcurrencyException)
        {
            await unitOfWork.RollbackTransactionAsync(cancellationToken);
            return Result.Failure<AccountSummaryDto>(
                ApplicationError.Conflict("ACCOUNT.CONCURRENT_UPDATE", "Tài khoản vừa được thay đổi. Vui lòng tải lại."));
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    public Task<Result<AccountSummaryDto>> CreateStaffAccountAsync(
        Guid requesterAccountId,
        CreateStaffAccountRequest request,
        CancellationToken cancellationToken = default) =>
        CreateInternalCoreAsync(requesterAccountId, request, Role.StaffRoleId, false, cancellationToken);

    private async Task<Result<AccountSummaryDto>> CreateInternalCoreAsync(
        Guid requesterAccountId,
        CreateStaffAccountRequest request,
        Guid roleId,
        bool adminRequired,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requester = await unitOfWork.AuthRepository.GetByIdAsync(requesterAccountId, cancellationToken);
        if (requester is null || requester.Status != AccountStatus.Active || requester.EmailVerifiedAt is null)
        {
            return Result.Failure<AccountSummaryDto>(
                ApplicationError.Unauthorized("ACCOUNT.UNAUTHORIZED", "Tài khoản không còn hiệu lực."));
        }

        if (!requester.AccountRoles.Any(ar => ar.RoleId == Role.AdminRoleId)
            && (adminRequired || !requester.AccountRoles.Any(ar => ar.RoleId == Role.ManagerRoleId)))
        {
            return Result.Failure<AccountSummaryDto>(
                ApplicationError.Forbidden("ACCOUNT.FORBIDDEN", "Bạn không có quyền tạo tài khoản nhân viên."));
        }

        var useDefaultPassword = request.UseDefaultPassword ?? request.Password is null;
        if (useDefaultPassword && request.Password is not null
            || !useDefaultPassword && (string.IsNullOrEmpty(request.Password)
                || Encoding.UTF8.GetByteCount(request.Password) > 72
                || !new RegularExpressionAttribute(PasswordPattern).IsValid(request.Password)))
        {
            return Result.Failure<AccountSummaryDto>(ApplicationError.Validation(
                "ACCOUNT.INVALID_PASSWORD", "Chọn mật khẩu mặc định hoặc nhập mật khẩu tự đặt hợp lệ."));
        }

        var password = useDefaultPassword ? DefaultInternalPassword : request.Password!;

        var email = request.Email.Trim();
        if (EmailIdentity.Normalize(email) is null)
        {
            return Result.Failure<AccountSummaryDto>(
                ApplicationError.Validation("ACCOUNT.INVALID_EMAIL", "Địa chỉ email không đúng định dạng được hỗ trợ."));
        }

        var username = request.Username.Trim();
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(request.FullName)
            || string.IsNullOrWhiteSpace(request.PhoneNumber) || string.IsNullOrWhiteSpace(request.JobTitle)
            || request.HiredOn is null)
        {
            return Result.Failure<AccountSummaryDto>(
                ApplicationError.Validation("ACCOUNT.INVALID_INPUT", "Thông tin nhân viên chưa đầy đủ."));
        }

        if (await unitOfWork.AuthRepository.ExistsEmailAsync(email, cancellationToken)
            || await unitOfWork.AuthRepository.ExistsUsernameAsync(username, cancellationToken))
        {
            return Result.Failure<AccountSummaryDto>(
                ApplicationError.Conflict("ACCOUNT.IDENTITY_EXISTS", "Email hoặc tên đăng nhập đã được sử dụng."));
        }

        var accountId = Guid.NewGuid();
        var account = new Account
        {
            Id = accountId,
            FullName = request.FullName.Trim(),
            Email = email,
            PhoneNumber = request.PhoneNumber.Trim(),
            Username = username,
            PasswordHash = passwordHasher.HashPassword(password),
            AccountType = AccountType.Internal,
            Status = AccountStatus.PendingActivation,
            CreatedByAccountId = requesterAccountId
        };
        account.EmployeeProfile = new EmployeeProfile
        {
            Id = accountId,
            Account = account,
            JobTitle = request.JobTitle.Trim(),
            HiredOn = request.HiredOn.Value,
            Status = EmployeeStatus.Working
        };
        account.AccountRoles.Add(new AccountRole
        {
            AccountId = accountId,
            Account = account,
            RoleId = roleId
        });

        unitOfWork.AuthRepository.AddAccount(account);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Result.Failure<AccountSummaryDto>(
                ApplicationError.Conflict("ACCOUNT.IDENTITY_EXISTS", "Email, tên đăng nhập hoặc số điện thoại đã được sử dụng."));
        }

        var roleCode = roleId == Role.AdminRoleId ? "Admin"
            : roleId == Role.ManagerRoleId ? "Manager" : "Staff";
        return Result.Success(new AccountSummaryDto(
            account.Id, account.FullName, account.Email, account.PhoneNumber,
            account.AccountType.ToString(), account.Status.ToString(), [roleCode], account.CreatedAt,
            Username: account.Username));
    }

    public async Task<Result<AccountSummaryDto>> GetAccountByIdAsync(
        Guid requesterAccountId,
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var requester = await unitOfWork.AuthRepository.GetByIdAsync(requesterAccountId, cancellationToken);
        if (requester is null || requester.Status != AccountStatus.Active || requester.EmailVerifiedAt is null)
        {
            return Result.Failure<AccountSummaryDto>(
                ApplicationError.Unauthorized("ACCOUNT.UNAUTHORIZED", "Tài khoản không còn hiệu lực."));
        }

        var isAdmin = requester.AccountRoles.Any(ar => ar.RoleId == Role.AdminRoleId);
        var isManager = requester.AccountRoles.Any(ar => ar.RoleId == Role.ManagerRoleId);
        if (!isAdmin && !isManager)
        {
            return Result.Failure<AccountSummaryDto>(
                ApplicationError.Forbidden("ACCOUNT.FORBIDDEN", "Bạn không có quyền xem tài khoản."));
        }

        var account = await unitOfWork.AuthRepository.GetByIdAsync(accountId, cancellationToken);
        if (account is null || (!isAdmin && !IsStaffOnly(account)))
        {
            return Result.Failure<AccountSummaryDto>(
                ApplicationError.NotFound("ACCOUNT.NOT_FOUND", "Không tìm thấy tài khoản."));
        }

        return Result.Success(MapToSummary(account));
    }

    public async Task<Result<PagedResponse<AccountSummaryDto>>> GetPagedAccountsAsync(
        Guid requesterAccountId,
        PagedRequest paging,
        string? search,
        AccountType? accountType,
        AccountStatus? status,
        string? role,
        CancellationToken cancellationToken = default)
    {
        Guid? roleId = role?.Trim().ToUpperInvariant() switch
        {
            null or "" => null,
            "ADMIN" => Role.AdminRoleId,
            "MANAGER" => Role.ManagerRoleId,
            "STAFF" => Role.StaffRoleId,
            "CUSTOMER" => Role.CustomerRoleId,
            _ => Guid.Empty
        };
        if (roleId == Guid.Empty)
            return Result.Failure<PagedResponse<AccountSummaryDto>>(ApplicationError.Validation(
                "ACCOUNT.INVALID_ROLE", "Vai trò lọc phải là Admin, Manager, Staff hoặc Customer."));

        var requester = await unitOfWork.AuthRepository.GetListRequesterAsync(requesterAccountId, cancellationToken);
        if (requester is null || requester.Status != AccountStatus.Active || requester.EmailVerifiedAt is null)
        {
            return Result.Failure<PagedResponse<AccountSummaryDto>>(
                ApplicationError.Unauthorized("ACCOUNT.UNAUTHORIZED", "Tài khoản không còn hiệu lực."));
        }

        var isAdmin = requester.AccountRoles.Any(ar => ar.RoleId == Role.AdminRoleId);
        var isManager = requester.AccountRoles.Any(ar => ar.RoleId == Role.ManagerRoleId);
        if (!isAdmin && !isManager)
        {
            return Result.Failure<PagedResponse<AccountSummaryDto>>(
                ApplicationError.Forbidden("ACCOUNT.FORBIDDEN", "Bạn không có quyền xem danh sách tài khoản."));
        }

        var page = await unitOfWork.AuthRepository.GetPagedAccountsAsync(
            paging, search, accountType, status, staffOnly: !isAdmin, roleId, cancellationToken);
        var items = page.Items.Select(MapToSummary).ToList();
        return Result.Success(new PagedResponse<AccountSummaryDto>(
            items, page.Page, page.PageSize, page.TotalCount));
    }

    private static AccountSummaryDto MapToSummary(Account account) => new(
        account.Id,
        account.FullName,
        account.Email,
        account.PhoneNumber,
        account.AccountType.ToString(),
        account.Status.ToString(),
        account.AccountRoles
            .Select(ar => ar.Role?.Code ?? (ar.RoleId == Role.AdminRoleId ? "Admin"
                : ar.RoleId == Role.ManagerRoleId ? "Manager"
                : ar.RoleId == Role.StaffRoleId ? "Staff"
                : ar.RoleId == Role.CustomerRoleId ? "Customer" : null))
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code!)
            .Distinct(StringComparer.Ordinal)
            .ToList(),
        account.CreatedAt,
        IsRootAdmin(account),
        account.Username);

    private static EmployeeProfileDto MapEmployeeProfile(Account account)
    {
        var profile = account.EmployeeProfile!;
        return new EmployeeProfileDto(account.Id, account.FullName, account.Email,
            account.PhoneNumber, account.Username, profile.JobTitle, profile.HiredOn,
            profile.LeftOn, profile.Status.ToString(), account.Status.ToString(),
            MapToSummary(account).Roles);
    }

    private static bool IsStaffOnly(Account account) =>
        account.AccountType == AccountType.Internal
        && account.AccountRoles.Any(ar => ar.RoleId == Role.StaffRoleId)
        && !account.AccountRoles.Any(ar => ar.RoleId == Role.AdminRoleId || ar.RoleId == Role.ManagerRoleId);

    private static bool IsRootAdmin(Account account) =>
        account.AccountType == AccountType.Internal
        && string.Equals(account.Username, "ocs_admin", StringComparison.OrdinalIgnoreCase);
}
