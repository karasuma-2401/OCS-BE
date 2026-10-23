using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using OCS.Application.Abstractions.Auditing;
using OCS.Domain.Entities;
using OCS.Domain.Enums;

namespace OCS.Infrastructure.Persistence.Seeds;

public sealed class DevelopmentDataSeeder(AppDbContext dbContext, IHostEnvironment environment, SystemAuditContext auditContext)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException("Demo seeding is only allowed in Development or Testing.");
        }

        auditContext.Initialize("DemoSeed");
        // Schema must already be migrated. This command never migrates a database implicitly.
        (Guid Id, string Name, string Phone, string? Username, Guid Role)[] accounts =
        {
            (Guid.Parse("10000000-0000-0000-0000-000000000001"), "Quản lý demo", "0900000001", "manager.demo", Guid.Parse("20000000-0000-0000-0000-000000000001")),
            (Guid.Parse("10000000-0000-0000-0000-000000000002"), "Nhân viên demo", "0900000002", "staff.demo", Guid.Parse("20000000-0000-0000-0000-000000000002")),
            (Guid.Parse("10000000-0000-0000-0000-000000000003"), "Khách ký gửi demo", "0900000003", null, Guid.Parse("20000000-0000-0000-0000-000000000004"))
        };

        foreach (var sample in accounts)
        {
            // Do not overwrite accounts that a teammate has edited or activated.
            if (await dbContext.Accounts.AnyAsync(account => account.Id == sample.Id, cancellationToken))
            {
                continue;
            }

            var account = new Account
            {
                AccountType = sample.Username is null ? AccountType.Customer : AccountType.Internal,
                FullName = sample.Name,
                PhoneNumber = sample.Phone,
                Username = sample.Username,
                Status = AccountStatus.PendingActivation
            };
            dbContext.Accounts.Add(account);
            dbContext.Entry(account).Property(entity => entity.Id).CurrentValue = sample.Id;
            if (sample.Username is null)
            {
                dbContext.CustomerProfiles.Add(new CustomerProfile { Id = sample.Id, Account = account });
            }
            else
            {
                dbContext.EmployeeProfiles.Add(new EmployeeProfile
                {
                    Id = sample.Id,
                    Account = account,
                    JobTitle = sample.Name,
                    HiredOn = DateOnly.FromDateTime(DateTime.UtcNow)
                });
            }

            dbContext.AccountRoles.Add(new AccountRole { AccountId = sample.Id, RoleId = sample.Role });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
