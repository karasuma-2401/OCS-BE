using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OCS.Application.Abstractions.Auditing;
using OCS.Domain.Entities;
using OCS.Domain.Enums;
using OCS.Infrastructure.Persistence;
using OCS.Infrastructure.Persistence.Interceptors;
using OCS.Infrastructure.Persistence.Seeds;
using Microsoft.Extensions.Configuration;
using OCS.Application.Features.Catalog;
using OCS.Application.Abstractions.Persistence;

namespace OCS.Infrastructure.IntegrationTests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("OCS_RUN_POSTGRES_TESTS") != "1")
        {
            Skip = "Set OCS_RUN_POSTGRES_TESTS=1 with Docker running to test PostgreSQL migrations and audit.";
        }
    }
}

public sealed class PersistenceTests
{
    [Fact]
    public void MigrationSnapshotMatchesTheCurrentModel()
    {
        using var db = new AppDbContextFactory().CreateDbContext([]);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [PostgresFact]
    public async Task FreshMigrationHasReferenceDataWithoutDemoAccountsAndSeederIsIdempotent()
    {
        await using var postgres = new TestPostgres();
        await postgres.StartAsync();
        var audit = new SystemAuditContext();
        audit.Initialize("IntegrationTest");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .AddInterceptors(new TimestampSaveChangesInterceptor(), new AuditSaveChangesInterceptor(audit)).Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();
        Assert.Equal(4, await db.Roles.CountAsync());
        Assert.Empty(await db.Accounts.ToListAsync());
        Assert.Empty(await db.AuditLogs.ToListAsync());

        var environment = new TestEnvironment { EnvironmentName = "Testing" };
        var seeder = new DevelopmentDataSeeder(db, environment, audit);
        await seeder.SeedAsync();
        await seeder.SeedAsync();
        Assert.Equal(3, await db.Accounts.CountAsync());
        Assert.Equal(3, await db.AccountRoles.CountAsync());
        Assert.All(await db.AuditLogs.ToListAsync(), entry => Assert.Equal("DemoSeed", entry.Source));
        environment.EnvironmentName = Environments.Production;
        await Assert.ThrowsAsync<InvalidOperationException>(() => seeder.SeedAsync());

        var brand = new Brand { Name = "Integration brand", DefaultCommissionRate = 20m };
        db.Brands.Add(brand);
        await db.SaveChangesAsync();
        var log = await db.AuditLogs.SingleAsync(entry => entry.EntityId == brand.Id.ToString());
        db.Entry(log).Property(entry => entry.Reason).CurrentValue = "Attempt to change immutable history";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [PostgresFact]
    public async Task RepositoryAndUnitOfWorkShareTheContextAndEfMaterializesPrivateSetters()
    {
        await using var postgres = new TestPostgres();
        await postgres.StartAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = postgres.GetConnectionString()
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<SystemAuditContext>();
        audit.Initialize("IntegrationTest");
        await context.Database.MigrateAsync();
        await new DevelopmentDataSeeder(context, new TestEnvironment(), audit).SeedAsync();
        audit.Initialize("IntegrationTest");
        var brand = new Brand { Name = "Repository brand" };
        var category = new Category { Name = "Repository category" };
        var buyout = new Buyout
        {
            BuyoutNumber = "TEST-001",
            CustomerId = Guid.Parse("10000000-0000-0000-0000-000000000003"),
            CreatedByEmployeeId = Guid.Parse("10000000-0000-0000-0000-000000000001")
        };
        context.AddRange(brand, category, buyout);
        var product = new Product { Barcode = "TEST-001", Name = "Test product", Size = "M", BrandId = brand.Id, CategoryId = category.Id };
        product.AssignBuyoutTerms(buyout.Id, 100m, 50m);
        product.OfferForSale();
        var repository = scope.ServiceProvider.GetRequiredService<IProductRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        repository.Add(product);
        Assert.Equal(EntityState.Added, context.Entry(product).State);
        await unitOfWork.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var loaded = await repository.GetByBarcodeAsync(product.Barcode);
        Assert.NotNull(loaded);
        Assert.Equal(100m, loaded.ListedPrice);
        Assert.Equal(ProductStatus.OnSale, loaded.Status);
        loaded.MarkSold();
        await unitOfWork.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Equal(ProductStatus.Sold, (await repository.GetByIdAsync(product.Id))!.Status);

        var invalidBrand = new Brand { Name = "Invalid rate", DefaultCommissionRate = 101m };
        context.Brands.Add(invalidBrand);
        await Assert.ThrowsAsync<DbUpdateException>(() => unitOfWork.SaveChangesAsync());
        context.ChangeTracker.Clear();
        Assert.False(await context.Brands.AnyAsync(item => item.Id == invalidBrand.Id));
        Assert.False(await context.AuditLogs.AnyAsync(item => item.EntityId == invalidBrand.Id.ToString()));
    }

    [PostgresFact]
    public async Task CleanupPreservesEditedLegacyAccounts()
    {
        await using var postgres = new TestPostgres();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.GetConnectionString()).Options;
        await using var db = new AppDbContext(options);
        await db.GetService<IMigrator>().MigrateAsync("20261021031023_AddFinancialAndExceptionTables");
        var manager = await db.Accounts.SingleAsync(account => account.Username == "manager.demo");
        manager.FullName = "Account edited by the team";
        await db.SaveChangesAsync();
        await db.Database.MigrateAsync();
        Assert.Equal(3, await db.Accounts.CountAsync());
        Assert.Equal("Account edited by the team", (await db.Accounts.SingleAsync(account => account.Id == manager.Id)).FullName);
    }

    [PostgresFact]
    public async Task CleanupPreservesLegacyAccountsReferencedByBusinessData()
    {
        await using var postgres = new TestPostgres();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.GetConnectionString()).Options;
        await using var db = new AppDbContext(options);
        await db.GetService<IMigrator>().MigrateAsync("20261021031023_AddFinancialAndExceptionTables");
        db.Buyouts.Add(new Buyout
        {
            BuyoutNumber = "LEGACY-001",
            CustomerId = Guid.Parse("10000000-0000-0000-0000-000000000003"),
            CreatedByEmployeeId = Guid.Parse("10000000-0000-0000-0000-000000000001")
        });
        await db.SaveChangesAsync();
        await db.Database.MigrateAsync();
        Assert.Equal(3, await db.Accounts.CountAsync());
        Assert.Equal(1, await db.Buyouts.CountAsync());
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "PersistenceTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
