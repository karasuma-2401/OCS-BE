using Microsoft.EntityFrameworkCore;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Models.Products;
using OCS.Api.Services;
using Npgsql;
using Microsoft.AspNetCore.Authorization;
using OCS.Api.Authorization;
using OCS.Api.Controllers;

namespace OCS.Api.IntegrationTests;

public sealed class InventoryServiceTests
{
    [Theory]
    [InlineData(nameof(InventoryController.List), PermissionCatalog.ProductsRead)]
    [InlineData(nameof(InventoryController.Detail), PermissionCatalog.ProductsRead)]
    [InlineData(nameof(InventoryController.Review), PermissionCatalog.ProductsApprove)]
    [InlineData(nameof(InventoryController.Resubmit), PermissionCatalog.ProductsCreate)]
    [InlineData(nameof(InventoryController.Offer), PermissionCatalog.ProductsOffer)]
    [InlineData(nameof(InventoryController.ChangeLocation), PermissionCatalog.ProductsLocation)]
    [InlineData(nameof(InventoryController.CreateLocation), PermissionCatalog.ProductsLocationsManage)]
    [InlineData(nameof(InventoryController.ChangePrice), PermissionCatalog.ProductsPrice)]
    [InlineData(nameof(InventoryController.ChangeDetails), PermissionCatalog.ProductsCreate)]
    public void InventoryEndpointRequiresItsOwnPermission(string method, string permission)
    {
        var action = typeof(InventoryController).GetMethod(method)!;
        Assert.Contains(action.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>(),
            attribute => attribute.Policy == permission);
    }

