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
        (Guid Id, string Name, string Phone, string? Username, Guid Role)[] accounts =
        [
            (Guid.Parse("10000000-0000-0000-0000-000000000001"), "Admin Dev", "0900000001", "admin_dev", Role.AdminRoleId),
            (Guid.Parse("10000000-0000-0000-0000-000000000002"), "Manager Dev", "0900000002", "manager_dev", Role.ManagerRoleId),
            (Guid.Parse("10000000-0000-0000-0000-000000000003"), "Staff Dev", "0900000003", "staff_dev", Role.StaffRoleId)
        ];

        foreach (var (id, name, phone, username, roleId) in accounts)
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

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
