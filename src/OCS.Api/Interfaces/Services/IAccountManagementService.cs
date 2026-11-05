using OCS.Api.Common;
using OCS.Api.Consts;
using OCS.Api.Models.Accounts;

namespace OCS.Api.Interfaces.Services;

public interface IAccountManagementService
{
    Task<Result<bool>> ResetPasswordAsync(Guid requesterAccountId, Guid accountId,
        ResetAccountPasswordRequest request, CancellationToken cancellationToken = default);

    Task<Result<PagedResponse<AccountAuditLogDto>>> GetAccountHistoryAsync(Guid requesterAccountId,
        PagedRequest paging, Guid? accountId, CancellationToken cancellationToken = default);

    Task<Result<AccountSummaryDto>> UpdateAccountInfoAsync(Guid requesterAccountId, Guid accountId,
        UpdateAccountInfoRequest request, CancellationToken cancellationToken = default);

    Task<Result<AccountSummaryDto>> CreateInternalAccountAsync(Guid requesterAccountId,
        CreateInternalAccountRequest request, CancellationToken cancellationToken = default);

    Task<Result<AccountSummaryDto>> ChangeStatusAsync(Guid requesterAccountId, Guid accountId,
        ChangeAccountStatusRequest request, CancellationToken cancellationToken = default);

    Task<Result<EmployeeProfileDto>> GetEmployeeProfileAsync(Guid requesterAccountId, Guid accountId,
        CancellationToken cancellationToken = default);

    Task<Result<EmployeeProfileDto>> UpdateEmployeeProfileAsync(Guid requesterAccountId, Guid accountId,
        UpdateEmployeeProfileRequest request, CancellationToken cancellationToken = default);

    Task<Result<AccountSummaryDto>> UpdateRolesAsync(
        Guid requesterAccountId,
        Guid accountId,
        UpdateAccountRolesRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<AccountSummaryDto>> ChangeLockStatusAsync(
        Guid requesterAccountId,
        Guid accountId,
        ChangeAccountLockRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<AccountSummaryDto>> CreateStaffAccountAsync(
        Guid requesterAccountId,
        CreateStaffAccountRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<AccountSummaryDto>> GetAccountByIdAsync(
        Guid requesterAccountId,
        Guid accountId,
        CancellationToken cancellationToken = default);

    Task<Result<PagedResponse<AccountSummaryDto>>> GetPagedAccountsAsync(
        Guid requesterAccountId,
        PagedRequest paging,
        string? search,
        AccountType? accountType,
        AccountStatus? status,
        string? role,
        CancellationToken cancellationToken = default);
}
