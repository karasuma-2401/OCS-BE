using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using OCS.Api.Interfaces.Repositories;

namespace OCS.Api.Authorization;

public sealed record PermissionRequirement(string Code) : IAuthorizationRequirement;

public sealed class PermissionAuthorizationHandler(IAuthRepository authRepository)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User.IsInRole("Admin") && PermissionCatalog.RequiredAdminCodes.Contains(requirement.Code))
        {
            context.Succeed(requirement);
            return;
        }

        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId)) return;
        var permissions = await authRepository.GetEffectivePermissionsAsync(accountId);
        if (permissions.Contains(requirement.Code, StringComparer.Ordinal)) context.Succeed(requirement);
    }
}
