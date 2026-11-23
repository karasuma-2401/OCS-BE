using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OCS.Api.Authorization;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Controllers;
using OCS.Api.Entities;
using OCS.Api.Models.Products;
using OCS.Api.Models.Pos;
using OCS.Api.Services;

namespace OCS.Api.IntegrationTests;

public sealed class ExpiryProcessingTests
{
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static (AppDbContext Db, ExpiryProcessingService Service, Product[] Products) Setup(
        DateTimeOffset now, ExpiryHandling handling = ExpiryHandling.Return,
        int? collectionDays = null, bool allowUnclaimed = false)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var brand = new Brand { Name = "Brand", DefaultCommissionRate = 20 };
        var category = new Category { Name = "Áo" };
        var contract = new ConsignmentContract
        {
            ContractNumber = "KG-01",
            SignedAt = now.AddDays(-60),
            EndsAt = now.AddDays(-2),
            ExpiryHandling = handling,
            CustomerNameSnapshot = "Khách",
            Status = ConsignmentContractStatus.Active
        };
        var products = Enumerable.Range(1, 2).Select(i =>
        {
            var p = new Product { Name = $"Áo {i}", Barcode = $"BC-{i}", Size = "M", BrandId = brand.Id, CategoryId = category.Id };
            p.AssignConsignmentTerms(contract.Id, 100000, brand);
            p.OfferForSale();
            return p;
        }).ToArray();
        db.AddRange(brand, category, contract);
        db.Products.AddRange(products);
        db.SaveChanges();
        db.ChangeTracker.Clear();
        var settings = new Dictionary<string, string?>
        {
            ["ExpiryProcessing:CollectionDays"] = collectionDays?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["ExpiryProcessing:AllowUnclaimedDonation"] = allowUnclaimed.ToString()
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return (db, new ExpiryProcessingService(db, new FixedClock(now), config), products);
    }

    [Fact]
    public async Task JobExpiresOnlyOnceAndCreatesOneCaseForContract()
    {
        var (db, service, _) = Setup(DateTimeOffset.UtcNow);
        await using (db)
        {
            var contract = await db.ConsignmentContracts.SingleAsync();
            contract.CustomerId = Guid.NewGuid();
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            Assert.Equal(2, await service.ExpireAsync(default));
            db.ChangeTracker.Clear();
            Assert.Equal(0, await service.ExpireAsync(default));
            Assert.Single(await db.ExpiryProcessings.ToListAsync());
            Assert.Equal(2, await db.ExpiryProcessingItems.CountAsync());
            Assert.Equal(2, await db.ProductStatusHistories.CountAsync());
            Assert.Single(await db.Notifications.ToListAsync());
            Assert.All(await db.Products.ToListAsync(), p => Assert.Equal(ProductStatus.Expired, p.Status));
            var queue = await service.ListAsync(new ExpiryQuery(Status: "Pending", Type: "Return"), default);
            Assert.True(queue.IsSuccess);
            Assert.Single(queue.Data!.Items);
        }
    }

    [Fact]
    public async Task JobUsesExactContractEndAndDoesNotDonateWithoutHandover()
    {
        var now = DateTimeOffset.UtcNow;
        var (db, service, _) = Setup(now, ExpiryHandling.Donate);
        await using (db)
        {
            var contract = await db.ConsignmentContracts.SingleAsync();
            contract.EndsAt = now.AddSeconds(1);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            Assert.Equal(0, await service.ExpireAsync(default));
            contract = await db.ConsignmentContracts.SingleAsync();
            contract.EndsAt = now;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            Assert.Equal(2, await service.ExpireAsync(default));
            var processing = await db.ExpiryProcessings.SingleAsync();
            Assert.Equal(ExpiryProcessingType.Donate, processing.Type);
            Assert.Equal(ExpiryProcessingStatus.Pending, processing.Status);
            Assert.All(await db.Products.ToListAsync(), p => Assert.Equal(ProductStatus.Expired, p.Status));
        }
    }

    [Fact]
    public async Task UnclaimedCannotBeApprovedWithoutPolicyOrDeadline()
    {
        var (db, service, _) = Setup(DateTimeOffset.UtcNow);
        await using (db)
        {
            await service.ExpireAsync(default);
            var id = (await db.ExpiryProcessings.SingleAsync()).Id;
            var result = await service.MarkUnclaimedAsync(id, Guid.NewGuid(), new MarkUnclaimedRequest("Khách xác nhận không nhận"), default);
            Assert.False(result.IsSuccess);
            Assert.Equal(ExpiryProcessingStatus.Pending, (await db.ExpiryProcessings.SingleAsync()).Status);
        }
    }

