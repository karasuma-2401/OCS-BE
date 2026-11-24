using Microsoft.EntityFrameworkCore;
using OCS.Api.Authorization;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Models.Products;
using OCS.Api.Services;

namespace OCS.Api.IntegrationTests;

public sealed class EarlyWithdrawalTests
{
    private sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private static (AppDbContext Db, EarlyWithdrawalService Service, Guid Customer, Guid Employee, Product Product) Setup(decimal? fee = 10)
    {
        var now = DateTimeOffset.UtcNow;
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var customer = new Account { AccountType = AccountType.Customer, Status = AccountStatus.Active };
        var employee = new Account { AccountType = AccountType.Internal, Status = AccountStatus.Active };
        var brand = new Brand { Name = "Brand", DefaultCommissionRate = 20 };
        var category = new Category { Name = "Category" };
        var contract = new ConsignmentContract { CustomerId = customer.Id, CreatedByEmployeeId = employee.Id,
            SignedAt = now.AddDays(-1), EndsAt = now.AddDays(30), EarlyWithdrawalFeeRate = fee };
        var product = new Product { Name = "Áo", BrandId = brand.Id, CategoryId = category.Id, StorageLocationId = Guid.NewGuid() };
        product.AssignConsignmentTerms(contract.Id, 123456.75m, brand);
        product.OfferForSale();
        db.AddRange(customer, employee, brand, category, contract, product);
        db.Add(new CustomerProfile { Id = customer.Id, Account = customer });
        db.Add(new EmployeeProfile { Id = employee.Id, Account = employee });
        db.SaveChanges();
        db.ChangeTracker.Clear();
        return (db, new EarlyWithdrawalService(db, new Clock(now)), customer.Id, employee.Id, product);
    }

    [Fact]
    public async Task WorkflowSnapshotsFeeCollectsOnceAndChangesStatusOnlyOnHandover()
    {
        var (db, service, customer, employee, product) = Setup();
        await using var cleanup = db;
        var created = await service.CreateAsync(customer, new([product.Id], "Cần lấy lại"), default);
        Assert.True(created.IsSuccess);
        Assert.Equal(12345.68m, created.Data!.TotalPenaltyAmount);
        var id = created.Data.Id;
        Assert.False((await service.HandoverAsync(id, employee, new("Khách đã nhận"), default)).IsSuccess);
        Assert.False((await service.PayAsync(id, employee, new(12345.68m, "Cash", null), default)).IsSuccess);
        (await db.ConsignmentContracts.SingleAsync()).EarlyWithdrawalFeeRate = 90;
        await db.SaveChangesAsync();
        Assert.True((await service.ApproveAsync(id, employee, default)).IsSuccess);
        var p = await db.Products.SingleAsync();
        Assert.Equal(ProductStatus.OnSale, p.Status);
        Assert.True(p.IsReservedForWithdrawal);
        Assert.False((await new PosService(db).LookupPosProductAsync(p.Id.ToString())).Data!.CanAddToCart);
        Assert.False((await service.PayAsync(id, employee, new(1, "Cash", null), default)).IsSuccess);
        Assert.True((await service.PayAsync(id, employee, new(12345.68m, "BankTransfer", "BANK-001"), default)).IsSuccess);
        Assert.False((await service.PayAsync(id, employee, new(12345.68m, "Cash", null), default)).IsSuccess);
        Assert.False((await service.RejectAsync(id, employee, new("Không nhận"), default)).IsSuccess);
        Assert.True((await service.HandoverAsync(id, employee, new("Khách đã nhận đủ hàng"), default)).IsSuccess);
        Assert.False((await service.HandoverAsync(id, employee, new("Lặp"), default)).IsSuccess);
        Assert.Equal(ProductStatus.EarlyWithdrawn, p.Status);
        Assert.False(p.IsReservedForWithdrawal);
        Assert.Null(p.StorageLocationId);
        Assert.Single(await db.FinancialTransactions.ToListAsync());
        Assert.Single(await db.ProductStatusHistories.ToListAsync());
    }

    [Fact]
    public async Task OwnershipDuplicateAndCancellationAreEnforced()
    {
        var (db, service, customer, _, product) = Setup();
        await using var cleanup = db;
        var other = new Account { AccountType = AccountType.Customer, Status = AccountStatus.Active };
        db.Add(new CustomerProfile { Id = other.Id, Account = other });
        await db.SaveChangesAsync();
        Assert.False((await service.CreateAsync(other.Id, new([product.Id], "Rút"), default)).IsSuccess);
        Assert.False((await service.CreateAsync(customer, new([product.Id, product.Id], "Rút"), default)).IsSuccess);
        var row = (await service.CreateAsync(customer, new([product.Id], "Rút"), default)).Data!;
        Assert.False((await service.CreateAsync(customer, new([product.Id], "Trùng"), default)).IsSuccess);
        Assert.False((await service.DetailAsync(row.Id, other.Id, default)).IsSuccess);
        Assert.False((await service.CancelAsync(row.Id, other.Id, default)).IsSuccess);
        Assert.True((await service.CancelAsync(row.Id, customer, default)).IsSuccess);
        Assert.True((await service.CreateAsync(customer, new([product.Id], "Gửi lại"), default)).IsSuccess);
    }

