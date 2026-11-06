using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OCS.Api.Common;
using OCS.Api.Authorization;
using OCS.Api.Consts;
using OCS.Api.Extensions;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Accounts;

namespace OCS.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Manager")]
public sealed class AccountsController(IAccountManagementService accountManagementService) : ControllerBase
{
    [HttpPost("{id:guid}/reset-password")]
    [Authorize(Roles = "Admin")]
    [Authorize(Policy = PermissionCatalog.AccountsResetPassword)]
    public async Task<IActionResult> ResetPassword(Guid id, [FromBody] ResetAccountPasswordRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var requesterAccountId))
            return Unauthorized();
        var result = await accountManagementService.ResetPasswordAsync(
            requesterAccountId, id, request, cancellationToken);
        return result.IsSuccess ? NoContent() : result.ToActionResult(this);
    }

    [HttpGet("history")]
    [Authorize(Roles = "Admin")]
    [Authorize(Policy = PermissionCatalog.AccountsHistory)]
    public async Task<IActionResult> GetHistory([FromQuery] PagedRequest paging,
        [FromQuery] Guid? accountId, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var requesterAccountId))
            return Unauthorized();
        var result = await accountManagementService.GetAccountHistoryAsync(
            requesterAccountId, paging, accountId, cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [Authorize(Policy = PermissionCatalog.AccountsUpdate)]
    public async Task<IActionResult> UpdateAccountInfo(Guid id, [FromBody] UpdateAccountInfoRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var requesterAccountId))
            return Unauthorized();
        var result = await accountManagementService.UpdateAccountInfoAsync(
            requesterAccountId, id, request, cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpPost("internal")]
    [Authorize(Roles = "Admin")]
    [Authorize(Policy = PermissionCatalog.AccountsCreate)]
    public async Task<IActionResult> CreateInternal([FromBody] CreateInternalAccountRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var requesterAccountId))
            return Unauthorized();
        var result = await accountManagementService.CreateInternalAccountAsync(
            requesterAccountId, request, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Data!.Id }, result.Data)
            : result.ToActionResult(this);
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = PermissionCatalog.AccountsStatus)]
    public async Task<IActionResult> ChangeStatus(Guid id, [FromBody] ChangeAccountStatusRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var requesterAccountId))
            return Unauthorized();
        var result = await accountManagementService.ChangeStatusAsync(
            requesterAccountId, id, request, cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpGet("{id:guid}/employee-profile")]
    [Authorize(Policy = PermissionCatalog.AccountsRead)]
    public async Task<IActionResult> GetEmployeeProfile(Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var requesterAccountId))
            return Unauthorized();
        var result = await accountManagementService.GetEmployeeProfileAsync(
            requesterAccountId, id, cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpPatch("{id:guid}/employee-profile")]
    [Authorize(Policy = PermissionCatalog.AccountsUpdate)]
    public async Task<IActionResult> UpdateEmployeeProfile(Guid id,
        [FromBody] UpdateEmployeeProfileRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var requesterAccountId))
            return Unauthorized();
        var result = await accountManagementService.UpdateEmployeeProfileAsync(
            requesterAccountId, id, request, cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpPut("{id:guid}/roles")]
    [Authorize(Roles = "Admin")]
    [Authorize(Policy = PermissionCatalog.AccountsRoles)]
    public async Task<IActionResult> UpdateRoles(
        Guid id,
        [FromBody] UpdateAccountRolesRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var requesterAccountId))
        {
            return Result.Failure<AccountSummaryDto>(
                ApplicationError.Unauthorized("ACCOUNT.UNAUTHORIZED", "Phiên đăng nhập không hợp lệ."))
                .ToActionResult(this);
        }

        var result = await accountManagementService.UpdateRolesAsync(
            requesterAccountId, id, request, cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpPatch("{id:guid}/lock-status")]
    [Authorize(Policy = PermissionCatalog.AccountsStatus)]
    public async Task<IActionResult> ChangeLockStatus(
        Guid id,
        [FromBody] ChangeAccountLockRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var requesterAccountId))
        {
            return Result.Failure<AccountSummaryDto>(
                ApplicationError.Unauthorized("ACCOUNT.UNAUTHORIZED", "Phiên đăng nhập không hợp lệ."))
                .ToActionResult(this);
        }

        var result = await accountManagementService.ChangeLockStatusAsync(
            requesterAccountId, id, request, cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpPost("staff")]
    [Authorize(Policy = PermissionCatalog.AccountsCreate)]
    public async Task<IActionResult> CreateStaff(
        [FromBody] CreateStaffAccountRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var requesterAccountId))
        {
            return Result.Failure<AccountSummaryDto>(
                ApplicationError.Unauthorized("ACCOUNT.UNAUTHORIZED", "Phiên đăng nhập không hợp lệ."))
                .ToActionResult(this);
        }

        var result = await accountManagementService.CreateStaffAccountAsync(
            requesterAccountId, request, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Data!.Id }, result.Data)
            : result.ToActionResult(this);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.AccountsRead)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var requesterAccountId))
        {
            return Result.Failure<AccountSummaryDto>(
                ApplicationError.Unauthorized("ACCOUNT.UNAUTHORIZED", "Phiên đăng nhập không hợp lệ."))
                .ToActionResult(this);
        }

        var result = await accountManagementService.GetAccountByIdAsync(
            requesterAccountId, id, cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpGet]
    [Authorize(Policy = PermissionCatalog.AccountsRead)]
    public async Task<IActionResult> GetAccounts(
        [FromQuery] PagedRequest paging,
        [FromQuery] string? search,
        [FromQuery] AccountType? accountType,
        [FromQuery] AccountStatus? status,
        [FromQuery] string? role,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var requesterAccountId))
        {
            return Result.Failure<PagedResponse<AccountSummaryDto>>(
                ApplicationError.Unauthorized("ACCOUNT.UNAUTHORIZED", "Phiên đăng nhập không hợp lệ."))
                .ToActionResult(this);
        }

        var result = await accountManagementService.GetPagedAccountsAsync(
            requesterAccountId, paging, search, accountType, status, role, cancellationToken);
        return result.ToActionResult(this);
    }
}