    [Fact]
    public async Task ReturnConfirmsOnlyScannedProductAndRejectsRepeat()
    {
        var (db, service, _) = Setup(DateTimeOffset.UtcNow);
        await using (db)
        {
            await service.ExpireAsync(default);
            db.ChangeTracker.Clear();
            var processing = await db.ExpiryProcessings.SingleAsync();
            var actor = Guid.NewGuid();
            var first = await service.ConfirmReturnAsync(processing.Id, actor, new ConfirmReturnRequest(["BC-1"]), default);
            Assert.True(first.IsSuccess);
            Assert.Equal("Pending", first.Data!.Status);
            Assert.Equal("Returned", first.Data.Products.Single(x => x.Barcode == "BC-1").Status);
            Assert.Equal("Expired", first.Data.Products.Single(x => x.Barcode == "BC-2").Status);
            Assert.False((await service.ConfirmReturnAsync(processing.Id, actor, new ConfirmReturnRequest(["BC-1"]), default)).IsSuccess);
            var second = await service.ConfirmReturnAsync(processing.Id, actor, new ConfirmReturnRequest(["BC-2"]), default);
            Assert.Equal("Completed", second.Data!.Status);
            Assert.Equal(4, await db.ProductStatusHistories.CountAsync());
        }
    }

    [Fact]
    public async Task CustomerFiltersRunBeforePaginationAndMatchGuestSnapshots()
    {
        var (db, service, _) = Setup(DateTimeOffset.UtcNow);
        await using (db)
        {
            await service.ExpireAsync(default);
            var byName = await service.ListAsync(new ExpiryQuery(PageSize: 1, CustomerQuery: "  KHÁCH  "), default);
            Assert.True(byName.IsSuccess);
            Assert.Equal(1, byName.Data!.TotalCount);
            var byUnknownId = await service.ListAsync(new ExpiryQuery(CustomerId: Guid.NewGuid()), default);
            Assert.Empty(byUnknownId.Data!.Items);
            Assert.Equal(0, byUnknownId.Data.TotalCount);
            var invalid = await service.ListAsync(new ExpiryQuery(CustomerQuery: new string('x', 201)), default);
            Assert.Equal("EXPIRY.CUSTOMER_FILTER", invalid.Error!.Code);
        }
    }

    [Fact]
    public async Task EachDonationHandoverHasImmutableReceiptAndRepeatIsRejected()
    {
        var (db, service, _) = Setup(DateTimeOffset.UtcNow, ExpiryHandling.Donate);
        await using (db)
        {
            await service.ExpireAsync(default);
            db.ChangeTracker.Clear();
            var id = (await db.ExpiryProcessings.SingleAsync()).Id;
            var actor = Guid.NewGuid();
            Assert.True((await service.ConfirmDonationAsync(id, actor,
                new ConfirmDonationRequest(["BC-1"], "Tổ chức A", "Biên nhận A", null), default)).IsSuccess);
            Assert.False((await service.ConfirmDonationAsync(id, actor,
                new ConfirmDonationRequest(["BC-1"], "Tổ chức A", "Biên nhận A", null), default)).IsSuccess);
            Assert.True((await service.ConfirmDonationAsync(id, actor,
                new ConfirmDonationRequest(["BC-2"], "Tổ chức B", "Biên nhận B", "Lần hai"), default)).IsSuccess);
            db.ChangeTracker.Clear();
            var receipts = await service.ReceiptsAsync(id, default);
            Assert.Equal(2, receipts.Data!.Count);
            var firstReceipt = receipts.Data.Single(x => x.Evidence == "Biên nhận A");
            var secondReceipt = receipts.Data.Single(x => x.Evidence == "Biên nhận B");
            Assert.Equal(["BC-1"], firstReceipt.Products.Select(x => x.Barcode));
            Assert.Equal(["BC-2"], secondReceipt.Products.Select(x => x.Barcode));
            Assert.Equal("Tổ chức A", firstReceipt.RecipientOrganization);
            Assert.Equal("Tổ chức B", secondReceipt.RecipientOrganization);
            var product = await db.Products.SingleAsync(x => x.Barcode == "BC-1");
            product.Name = "Tên đã đổi";
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var fetched = await service.ReceiptAsync(id, firstReceipt.Id, default);
            Assert.Equal(firstReceipt.ReceiptNumber, fetched.Data!.ReceiptNumber);
            Assert.Equal(firstReceipt.Products[0].ProductId, fetched.Data.Products[0].ProductId);
            Assert.Equal("Áo 1", fetched.Data.Products[0].Name);
            Assert.False((await service.ReceiptAsync(Guid.NewGuid(), firstReceipt.Id, default)).IsSuccess);
        }
    }

