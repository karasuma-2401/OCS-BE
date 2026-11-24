using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using OCS.Api.Context;
using OCS.Api.Context.Migrations;
using OCS.Api.Consts;
using OCS.Api.Entities;
using OCS.Api.Services;

namespace OCS.Api.IntegrationTests;

// Uses connection-local temporary tables only. No business rows or permanent schema are changed.
public sealed class EarlyWithdrawalPostgresTests
{
    [WithdrawalPostgresFact]
    public async Task RealServiceResubmitsCancelledAndRejectedRequestsAndPreservesHistoryOnPostgres()
    {
        await using var connection = new NpgsqlConnection(Environment.GetEnvironmentVariable("OCS_TEST_POSTGRES"));
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
        // Every table is connection-local, including seed data. Excluding public from search_path
        // ensures no service query can read or write real application data during this test.
        var schema = db.Database.GenerateCreateScript().Replace("CREATE TABLE ", "CREATE TEMP TABLE ", StringComparison.Ordinal);
        await using (var command = new NpgsqlCommand("SET search_path TO pg_temp;\n" + schema, connection))
            await command.ExecuteNonQueryAsync();
        var customer = new Account { AccountType = AccountType.Customer, Status = AccountStatus.Active, Email = "customer@example.test", PhoneNumber = "0900000001" };
        var staff = new Account { AccountType = AccountType.Internal, Status = AccountStatus.Active, Email = "staff@example.test", PhoneNumber = "0900000002",
            Username = "withdrawal-test", PasswordHash = "test-only-hash" };
        var brand = new Brand { Name = "Test", DefaultCommissionRate = 15 };
        var category = new Category { Name = "Test" };
        var contract = new ConsignmentContract { ContractNumber = "PG-TEST", CustomerId = customer.Id, CreatedByEmployeeId = staff.Id,
            SignedAt = DateTimeOffset.UtcNow.AddDays(-1), EndsAt = DateTimeOffset.UtcNow.AddMonths(1) };
        var product = new Product { Name = "Áo", Barcode = "PG-TEST-1", Brand = brand, Category = category };
        product.AssignConsignmentTerms(contract.Id, 20000, brand); product.OfferForSale();
        db.AddRange(new CustomerProfile { Id = customer.Id, Account = customer }, new EmployeeProfile { Id = staff.Id, Account = staff }, contract, product);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var service = new EarlyWithdrawalService(db, TimeProvider.System);
        var first = await service.CreateAsync(customer.Id, new([product.Id], "Lần 1"), default);
        Assert.True(first.IsSuccess); Assert.Equal(3000m, first.Data!.TotalPenaltyAmount);
        Assert.True((await service.CancelAsync(first.Data.Id, customer.Id, default)).IsSuccess);
        db.ChangeTracker.Clear();
        var second = await service.CreateAsync(customer.Id, new([product.Id], "Lần 2"), default);
        Assert.True(second.IsSuccess);
        Assert.False((await service.CreateAsync(customer.Id, new([product.Id], "Trùng"), default)).IsSuccess);
        Assert.True((await service.ApproveAsync(second.Data!.Id, staff.Id, default)).IsSuccess);
        Assert.True((await service.RejectAsync(second.Data.Id, staff.Id, new("Đổi lịch"), default)).IsSuccess);
        db.ChangeTracker.Clear();
        var third = await service.CreateAsync(customer.Id, new([product.Id], "Lần 3"), default);
        Assert.True(third.IsSuccess);
        var tracking = new CustomerConsignmentService(db, new OCS.Api.Repositories.CustomerProfileRepository(db));
        var detail = (await tracking.GetProductAsync(customer.Id, contract.Id, product.Id, default))!;
        Assert.Equal(third.Data!.Id, detail.Withdrawal!.Id); Assert.False(detail.CanWithdrawEarly);
        Assert.True((await service.ApproveAsync(third.Data.Id, staff.Id, default)).IsSuccess);
        Assert.True((await service.PayAsync(third.Data.Id, staff.Id, new(3000, "Cash", null), default)).IsSuccess);
        Assert.False((await service.RejectAsync(third.Data.Id, staff.Id, new("Đã thu tiền"), default)).IsSuccess);
        Assert.True((await service.HandoverAsync(third.Data.Id, staff.Id, new("Đã nhận đủ"), default)).IsSuccess);
        Assert.False((await service.CreateAsync(customer.Id, new([product.Id], "Đã bàn giao"), default)).IsSuccess);
        Assert.Equal(3, await db.EarlyWithdrawalItems.CountAsync());
        Assert.Equal("Cancelled", (await service.DetailAsync(first.Data.Id, customer.Id, default)).Data!.Status);
        Assert.Equal("Rejected", (await service.DetailAsync(second.Data.Id, customer.Id, default)).Data!.Status);
        Assert.Equal(1, await db.FinancialTransactions.CountAsync());
        Assert.Equal(ProductStatus.EarlyWithdrawn, (await db.Products.SingleAsync()).Status);
    }

    [WithdrawalPostgresFact]
    public async Task MigrationAllowsRepeatedProductsAcrossRequestsAndRetainsLineUniqueness()
    {
        await using var connection = new NpgsqlConnection(Environment.GetEnvironmentVariable("OCS_TEST_POSTGRES"));
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
        async Task Execute(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection, tx);
            await command.ExecuteNonQueryAsync();
        }
        await Execute("""
            SET LOCAL search_path TO pg_temp;
            CREATE TEMP TABLE "EarlyWithdrawalItems" (
                "EarlyWithdrawalId" uuid NOT NULL, "ProductId" uuid NOT NULL,
                PRIMARY KEY ("EarlyWithdrawalId", "ProductId"));
            CREATE UNIQUE INDEX "IX_EarlyWithdrawalItems_ProductId" ON "EarlyWithdrawalItems" ("ProductId");
            INSERT INTO "EarlyWithdrawalItems" VALUES
                ('00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000099');
            SAVEPOINT old_index;
            """);
        var sql = """
            INSERT INTO "EarlyWithdrawalItems" VALUES
                ('00000000-0000-0000-0000-000000000002', '00000000-0000-0000-0000-000000000099');
            """;
        var oldError = await Assert.ThrowsAsync<PostgresException>(() => Execute(sql));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, oldError.SqlState);
        await Execute("ROLLBACK TO SAVEPOINT old_index;");
        var migration = new AllowEarlyWithdrawalResubmission();
        foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations))
            await Execute(command.CommandText);
        await Execute(sql);
        await Execute("SAVEPOINT duplicate_line;");
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => Execute(sql));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
        await Execute("ROLLBACK TO SAVEPOINT duplicate_line;");
        await using var count = new NpgsqlCommand("SELECT COUNT(*) FROM \"EarlyWithdrawalItems\"", connection, tx);
        Assert.Equal(2L, await count.ExecuteScalarAsync());
        await tx.RollbackAsync();
    }
}

public sealed class WithdrawalPostgresFactAttribute : FactAttribute
{
    public WithdrawalPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OCS_TEST_POSTGRES")))
            Skip = "Set OCS_TEST_POSTGRES to run the PostgreSQL temporary-table regression.";
    }
}
