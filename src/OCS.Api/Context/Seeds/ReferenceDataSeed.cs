using Microsoft.EntityFrameworkCore;
using OCS.Api.Entities;
using OCS.Api.Authorization;

namespace OCS.Api.Context.Seeds;

// Only shared role and permission definitions belong in the migration model.
internal static class ReferenceDataSeed
{
    private static readonly Guid ManagerRoleId = new("20000000-0000-0000-0000-000000000001");
    private static readonly Guid StaffRoleId = new("20000000-0000-0000-0000-000000000002");
    private static readonly Guid AdminRoleId = new("20000000-0000-0000-0000-000000000003");
    private static readonly Guid CustomerRoleId = new("20000000-0000-0000-0000-000000000004");

    public static void Configure(ModelBuilder modelBuilder) => SeedRolesAndPermissions(modelBuilder);

    private static void SeedRolesAndPermissions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Role>().HasData(
            new
            {
                Id = AdminRoleId,
                Code = "Admin",
                Name = "Quản trị viên",
                Description = "Vai trò quản trị hệ thống nội bộ."
            },
            new
            {
                Id = ManagerRoleId,
                Code = "Manager",
                Name = "Quản lý",
                Description = "Vai trò mẫu cho tài khoản quản lý nội bộ."
            },
            new
            {
                Id = StaffRoleId,
                Code = "Staff",
                Name = "Nhân viên",
                Description = "Vai trò mẫu cho nhân viên sử dụng POS."
            },
            new
            {
                Id = CustomerRoleId,
                Code = "Customer",
                Name = "Khách hàng",
                Description = "Vai trò khách hàng sử dụng mini app để đặt lịch ký gửi."
            });

        modelBuilder.Entity<Permission>().HasData(PermissionCatalog.All.Select(permission => new
        {
            Id = PermissionCatalog.IdFor(permission.Number),
            permission.Code,
            permission.Name,
            permission.Description
        }).ToArray());

        // Each role receives explicit sample permissions; Customer gets no internal permissions.
        var grants = new List<object>();
        void Grant(Guid roleId, params string[] codes)
        {
            foreach (var code in codes)
            {
                var permission = PermissionCatalog.All.Single(item => item.Code == code);
                grants.Add(new { RoleId = roleId, PermissionId = PermissionCatalog.IdFor(permission.Number) });
            }
        }

        Grant(AdminRoleId, PermissionCatalog.DefaultsFor("Admin").ToArray());
        Grant(ManagerRoleId, PermissionCatalog.DefaultsFor("Manager").ToArray());
        Grant(StaffRoleId, PermissionCatalog.DefaultsFor("Staff").ToArray());
        Grant(CustomerRoleId, PermissionCatalog.DefaultsFor("Customer").ToArray());
        modelBuilder.Entity<RolePermission>().HasData(grants.ToArray());
    }

}