    [Fact]
    public async Task MissingContractFeeAndSoldOrExpiredProductsAreBlocked()
    {
        var (db, service, customer, employee, product) = Setup(null);
        await using var cleanup = db;
        Assert.False((await service.CreateAsync(customer, new([product.Id], "Rút"), default)).IsSuccess);
        var contract = await db.ConsignmentContracts.SingleAsync();
        contract.EarlyWithdrawalFeeRate = 10;
        await db.SaveChangesAsync();
        var row = (await service.CreateAsync(customer, new([product.Id], "Rút"), default)).Data!;
        (await db.Products.SingleAsync()).Status = ProductStatus.Sold;
        await db.SaveChangesAsync();
        Assert.False((await service.ApproveAsync(row.Id, employee, default)).IsSuccess);
        Assert.False((await service.CreateAsync(customer, new([product.Id], "Rút"), default)).IsSuccess);
    }

    [Fact]
    public async Task FreeWithdrawalNeedsApprovalAndEvidenceWithoutFinancialTransaction()
    {
        var (db, service, customer, employee, product) = Setup(0);
        await using var cleanup = db;
        var row = (await service.CreateAsync(customer, new([product.Id], "Rút"), default)).Data!;
        Assert.True((await service.ApproveAsync(row.Id, employee, default)).IsSuccess);
        Assert.False((await service.HandoverAsync(row.Id, employee, new(" "), default)).IsSuccess);
        Assert.True((await service.HandoverAsync(row.Id, employee, new("Đã giao"), default)).IsSuccess);
        Assert.Empty(await db.FinancialTransactions.ToListAsync());
    }

    [Fact]
    public async Task RejectionReleasesReservationAndAllowsNewRequest()
    {
        var (db, service, customer, employee, product) = Setup();
        await using var cleanup = db;
        var row = (await service.CreateAsync(customer, new([product.Id], "Rút"), default)).Data!;
        Assert.True((await service.ApproveAsync(row.Id, employee, default)).IsSuccess);
        Assert.False((await service.CancelAsync(row.Id, customer, default)).IsSuccess);
        Assert.True((await service.RejectAsync(row.Id, employee, new("Khách đổi ý"), default)).IsSuccess);
        Assert.False((await db.Products.SingleAsync()).IsReservedForWithdrawal);
        Assert.True((await service.CreateAsync(customer, new([product.Id], "Rút lại"), default)).IsSuccess);
        Assert.Contains(PermissionCatalog.WithdrawalApprove, PermissionCatalog.DefaultsFor("Staff"));
    }

    [Fact]
    public async Task ApprovalBlocksActivePaymentButAllowsExpiredUnpaidInvoice()
    {
        var (db, service, customer, employee, product) = Setup();
        await using var cleanup = db;
        var row = (await service.CreateAsync(customer, new([product.Id], "Rút"), default)).Data!;
        var invoice = new SaleInvoice { SoldByEmployeeId = employee, TotalAmount = product.ListedPrice };
        invoice.Lines.Add(new SaleInvoiceLine { ProductId = product.Id, SalePrice = product.ListedPrice });
        db.Add(invoice);
        var payment = new FinancialTransaction { SaleInvoiceId = invoice.Id, Status = FinancialTransactionStatus.Pending,
            Amount = product.ListedPrice, ExpiresAt = DateTimeOffset.UtcNow.AddHours(1), PerformedByEmployeeId = employee };
        db.Add(payment);
        await db.SaveChangesAsync();
        Assert.False((await service.ApproveAsync(row.Id, employee, default)).IsSuccess);
        payment.ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1);
        await db.SaveChangesAsync();
        Assert.True((await service.ApproveAsync(row.Id, employee, default)).IsSuccess);
        Assert.Single(await db.SaleInvoiceLines.ToListAsync());
        Assert.True((await db.Products.SingleAsync()).IsReservedForWithdrawal);
    }

    [Fact]
    public async Task EligibleProductsReturnServerRoundedFeesAndExcludeActiveRequests()
    {
        var (db, service, customer, _, product) = Setup();
        await using var cleanup = db;
        var eligible = await service.EligibleAsync(customer, default);
        Assert.True(eligible.IsSuccess);
        Assert.Equal(product.Id, Assert.Single(eligible.Data!).ProductId);
        Assert.Equal(12345.68m, eligible.Data![0].PenaltyAmount);
        var created = await service.CreateAsync(customer, new([product.Id], "Rút"), default);
        Assert.Empty((await service.EligibleAsync(customer, default)).Data!);
        await service.CancelAsync(created.Data!.Id, customer, default);
        Assert.Single((await service.EligibleAsync(customer, default)).Data!);
        (await db.ConsignmentContracts.SingleAsync()).EarlyWithdrawalFeeRate = null;
        await db.SaveChangesAsync();
        Assert.Empty((await service.EligibleAsync(customer, default)).Data!);
    }

    [Fact]
    public async Task EligibleProductsNeverExposeAnotherCustomersProducts()
    {
        var (db, service, _, _, product) = Setup();
        await using var cleanup = db;
        var other = new Account { AccountType = AccountType.Customer, Status = AccountStatus.Active };
        db.Add(new CustomerProfile { Id = other.Id, Account = other });
        await db.SaveChangesAsync();
        Assert.Empty((await service.EligibleAsync(other.Id, default)).Data!);
        Assert.False((await service.EligibleAsync(Guid.NewGuid(), default)).IsSuccess);
        Assert.NotNull(await db.Products.FindAsync(product.Id));
    }
}
