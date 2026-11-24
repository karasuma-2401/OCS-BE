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
    [Fact]
    public async Task SearchAndSummaryRespectCustomerScopeAndFilterBeforePagination()
    {
        var (db, service, customer, employee, product) = Setup();
        await using var cleanup = db;
        var first = (await service.CreateAsync(customer, new([product.Id], "Rút lần một"), default)).Data!;
        await service.RejectAsync(first.Id, employee, new("Khách đổi ý"), default);
        var second = (await service.CreateAsync(customer, new([product.Id], "Rút lần hai"), default)).Data!;
        var other = new Account { AccountType = AccountType.Customer, Status = AccountStatus.Active };
        db.Add(new CustomerProfile { Id = other.Id, Account = other });
        db.Add(new EarlyWithdrawal { CustomerId = other.Id, Status = "Completed", Reason = "Của khách khác", RequestedAt = DateTimeOffset.UtcNow });
        (await db.EarlyWithdrawals.SingleAsync(x => x.Id == first.Id)).RequestedAt = DateTimeOffset.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();
        var summary = (await service.SummaryAsync(customer, default)).Data!;
        Assert.Equal(2, summary.TotalCount);
        Assert.Equal(1, summary.PendingCount);
        Assert.Equal(1, summary.RejectedCount);
        Assert.Equal(0, summary.CompletedCount);
        Assert.Equal(3, (await service.SummaryAsync(null, default)).Data!.TotalCount);
        var search = await service.SearchAsync(customer, new WithdrawalQuery(PageSize: 1, Q: "ÁO", Sort: "oldest"), default);
        Assert.True(search.IsSuccess);
        Assert.Equal(2, search.Data!.TotalCount);
        Assert.Equal(first.Id, Assert.Single(search.Data.Items).Id);
        var pending = await service.SearchAsync(customer, new WithdrawalQuery(Status: "Pending", PaymentStatus: "Unpaid"), default);
        Assert.Equal(second.Id, Assert.Single(pending.Data!.Items).Id);
        Assert.Empty((await service.SearchAsync(customer, new WithdrawalQuery(Status: "Completed"), default)).Data!.Items);
        Assert.Equal(first.Id, Assert.Single((await service.SearchAsync(customer, new WithdrawalQuery(Q: first.Id.ToString()[..8]), default)).Data!.Items).Id);
    }

    [Fact]
    public async Task SearchRejectsInvalidFiltersAndInactiveCustomers()
    {
        var (db, service, customer, _, _) = Setup();
        await using var cleanup = db;
        Assert.False((await service.SearchAsync(customer, new WithdrawalQuery(Page: 0), default)).IsSuccess);
        Assert.False((await service.SearchAsync(customer, new WithdrawalQuery(Status: "Unknown"), default)).IsSuccess);
        Assert.False((await service.SearchAsync(customer, new WithdrawalQuery(PaymentStatus: "Unknown"), default)).IsSuccess);
        Assert.False((await service.SearchAsync(customer, new WithdrawalQuery(Q: new string('a', 201)), default)).IsSuccess);
        Assert.False((await service.SearchAsync(Guid.NewGuid(), new WithdrawalQuery(), default)).IsSuccess);
        Assert.False((await service.SummaryAsync(Guid.NewGuid(), default)).IsSuccess);
    }

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
            SignedAt = now.AddDays(-1), EndsAt = now.AddDays(30), EarlyWithdrawalFeeRate = 77 };
        var product = new Product { Name = "Áo", BrandId = brand.Id, CategoryId = category.Id, StorageLocationId = Guid.NewGuid() };
        product.AssignConsignmentTerms(contract.Id, 123456.75m, brand, fee ?? 10);
        product.OfferForSale();
        db.AddRange(customer, employee, brand, category, contract, product);
        db.Add(new CustomerProfile { Id = customer.Id, Account = customer });
        db.Add(new EmployeeProfile { Id = employee.Id, Account = employee });
        db.SaveChanges();
        if (fee is null)
        {
            db.Entry(product).Property(p => p.AgreedCommissionRate).CurrentValue = null;
            db.SaveChanges();
        }
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
    public async Task MissingProductCommissionAndSoldProductsAreBlocked()
    {
        var (db, service, customer, employee, product) = Setup(null);
        await using var cleanup = db;
        Assert.False((await service.CreateAsync(customer, new([product.Id], "Rút"), default)).IsSuccess);
        db.Entry(await db.Products.SingleAsync()).Property(p => p.AgreedCommissionRate).CurrentValue = 10;
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
        Assert.Single((await service.EligibleAsync(customer, default)).Data!);
        db.Entry(await db.Products.SingleAsync()).Property(p => p.AgreedCommissionRate).CurrentValue = null;
        await db.SaveChangesAsync();
        Assert.Empty((await service.EligibleAsync(customer, default)).Data!);
    }

    [Fact]
    public async Task FeesUseEachProductsAgreedRateEvenWithinTheSameContract()
    {
        var (db, service, customer, _, first) = Setup(10);
        await using var cleanup = db;
        var contract = await db.ConsignmentContracts.SingleAsync();
        contract.EarlyWithdrawalFeeRate = null;
        var brand = await db.Brands.SingleAsync();
        var second = new Product { Name = "Quần", BrandId = brand.Id, CategoryId = first.CategoryId };
        second.AssignConsignmentTerms(contract.Id, 100000m, brand, 25);
        second.OfferForSale(); db.Products.Add(second);
        brand.DefaultCommissionRate = 80;
        await db.SaveChangesAsync();
        var eligible = (await service.EligibleAsync(customer, default)).Data!;
        Assert.Equal(10m, eligible.Single(p => p.ProductId == first.Id).PenaltyRate);
        Assert.Equal(25000m, eligible.Single(p => p.ProductId == second.Id).PenaltyAmount);
        var created = (await service.CreateAsync(customer, new([first.Id, second.Id], "Lấy hai món"), default)).Data!;
        Assert.Equal(37345.68m, created.TotalPenaltyAmount);
        Assert.Collection(created.Items.OrderBy(i => i.PenaltyRate), i => Assert.Equal(10m, i.PenaltyRate), i => Assert.Equal(25m, i.PenaltyRate));
    }

    [Fact]
    public async Task CustomerTrackingShowsWithdrawalProgressEligibilityAndFinalProductStatus()
    {
        var (db, service, customer, employee, product) = Setup();
        await using var cleanup = db;
        var tracking = new CustomerConsignmentService(db, new OCS.Api.Repositories.CustomerProfileRepository(db));
        async Task<OCS.Api.Models.Customers.ConsignmentProductResponse> Read()
        {
            var result = (await tracking.SearchAsync(customer, new(), default))!;
            return Assert.Single(Assert.Single(result.Items).Products);
        }
        var initial = await Read();
        Assert.True(initial.CanWithdrawEarly); Assert.Equal(12345.68m, initial.EarlyWithdrawalFee);
        var row = (await service.CreateAsync(customer, new([product.Id], "Lấy lại"), default)).Data!;
        var pending = await Read();
        Assert.Equal("Pending", pending.Withdrawal!.Status); Assert.False(pending.CanWithdrawEarly);
        var detail = await tracking.GetProductAsync(customer, product.ConsignmentContractId!.Value, product.Id, default);
        Assert.Equal(row.Id, detail!.Withdrawal!.Id); Assert.False(detail.CanWithdrawEarly);
        await service.ApproveAsync(row.Id, employee, default);
        Assert.Equal("Approved", (await Read()).Withdrawal!.Status);
        await service.PayAsync(row.Id, employee, new(row.TotalPenaltyAmount, "Cash", null), default);
        Assert.Equal("Paid", (await Read()).Withdrawal!.PaymentStatus);
        await service.HandoverAsync(row.Id, employee, new("Đã nhận đủ món"), default);
        var completed = await Read();
        Assert.Equal("EarlyWithdrawn", completed.Status);
        Assert.Equal("Completed", completed.Withdrawal!.Status); Assert.False(completed.CanWithdrawEarly);
    }

    [Fact]
    public async Task CustomerTrackingShowsRejectionReasonAndAllowsResubmission()
    {
        var (db, service, customer, employee, product) = Setup();
        await using var cleanup = db;
        var row = (await service.CreateAsync(customer, new([product.Id], "Lấy lại"), default)).Data!;
        await service.RejectAsync(row.Id, employee, new("Cần đối chiếu thông tin nhận hàng"), default);
        var tracking = new CustomerConsignmentService(db, new OCS.Api.Repositories.CustomerProfileRepository(db));
        var detail = await tracking.GetProductAsync(customer, product.ConsignmentContractId!.Value, product.Id, default);
        Assert.True(detail!.CanWithdrawEarly);
        Assert.Equal("Rejected", detail.Withdrawal!.Status);
        Assert.Equal("Cần đối chiếu thông tin nhận hàng", detail.Withdrawal.DecisionNote);
    }

    [Fact]
    public async Task FutureExpiryDoesNotMakePendingProcessingProductsEligibleAndTrackingExplainsWhy()
    {
        var (db, service, customer, _, product) = Setup();
        await using var cleanup = db;
        var contract = await db.ConsignmentContracts.SingleAsync();
        contract.EndsAt = DateTimeOffset.UtcNow.AddMonths(2);
        var pending = await db.Products.SingleAsync();
        pending.Status = ProductStatus.PendingProcessing;
        await db.SaveChangesAsync();
        var tracking = new CustomerConsignmentService(db, new OCS.Api.Repositories.CustomerProfileRepository(db));
        var detail = (await tracking.GetProductAsync(customer, contract.Id, product.Id, default))!;
        Assert.False(detail.CanWithdrawEarly);
        Assert.Contains("Chờ xử lý", detail.EarlyWithdrawalBlockReason);
        Assert.Empty((await service.EligibleAsync(customer, default)).Data!);
        var listed = Assert.Single(Assert.Single((await tracking.GetAsync(customer, default))!).Products);
        Assert.Equal(detail.EarlyWithdrawalBlockReason, listed.EarlyWithdrawalBlockReason);
        pending.OfferForSale();
        await db.SaveChangesAsync();
        var ready = (await tracking.GetProductAsync(customer, contract.Id, product.Id, default))!;
        Assert.True(ready.CanWithdrawEarly); Assert.Null(ready.EarlyWithdrawalBlockReason);
        Assert.Equal(product.Id, Assert.Single((await service.EligibleAsync(customer, default)).Data!).ProductId);
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
