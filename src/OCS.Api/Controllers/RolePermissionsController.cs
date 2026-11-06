using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Authorization;

namespace OCS.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize(Roles = "Admin")]
public sealed class RolePermissionsController(AppDbContext dbContext) : ControllerBase
{
    private static readonly string[] RoleCodes = ["Admin", "Manager", "Staff", "Customer"];

    [HttpGet("permissions")]
    public async Task<ActionResult<IReadOnlyList<PermissionDto>>> GetPermissions(CancellationToken cancellationToken)
    {
        var permissions = await dbContext.Permissions
            .AsNoTracking()
            .OrderBy(permission => permission.Code)
            .ToListAsync(cancellationToken);

        return Ok(permissions.Select(permission => new PermissionDto(
            permission.Code,
            permission.Name,
            permission.Description,
            PermissionCatalog.RequiredAdminCodes.Contains(permission.Code),
            RoleCodes
                .Where(roleCode => PermissionCatalog.CanAssign(roleCode, permission.Code)).ToArray())).ToArray());
    }

    [HttpGet("roles")]
    public async Task<ActionResult<IReadOnlyList<RolePermissionsDto>>> GetRoles(CancellationToken cancellationToken)
    {
        var roles = await dbContext.Roles
            .AsNoTracking()
            .Include(role => role.RolePermissions)
            .ThenInclude(rolePermission => rolePermission.Permission)
            .OrderBy(role => role.Code)
            .ToListAsync(cancellationToken);

        return Ok(roles.Select(role => new RolePermissionsDto(
                role.Id,
                role.Code,
                role.Name,
                role.Description,
                (role.Code == "Admin"
                    ? role.RolePermissions.Select(rolePermission => rolePermission.Permission.Code)
                        .Concat(PermissionCatalog.RequiredAdminCodes)
                    : role.RolePermissions.Select(rolePermission => rolePermission.Permission.Code)).Distinct().Order().ToArray(),
                role.Code == "Admin")).ToArray());
    }

    [HttpPut("roles/{roleId:guid}/permissions")]
    public async Task<IActionResult> ReplaceRolePermissions(
        Guid roleId,
        [FromBody] ReplaceRolePermissionsRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"Roles\" WHERE \"Id\" = {roleId} FOR UPDATE", cancellationToken);
        var role = await dbContext.Roles
            .Include(item => item.RolePermissions)
            .ThenInclude(item => item.Permission)
            .FirstOrDefaultAsync(item => item.Id == roleId, cancellationToken);

        if (role is null)
        {
            return NotFound(new ProblemDetails { Title = "Không tìm thấy vai trò." });
        }

        if (request.ExpectedPermissionCodes is not null &&
            !request.ExpectedPermissionCodes.ToHashSet(StringComparer.Ordinal).SetEquals(
                role.RolePermissions.Select(item => item.Permission.Code).Concat(
                    role.Code == "Admin" ? PermissionCatalog.RequiredAdminCodes : [])))
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Phân quyền đã được thay đổi ở phiên khác. Vui lòng tải lại."
            });
        }

        if (request.PermissionCodes is null || request.PermissionCodes.Any(string.IsNullOrWhiteSpace))
        {
            return BadRequest(new ProblemDetails { Title = "Danh sách mã quyền không hợp lệ." });
        }

        var codes = request.PermissionCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var permissions = await dbContext.Permissions
            .Where(permission => codes.Contains(permission.Code))
            .ToListAsync(cancellationToken);

        if (permissions.Count != codes.Length)
        {
            var knownCodes = permissions.Select(permission => permission.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return BadRequest(new ProblemDetails
            {
                Title = "Danh sách quyền có mã không hợp lệ.",
                Detail = string.Join(", ", codes.Where(code => !knownCodes.Contains(code)))
            });
        }

        if (codes.Any(code => !PermissionCatalog.CanAssign(role.Code, code)))
        {
            return BadRequest(new ProblemDetails { Title = "Vai trò này không được gán một hoặc nhiều quyền đã chọn." });
        }

        if (role.Code == "Admin" && PermissionCatalog.RequiredAdminCodes.Any(code => !codes.Contains(code, StringComparer.Ordinal)))
        {
            return BadRequest(new ProblemDetails { Title = "Không thể bỏ các quyền quản trị cốt lõi của Admin." });
        }

        var requestedPermissionIds = permissions.Select(permission => permission.Id).ToHashSet();
        var removed = role.RolePermissions
            .Where(rolePermission => !requestedPermissionIds.Contains(rolePermission.PermissionId))
            .ToArray();
        dbContext.RolePermissions.RemoveRange(removed);
        foreach (var permission in permissions.Where(permission =>
                     role.RolePermissions.All(rolePermission => rolePermission.PermissionId != permission.Id)))
        {
            role.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(new RolePermissionsDto(role.Id, role.Code, role.Name, role.Description, codes, role.Code == "Admin"));
    }
}

public sealed record PermissionDto(string Code, string Name, string? Description, bool IsRequiredForAdmin, IReadOnlyList<string> AllowedRoleCodes);

public sealed record RolePermissionsDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    IReadOnlyList<string> PermissionCodes,
    bool IsProtected);

public sealed record ReplaceRolePermissionsRequest(string[] PermissionCodes, string[]? ExpectedPermissionCodes = null);
