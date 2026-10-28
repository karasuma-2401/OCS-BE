using Microsoft.EntityFrameworkCore;
using OCS.Api.Entities;

namespace OCS.Api.Context.Seeds;

// Only shared role and permission definitions belong in the migration model.
internal static class ReferenceDataSeed
{
    private static readonly Guid ManagerRoleId = new("20000000-0000-0000-0000-000000000001");
    private static readonly Guid StaffRoleId = new("20000000-0000-0000-0000-000000000002");
    private static readonly Guid AdminRoleId = new("20000000-0000-0000-0000-000000000003");
    private static readonly Guid CustomerRoleId = new("20000000-0000-0000-0000-000000000004");
    private static readonly Guid ReadCustomersPermissionId = new("30000000-0000-0000-0000-000000000001");
    private static readonly Guid CreateAccountsPermissionId = new("30000000-0000-0000-0000-000000000002");
    private static readonly Guid CreateAppointmentsPermissionId = new("30000000-0000-0000-0000-000000000003");

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

        modelBuilder.Entity<Permission>().HasData(
            new
            {
                Id = ReadCustomersPermissionId,
                Code = "customers.read",
                Name = "Xem hồ sơ khách",
                Description = "Tra cứu hồ sơ phục vụ tiếp nhận tại cửa hàng."
            },
            new
            {
                Id = CreateAccountsPermissionId,
                Code = "accounts.create",
                Name = "Cấp tài khoản nội bộ",
                Description = "Quản lý cấp tài khoản cho nhân viên."
            },
            new
            {
                Id = CreateAppointmentsPermissionId,
                Code = "appointments.create",
                Name = "Đặt lịch ký gửi",
                Description = "Khách hàng tạo lịch hẹn ký gửi của mình trên mini app."
            });

        // Each role receives explicit sample permissions; Customer gets no internal permissions.
        modelBuilder.Entity<RolePermission>().HasData(
            new { RoleId = AdminRoleId, PermissionId = ReadCustomersPermissionId },
            new { RoleId = AdminRoleId, PermissionId = CreateAccountsPermissionId },
            new { RoleId = StaffRoleId, PermissionId = ReadCustomersPermissionId },
            new { RoleId = ManagerRoleId, PermissionId = ReadCustomersPermissionId },
            new { RoleId = ManagerRoleId, PermissionId = CreateAccountsPermissionId },
            new { RoleId = CustomerRoleId, PermissionId = CreateAppointmentsPermissionId });
    }

}

