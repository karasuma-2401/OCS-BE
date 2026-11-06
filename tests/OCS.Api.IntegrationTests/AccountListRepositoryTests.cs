using Microsoft.EntityFrameworkCore;
using OCS.Api.Common;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Repositories;

namespace OCS.Api.IntegrationTests;

public sealed class AccountListRepositoryTests
{
    [Fact]
    public async Task ListingReturnsSummaryFieldsRolesAndStablePages()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var context = new AppDbContext(options);
        await SeedAsync(context);
        var repository = new AuthRepository(context);

        var first = await repository.GetPagedAccountsAsync(new PagedRequest { PageSize = 1 },
            null, AccountType.Internal, null, false, null);
        var second = await repository.GetPagedAccountsAsync(new PagedRequest { Page = 2, PageSize = 1 },
            null, AccountType.Internal, null, false, null);

        Assert.Equal(2, first.TotalCount);
        Assert.Equal("Newest", Assert.Single(first.Items).FullName);
        Assert.Equal("Staff", Assert.Single(first.Items.Single().AccountRoles).Role.Code);
        Assert.Equal("Older", Assert.Single(second.Items).FullName);
        var requester = await repository.GetListRequesterAsync(first.Items.Single().Id);
        Assert.NotNull(requester);
        Assert.Equal(AccountStatus.Active, requester.Status);
        Assert.NotNull(requester.EmailVerifiedAt);
        Assert.Equal(Role.StaffRoleId, Assert.Single(requester.AccountRoles).RoleId);
    }

    [PostgresFact]
    public async Task ListingPreservesSearchRoleAndManagerFiltersInPostgres()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Context();
        await SeedAsync(context, rolesExist: true);
        var repository = new AuthRepository(context);

        var page = await repository.GetPagedAccountsAsync(new PagedRequest(), "ewEs",
            AccountType.Internal, AccountStatus.Active, true, Role.StaffRoleId);

        Assert.Equal(1, page.TotalCount);
        var account = Assert.Single(page.Items);
        Assert.Equal("Newest", account.FullName);
        Assert.Equal("Staff", Assert.Single(account.AccountRoles).Role.Code);
        Assert.NotNull(await repository.GetListRequesterAsync(account.Id));
    }

    private static async Task SeedAsync(AppDbContext context, bool rolesExist = false)
    {
        if (!rolesExist)
            context.Roles.Add(new Role { Id = Role.StaffRoleId, Code = "Staff", Name = "Staff" });

        context.Accounts.AddRange(
            CreateAccount("Newest", "newest", "0900000001", DateTimeOffset.UtcNow),
            CreateAccount("Older", "older", "0900000002", DateTimeOffset.UtcNow.AddDays(-1)));
        await context.SaveChangesAsync();
    }

    private static Account CreateAccount(string name, string username, string phone, DateTimeOffset createdAt) => new()
    {
        FullName = name,
        Username = username,
        PhoneNumber = phone,
        AccountType = AccountType.Internal,
        Status = AccountStatus.Active,
        EmailVerifiedAt = DateTimeOffset.UtcNow,
        PasswordHash = "test-hash",
        CreatedAt = createdAt,
        AccountRoles = [new AccountRole { RoleId = Role.StaffRoleId }]
    };
}
