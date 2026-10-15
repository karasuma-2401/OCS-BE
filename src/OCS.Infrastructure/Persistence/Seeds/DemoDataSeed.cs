using Microsoft.EntityFrameworkCore;
using OCS.Domain.Entities;
using OCS.Domain.Enums;

namespace OCS.Infrastructure.Persistence.Seeds;

/// <summary>A small, deterministic demo dataset managed by migrations.</summary>
internal static class DemoDataSeed
{
    private static readonly Guid ManagerAccountId = new("10000000-0000-0000-0000-000000000001");
    private static readonly Guid StaffAccountId = new("10000000-0000-0000-0000-000000000002");
    private static readonly Guid CustomerAccountId = new("10000000-0000-0000-0000-000000000003");
    private static readonly Guid ManagerRoleId = new("20000000-0000-0000-0000-000000000001");
    private static readonly Guid StaffRoleId = new("20000000-0000-0000-0000-000000000002");
    private static readonly Guid AdminRoleId = new("20000000-0000-0000-0000-000000000003");
    private static readonly Guid CustomerRoleId = new("20000000-0000-0000-0000-000000000004");
    private static readonly Guid ReadCustomersPermissionId = new("30000000-0000-0000-0000-000000000001");
    private static readonly Guid CreateAccountsPermissionId = new("30000000-0000-0000-0000-000000000002");
    private static readonly Guid CreateAppointmentsPermissionId = new("30000000-0000-0000-0000-000000000003");
    private static readonly DateTimeOffset SeededAt = new(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);

    public static void Configure(ModelBuilder modelBuilder)
    {
        SeedAccounts(modelBuilder);
        SeedProfiles(modelBuilder);
        SeedRolesAndPermissions(modelBuilder);
        SeedAuditExample(modelBuilder);
    }

    private static void SeedAccounts(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>().HasData(
            new
            {
                Id = ManagerAccountId,
                AccountType = AccountType.Internal,
                FullName = "Quản lý demo",
                PhoneNumber = "0900000001",
                Username = "manager.demo",
                Email = "manager.demo@example.com",
                Status = AccountStatus.PendingActivation,
                CreatedAt = SeededAt,
                CreatedByAccountId = (Guid?)null
            },
            new
            {
                Id = StaffAccountId,
                AccountType = AccountType.Internal,
                FullName = "Nhân viên demo",
                PhoneNumber = "0900000002",
                Username = "staff.demo",
                Email = "staff.demo@example.com",
                Status = AccountStatus.PendingActivation,
                CreatedAt = SeededAt,
                CreatedByAccountId = (Guid?)ManagerAccountId
            },
            new
            {
                Id = CustomerAccountId,
                AccountType = AccountType.Customer,
                FullName = "Khách ký gửi demo",
                PhoneNumber = "0900000003",
                Username = (string?)null,
                Email = "customer.demo@example.com",
                Status = AccountStatus.PendingActivation,
                CreatedAt = SeededAt,
                CreatedByAccountId = (Guid?)null
            });
    }

    private static void SeedProfiles(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EmployeeProfile>().HasData(
            new
            {
                Id = ManagerAccountId,
                JobTitle = "Quản lý cửa hàng",
                HiredOn = new DateOnly(2026, 10, 1),
                Status = EmployeeStatus.Working
            },
            new
            {
                Id = StaffAccountId,
                JobTitle = "Nhân viên POS",
                HiredOn = new DateOnly(2026, 10, 10),
                Status = EmployeeStatus.Working
            });

        modelBuilder.Entity<CustomerProfile>().HasData(new
        {
            Id = CustomerAccountId,
            Address = "Địa chỉ mẫu tại TP. Hồ Chí Minh",
            BankCode = "VCB",
            BankAccountNumber = "000000000001",
            BankAccountHolderName = "KHACH KY GUI DEMO",
            Status = CustomerStatus.Active
        });
    }

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

        modelBuilder.Entity<AccountRole>().HasData(
            new { AccountId = ManagerAccountId, RoleId = ManagerRoleId },
            new { AccountId = StaffAccountId, RoleId = StaffRoleId },
            new { AccountId = CustomerAccountId, RoleId = CustomerRoleId });

        // Each role receives explicit sample permissions; Customer gets no internal permissions.
        modelBuilder.Entity<RolePermission>().HasData(
            new { RoleId = AdminRoleId, PermissionId = ReadCustomersPermissionId },
            new { RoleId = AdminRoleId, PermissionId = CreateAccountsPermissionId },
            new { RoleId = StaffRoleId, PermissionId = ReadCustomersPermissionId },
            new { RoleId = ManagerRoleId, PermissionId = ReadCustomersPermissionId },
            new { RoleId = ManagerRoleId, PermissionId = CreateAccountsPermissionId },
            new { RoleId = CustomerRoleId, PermissionId = CreateAppointmentsPermissionId });
    }

    private static void SeedAuditExample(ModelBuilder modelBuilder)
    {
        // A clearly labelled example for displaying audit history, not a real user event.
        // HasData is applied by migrations and does not run SaveChanges interceptors.
        modelBuilder.Entity<AuditLog>().HasData(new
        {
            Id = new Guid("40000000-0000-0000-0000-000000000001"),
            ActorType = AuditActorType.System,
            Action = "DemoDataSeeded",
            EntityType = nameof(Account),
            EntityId = ManagerAccountId.ToString(),
            Changes = """{"Dataset":{"old":null,"new":"AccountsDemoV1"}}""",
            Reason = "Bản ghi minh họa thuộc bộ dữ liệu demo; không phải sự kiện người dùng thực tế.",
            Source = "DemoSeed",
            OccurredAt = SeededAt,
            RecordedAt = SeededAt
        });
    }
}
