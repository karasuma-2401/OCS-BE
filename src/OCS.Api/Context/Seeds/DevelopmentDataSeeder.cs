using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using OCS.Api.Interfaces.Services;
using OCS.Api.Services;
using OCS.Api.Entities;
using OCS.Api.Consts;

namespace OCS.Api.Context.Seeds;

public sealed class DevelopmentDataSeeder(AppDbContext dbContext, IHostEnvironment environment, SystemAuditContext auditContext)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException("Demo seeding is only allowed in Development or Testing.");
        }

        auditContext.Initialize("DemoSeed");

        // 1. Internal Accounts (Staff, Manager, Admin)
        (Guid Id, string Name, string Phone, string? Username, Guid Role)[] staffAccounts =
        [
            (Guid.Parse("10000000-0000-0000-0000-000000000001"), "Admin Dev", "0900000001", "admin_dev", Role.AdminRoleId),
            (Guid.Parse("10000000-0000-0000-0000-000000000002"), "Manager Dev", "0900000002", "manager_dev", Role.ManagerRoleId),
            (Guid.Parse("10000000-0000-0000-0000-000000000003"), "Staff Dev", "0900000003", "staff_dev", Role.StaffRoleId)
        ];

        foreach (var (id, name, phone, username, roleId) in staffAccounts)
        {
            if (await dbContext.Accounts.AnyAsync(account => account.Id == id, cancellationToken))
            {
                continue;
            }

            var account = new Account
            {
                Id = id,
                PhoneNumber = phone,
                Username = username,
                FullName = name,
                AccountType = AccountType.Internal,
                Status = AccountStatus.Active
            };

            var profile = new EmployeeProfile
            {
                Id = id,
                Account = account
            };

            var accountRole = new AccountRole
            {
                AccountId = id,
                RoleId = roleId
            };

            dbContext.Accounts.Add(account);
            dbContext.EmployeeProfiles.Add(profile);
            dbContext.AccountRoles.Add(accountRole);
        }

        // 2. Customer Accounts (Khách hàng mẫu theo Figma)
        (Guid Id, string Name, string Phone)[] customerAccounts =
        [
            (Guid.Parse("10000000-0000-0000-0000-000000000004"), "Khách Hàng Ký Gửi", "0901234567"),
            (Guid.Parse("10000000-0000-0000-0000-000000000010"), "Nguyễn Thu Trang", "0908123456"),
            (Guid.Parse("10000000-0000-0000-0000-000000000011"), "Trần Mai Chi", "0912345678"),
            (Guid.Parse("10000000-0000-0000-0000-000000000012"), "Hoàng Yến", "0987654321")
        ];

        foreach (var (id, name, phone) in customerAccounts)
        {
            if (await dbContext.Accounts.AnyAsync(account => account.Id == id, cancellationToken))
            {
                continue;
            }

            var account = new Account
            {
                Id = id,
                PhoneNumber = phone,
                Username = null,
                FullName = name,
                AccountType = AccountType.Customer,
                Status = AccountStatus.Active
            };

            var profile = new CustomerProfile
            {
                Id = id,
                Account = account,
                Status = CustomerStatus.Active
            };

            var accountRole = new AccountRole
            {
                AccountId = id,
                RoleId = Role.CustomerRoleId
            };

            dbContext.Accounts.Add(account);
            dbContext.CustomerProfiles.Add(profile);
            dbContext.AccountRoles.Add(accountRole);
        }

        // 3. Appointment Time Slots
        (Guid Id, TimeOnly Start, TimeOnly End, int Max)[] timeSlots =
        [
            (Guid.Parse("11111111-0000-0000-0000-000000000001"), new TimeOnly(9, 0), new TimeOnly(10, 0), 3),
            (Guid.Parse("11111111-0000-0000-0000-000000000002"), new TimeOnly(10, 0), new TimeOnly(11, 0), 3),
            (Guid.Parse("11111111-0000-0000-0000-000000000003"), new TimeOnly(11, 0), new TimeOnly(12, 0), 3)
        ];

        var allowedIds = timeSlots.Select(t => t.Id).ToHashSet();
        var extraSlots = await dbContext.AppointmentTimeSlots
            .Where(s => !allowedIds.Contains(s.Id) && s.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var extra in extraSlots)
        {
            extra.IsActive = false;
        }

        foreach (var (id, start, end, max) in timeSlots)
        {
            var existing = await dbContext.AppointmentTimeSlots.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
            if (existing != null)
            {
                existing.StartTime = start;
                existing.EndTime = end;
                existing.MaxCustomers = max;
                existing.IsActive = true;
                continue;
            }

            dbContext.AppointmentTimeSlots.Add(new AppointmentTimeSlot
            {
                Id = id,
                StartTime = start,
                EndTime = end,
                MaxCustomers = max,
                IsActive = true
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // 4. Demo Appointments matching Figma
        (Guid Id, string Tracking, Guid CustomerId, Guid SlotId, DateOnly Date, int Items, AppointmentStatus Status, string Note)[] demoAppointments =
        [
            (
                Guid.Parse("20000000-0000-0000-0000-000000000001"),
                "LH-012",
                Guid.Parse("10000000-0000-0000-0000-000000000010"),
                Guid.Parse("11111111-0000-0000-0000-000000000001"),
                new DateOnly(2026, 10, 28),
                6,
                AppointmentStatus.Completed,
                "Áo khoác dạ và chân váy len"
            ),
            (
                Guid.Parse("20000000-0000-0000-0000-000000000002"),
                "LH-261028-018",
                Guid.Parse("10000000-0000-0000-0000-000000000011"),
                Guid.Parse("11111111-0000-0000-0000-000000000002"),
                new DateOnly(2026, 10, 28),
                8,
                AppointmentStatus.CheckedIn,
                "Quần áo và một túi xách"
            ),
            (
                Guid.Parse("20000000-0000-0000-0000-000000000003"),
                "LH-020",
                Guid.Parse("10000000-0000-0000-0000-000000000012"),
                Guid.Parse("11111111-0000-0000-0000-000000000003"),
                new DateOnly(2026, 10, 28),
                4,
                AppointmentStatus.Scheduled,
                "Váy công sở và áo sơ mi"
            )
        ];

        foreach (var (id, tracking, customerId, slotId, date, items, status, note) in demoAppointments)
        {
            if (await dbContext.Appointments.AnyAsync(a => a.Id == id, cancellationToken))
            {
                continue;
            }

            dbContext.Appointments.Add(new Appointment
            {
                Id = id,
                TrackingNumber = tracking,
                CustomerId = customerId,
                TimeSlotId = slotId,
                AppointmentDate = date,
                EstimatedItemCount = items,
                SubmissionMethod = SubmissionMethod.InPerson,
                Status = status,
                Note = note
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