    [Fact]
    public async Task UnclaimedNeedsConfiguredPolicyAndEvidenceBeforeDonation()
    {
        var now = DateTimeOffset.UtcNow;
        var (db, service, _) = Setup(now, collectionDays: 1, allowUnclaimed: true);
        await using (db)
        {
            await service.ExpireAsync(default);
            db.ChangeTracker.Clear();
            var processing = await db.ExpiryProcessings.SingleAsync();
            Assert.False((await service.MarkUnclaimedAsync(processing.Id, Guid.NewGuid(), new MarkUnclaimedRequest(""), default)).IsSuccess);
            var changed = await service.MarkUnclaimedAsync(processing.Id, Guid.NewGuid(), new MarkUnclaimedRequest("Điều khoản hợp đồng và biên bản liên hệ"), default);
            Assert.True(changed.IsSuccess);
            Assert.Equal("OverdueDonated", changed.Data!.Status);
            Assert.All(changed.Data.Products, x => Assert.Equal("Expired", x.Status));
            Assert.False((await service.ConfirmDonationAsync(processing.Id, Guid.NewGuid(),
                new ConfirmDonationRequest(["BC-1"], "", "", null), default)).IsSuccess);
            var donated = await service.ConfirmDonationAsync(processing.Id, Guid.NewGuid(),
                new ConfirmDonationRequest(["BC-1"], "Tổ chức A", "Biên nhận 123", null), default);
            Assert.True(donated.IsSuccess);
            Assert.Equal("Donated", donated.Data!.Products.Single(x => x.Barcode == "BC-1").Status);
            Assert.Equal("Biên nhận 123", donated.Data.Products.Single(x => x.Barcode == "BC-1").Evidence);
        }
    }

    [Fact]
    public async Task PosLookupRejectsExpiredContractBeforeJobRuns()
    {
        var (db, _, products) = Setup(DateTimeOffset.UtcNow);
        await using (db)
        {
            var lookup = await new PosService(db).LookupPosProductAsync(products[0].Barcode);
            Assert.True(lookup.IsSuccess);
            Assert.False(lookup.Data!.CanAddToCart);
            Assert.Equal(ProductStatus.OnSale.ToString(), lookup.Data.Status);
        }
    }

    [Fact]
    public async Task CashCheckoutRejectsExpiredContractBeforeJobRuns()
    {
        var (db, _, products) = Setup(DateTimeOffset.UtcNow);
        await using (db)
        {
            var actor = Guid.NewGuid();
            db.EmployeeProfiles.Add(new EmployeeProfile { Id = actor, JobTitle = "Staff" });
            await db.SaveChangesAsync();
            var result = await new PosService(db).CheckoutCashAsync(actor,
                new PosCashPaymentRequest([products[0].Id], 100000));
            Assert.False(result.IsSuccess);
            Assert.Equal("POS.PRODUCT_NOT_AVAILABLE", result.Error!.Code);
            Assert.Empty(await db.SaleInvoices.ToListAsync());
        }
    }

    [Theory]
    [InlineData(nameof(ExpiryProcessingsController.List), PermissionCatalog.ExpiryRead)]
    [InlineData(nameof(ExpiryProcessingsController.Detail), PermissionCatalog.ExpiryRead)]
    [InlineData(nameof(ExpiryProcessingsController.ConfirmReturn), PermissionCatalog.ExpiryReturn)]
    [InlineData(nameof(ExpiryProcessingsController.ConfirmDonation), PermissionCatalog.ExpiryDonate)]
    [InlineData(nameof(ExpiryProcessingsController.MarkUnclaimed), PermissionCatalog.ExpiryMarkUnclaimed)]
    [InlineData(nameof(ExpiryProcessingsController.DonationReceipts), PermissionCatalog.ExpiryRead)]
    [InlineData(nameof(ExpiryProcessingsController.DonationReceipt), PermissionCatalog.ExpiryRead)]
    public void EndpointsRequireDedicatedPermission(string method, string permission)
    {
        var action = typeof(ExpiryProcessingsController).GetMethod(method)!;
        Assert.Contains(action.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>(),
            attribute => attribute.Policy == permission);
    }
}