    private static async Task<(AppDbContext Db, Product Product, InventoryService Service)> SetupConsignment()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new AppDbContext(options);
        var brand = new Brand { Name = "Brand", DefaultCommissionRate = 20 };
        var category = new Category { Name = "Áo" };
        var contract = new ConsignmentContract { SignedAt = DateTimeOffset.UtcNow.AddDays(-1), EndsAt = DateTimeOffset.UtcNow.AddDays(30), Status = ConsignmentContractStatus.Active };
        var location = new StorageLocation { Code = "A1", Name = "Kệ A" };
        var product = new Product { Name = "Áo", Barcode = "123", Size = "M", BrandId = brand.Id, CategoryId = category.Id, StorageLocationId = location.Id };
        product.AssignConsignmentTerms(contract.Id, 150000, brand);
        db.AddRange(brand, category, contract, location, product);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return (db, product, new InventoryService(db));
    }

    [Fact]
    public async Task ConsignmentRequiresVerifiedAgreementAndApprovalBeforeSale()
    {
        var (db, product, service) = await SetupConsignment();
        await using (db)
        {
            var actor = Guid.NewGuid();
            Assert.False((await service.OfferAsync(product.Id, actor, new(Guid.Empty), default)).IsSuccess);
            var beforeApproval = await service.OfferAsync(product.Id, actor, new(product.InventoryVersion), default);
            Assert.False(beforeApproval.IsSuccess);
            var missingEvidence = await service.ReviewAsync(product.Id, actor, new(product.InventoryVersion, true, null, 150000, null), default);
            Assert.False(missingEvidence.IsSuccess);
            var wrongPrice = await service.ReviewAsync(product.Id, actor, new(product.InventoryVersion, true, null, 149000, "Hợp đồng 01"), default);
            Assert.False(wrongPrice.IsSuccess);
            var approved = await service.ReviewAsync(product.Id, actor, new(product.InventoryVersion, true, null, 150000, "Hợp đồng 01"), default);
            Assert.True(approved.IsSuccess);
            var offered = await service.OfferAsync(product.Id, actor, new(approved.Data!.Version), default);
            Assert.True(offered.IsSuccess);
            Assert.Equal("OnSale", offered.Data!.Status);
            Assert.Equal(2, await db.InventoryEvents.CountAsync());
            Assert.Equal(1, await db.ProductStatusHistories.CountAsync());
            var saved = await db.Products.SingleAsync(x => x.Id == product.Id);
            Assert.Equal(150000, saved.AgreedListedPrice);
            Assert.Throws<InvalidOperationException>(() => saved.ListedPrice = 100000);
        }
    }

    [Fact]
    public async Task ActiveProductDetailsCanBeEditedWithVersionAndAuditButSoldProductCannot()
    {
        var (db, product, service) = await SetupConsignment();
        await using (db)
        {
            var actor = Guid.NewGuid();
            var approved = await service.ReviewAsync(product.Id, actor, new(product.InventoryVersion, true, null, 150000, "Hợp đồng 01"), default);
            Assert.True(approved.IsSuccess);
            var offered = await service.OfferAsync(product.Id, actor, new(approved.Data!.Version), default);
            Assert.True(offered.IsSuccess);
            var outdated = await service.ChangeInfoAsync(product.Id, actor, new(approved.Data.Version, "Tên cũ", "M", null, 90), default);
            Assert.False(outdated.IsSuccess);
            var updated = await service.ChangeInfoAsync(product.Id, actor, new(offered.Data!.Version, "Áo mới", "L", "Tình trạng tốt", 90), default);
            Assert.True(updated.IsSuccess);
            Assert.Equal("Áo mới", updated.Data!.Name);
            Assert.Equal("OnSale", updated.Data.Status);
            Assert.Equal(150000, updated.Data.ListedPrice);
            Assert.NotEqual(offered.Data.Version, updated.Data.Version);
            Assert.Contains(await db.InventoryEvents.ToListAsync(), entry => entry.Action == "Details" && entry.ActorAccountId == actor);
            var tracked = await db.Products.SingleAsync(x => x.Id == product.Id);
            tracked.Status = ProductStatus.Sold;
            await db.SaveChangesAsync();
            var denied = await service.ChangeInfoAsync(product.Id, actor, new(updated.Data.Version, "Không được sửa", "L", null, 90), default);
            Assert.False(denied.IsSuccess);
            Assert.Equal("Áo mới", tracked.Name);
        }
    }

    [Fact]
    public async Task RejectionIsNotSupportedAndLegacyRejectedProductsCanBeResubmitted()
    {
        var (db, product, service) = await SetupConsignment();
        await using (db)
        {
            var actor = Guid.NewGuid();
            var noReason = await service.ReviewAsync(product.Id, actor, new(product.InventoryVersion, false, null, null, null), default);
            Assert.False(noReason.IsSuccess);
            var rejected = await service.ReviewAsync(product.Id, actor, new(product.InventoryVersion, false, "Cần bổ sung ảnh", null, null), default);
            Assert.False(rejected.IsSuccess);
            Assert.Equal(ProductApprovalStatus.Pending, (await db.Products.SingleAsync()).ApprovalStatus);
            Assert.Empty(db.InventoryEvents);

            var existing = await db.Products.SingleAsync();
            existing.ApprovalStatus = ProductApprovalStatus.Rejected;
            existing.InventoryVersion = Guid.NewGuid();
            await db.SaveChangesAsync();
            var stale = await service.ResubmitAsync(product.Id, actor, new(product.InventoryVersion), default);
            Assert.False(stale.IsSuccess);
            var resubmitted = await service.ResubmitAsync(product.Id, actor, new(existing.InventoryVersion), default);
            Assert.True(resubmitted.IsSuccess);
            Assert.Equal("Pending", resubmitted.Data!.ApprovalStatus);
            Assert.Single(db.InventoryEvents);
        }
    }

    [Fact]
    public async Task BuyoutMustBeApprovedBeforeReviewAndPriceChangesAreAudited()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);
        var brand = new Brand { Name = "Brand" };
        var category = new Category { Name = "Áo" };
        var buyout = new Buyout { Status = BuyoutStatus.Pending };
        var product = new Product { Name = "Áo", Barcode = "B01", Size = "M", BrandId = brand.Id, CategoryId = category.Id };
        product.AssignBuyoutTerms(buyout.Id, 200000, 100000);
        db.AddRange(brand, category, buyout, product);
        await db.SaveChangesAsync();
        var service = new InventoryService(db);
        var actor = Guid.NewGuid();
        Assert.False((await service.ReviewAsync(product.Id, actor, new(product.InventoryVersion, true, null, null, null), default)).IsSuccess);
        buyout.Status = BuyoutStatus.Approved;
        buyout.ApprovedByEmployeeId = Guid.NewGuid();
        await db.SaveChangesAsync();
        var changed = await service.ChangePriceAsync(product.Id, actor, new(product.InventoryVersion, 250000, "Điều chỉnh giá"), default);
        Assert.True(changed.IsSuccess);
        var entry = await db.InventoryEvents.SingleAsync();
        Assert.Equal("200000", entry.PreviousValue);
        Assert.Equal("250000", entry.NewValue);
        Assert.Equal(100000, product.PurchasePrice);
    }

    [PostgresFact]
    public async Task MigrationCreatesPendingDefaultAndInventoryPermissions()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var connection = await database.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT column_default FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'Products' AND column_name = 'ApprovalStatus'
            """, connection);
        Assert.Contains("Pending", (string)(await command.ExecuteScalarAsync())!);
        command.CommandText = """
            SELECT column_default FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'Products' AND column_name = 'InventoryVersion'
            """;
        Assert.Contains("gen_random_uuid", (string)(await command.ExecuteScalarAsync())!);
        command.CommandText = "SELECT count(*) FROM \"Permissions\" WHERE \"Code\" LIKE 'products.%'";
        Assert.True((long)(await command.ExecuteScalarAsync())! >= 7);
        command.CommandText = "SELECT to_regclass('\"InventoryEvents\"') IS NOT NULL";
        Assert.True((bool)(await command.ExecuteScalarAsync())!);
    }

    [PostgresFact]
    public async Task ApprovedBuyoutCanBeOfferedAndConcurrentWritesConflictInPostgres()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var db = database.Context();
        var customerAccount = new Account { AccountType = AccountType.Customer, FullName = "Customer", PhoneNumber = "0900000001" };
        var employeeAccount = new Account { AccountType = AccountType.Internal, FullName = "Employee", PhoneNumber = "0900000002", Username = "inventory-test" };
        var customer = new CustomerProfile { Id = customerAccount.Id, Account = customerAccount };
        var employee = new EmployeeProfile { Id = employeeAccount.Id, Account = employeeAccount, JobTitle = "Manager", HiredOn = DateOnly.FromDateTime(DateTime.UtcNow) };
        var brand = new Brand { Name = "Inventory test" };
        var category = new Category { Name = "Inventory test" };
        var location = new StorageLocation { Code = "T01", Name = "Test shelf" };
        var buyout = new Buyout
        {
            BuyoutNumber = "TEST-1",
            CustomerId = customer.Id,
            CreatedByEmployeeId = employee.Id,
            ApprovedByEmployeeId = employee.Id,
            Status = BuyoutStatus.Approved,
            TotalPurchasePrice = 100000
        };
        var contract = new ConsignmentContract
        {
            ContractNumber = "TEST-CONTRACT-1",
            CustomerId = customer.Id,
            CreatedByEmployeeId = employee.Id,
            SignedAt = DateTimeOffset.UtcNow.AddDays(-1),
            EndsAt = DateTimeOffset.UtcNow.AddDays(30)
        };
        var product = new Product
        {
            Name = "Test item",
            Barcode = "TEST-BARCODE",
            Size = "M",
            BrandId = brand.Id,
            CategoryId = category.Id,
            StorageLocationId = location.Id
        };
        product.AssignBuyoutTerms(buyout.Id, 200000, 100000);
        var consignment = new Product
        {
            Name = "Consignment",
            Barcode = "TEST-CONSIGNMENT",
            Size = "S",
            BrandId = brand.Id,
            CategoryId = category.Id,
            StorageLocationId = location.Id
        };
        consignment.AssignConsignmentTerms(contract.Id, 300000, brand);
        db.AddRange(customerAccount, employeeAccount, customer, employee, brand, category, location, buyout, contract, product, consignment);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var service = new InventoryService(db);
        var reviewed = await service.ReviewAsync(product.Id, employee.Id, new(product.InventoryVersion, true, null, null, null), default);
        Assert.True(reviewed.IsSuccess);
        var offered = await service.OfferAsync(product.Id, employee.Id, new(reviewed.Data!.Version), default);
        Assert.True(offered.IsSuccess);
        var detail = await service.DetailAsync(product.Id, default);
        Assert.Equal("OnSale", detail.Data!.Product.Status);
        Assert.Equal(2, detail.Data.History.Count);
        var consignmentReview = await service.ReviewAsync(consignment.Id, employee.Id,
            new(consignment.InventoryVersion, true, null, 300000, "Hợp đồng TEST-CONTRACT-1"), default);
        Assert.True(consignmentReview.IsSuccess);
        Assert.True((await service.OfferAsync(consignment.Id, employee.Id, new(consignmentReview.Data!.Version), default)).IsSuccess);

        await using var first = database.Context();
        await using var second = database.Context();
        var firstProduct = await first.Products.SingleAsync(x => x.Id == product.Id);
        var secondProduct = await second.Products.SingleAsync(x => x.Id == product.Id);
        firstProduct.InventoryVersion = Guid.NewGuid();
        secondProduct.InventoryVersion = Guid.NewGuid();
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }
}
