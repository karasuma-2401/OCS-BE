using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using OCS.Api.Authorization;
using OCS.Api.Controllers;

namespace OCS.Api.IntegrationTests;

public sealed class PermissionConfigurationTests
{
    [Fact]
    public void CatalogHasUniqueCodesAndStableIds()
    {
        Assert.Equal(PermissionCatalog.All.Count, PermissionCatalog.All.Select(item => item.Code).Distinct().Count());
        Assert.Equal(PermissionCatalog.All.Count, PermissionCatalog.All.Select(item => PermissionCatalog.IdFor(item.Number)).Distinct().Count());
        Assert.All(PermissionCatalog.All, item => Assert.True(PermissionCatalog.KnownCodes.Contains(item.Code)));
    }

    [Fact]
    public void AdminRightsAreReservedAndStaffCannotGainAccountManagement()
    {
        Assert.Equal(5, PermissionCatalog.RequiredAdminCodes.Count);
        Assert.Contains(PermissionCatalog.ScreenPos, PermissionCatalog.DefaultsFor("Manager"));
        Assert.Contains(PermissionCatalog.ScreenAccounts, PermissionCatalog.DefaultsFor("Manager"));
        Assert.Contains(PermissionCatalog.AccountsRead, PermissionCatalog.DefaultsFor("Manager"));
        Assert.Contains(PermissionCatalog.ScreenPos, PermissionCatalog.DefaultsFor("Staff"));
        Assert.All(PermissionCatalog.RequiredAdminCodes, code => Assert.True(PermissionCatalog.CanAssign("Admin", code)));
        Assert.False(PermissionCatalog.CanAssign("Admin", PermissionCatalog.AppointmentsCreate));
        Assert.False(PermissionCatalog.CanAssign("Manager", PermissionCatalog.ManagePermissions));
        Assert.False(PermissionCatalog.CanAssign("Staff", PermissionCatalog.ScreenAccounts));
        Assert.False(PermissionCatalog.CanAssign("Staff", PermissionCatalog.AccountsRoles));
        Assert.False(PermissionCatalog.CanAssign("Customer", PermissionCatalog.ScreenPos));
        Assert.True(PermissionCatalog.CanAssign("Customer", PermissionCatalog.AppointmentsCreate));
    }

    [Fact]
    public void SeededRolePermissionsAreWithinEachRolesAssignmentBoundary()
    {
        foreach (var (roleCode, codes) in PermissionCatalog.DefaultRolePermissions)
        {
            Assert.NotEmpty(codes);
            Assert.All(codes, code => Assert.True(PermissionCatalog.CanAssign(roleCode, code)));
        }

        Assert.Contains(PermissionCatalog.ScreenPos, PermissionCatalog.DefaultsFor("Manager"));
        Assert.Contains(PermissionCatalog.ScreenAccounts, PermissionCatalog.DefaultsFor("Manager"));
        Assert.Contains(PermissionCatalog.AccountsRead, PermissionCatalog.DefaultsFor("Manager"));
        Assert.DoesNotContain(PermissionCatalog.ScreenAccounts, PermissionCatalog.DefaultsFor("Staff"));
    }

    [Fact]
    public void ManagedAccountActionsRequireFunctionPermissions()
    {
        var actions = typeof(AccountsController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>().Any())
            .ToArray();

        Assert.NotEmpty(actions);
        Assert.All(actions, action => Assert.Contains(action.GetCustomAttributes<AuthorizeAttribute>(),
            attribute => attribute.Policy is not null && PermissionCatalog.KnownCodes.Contains(attribute.Policy)));
    }
}
