using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Models.Customers;
using OCS.Api.Repositories;
using OCS.Api.Services;

namespace OCS.Api.IntegrationTests;

public sealed class ConsignmentSearchTests
{
    [Fact]
    public async Task FiltersDatesAndTextWithoutLeakingOtherCustomersOrDroppingProducts()
    {
        await using var db = CreateDb();
        var owner = AddCustomer(db);
        var other = AddCustomer(db);
        var a = Contract(owner, "HD-Alpha", "2026-11-01T17:00:00Z", "2026-12-01T17:00:00Z");
        var b = Contract(owner, "HD-Beta", "2026-11-02T16:59:59Z", "2026-12-02T16:59:59Z");
        var before = Contract(owner, "HD-Before", "2026-11-01T16:59:59Z", "2026-12-01T16:59:59Z");
        var foreign = Contract(other, "HD-SECRET", "2026-11-02T00:00:00Z", "2026-12-02T00:00:00Z");
        var brand = new Brand { Name = "Brand", DefaultCommissionRate = 20 };
        var first = Product(a, brand, "Blue Shirt", "ABC-1", ProductStatus.Sold);
        a.Products.Add(first);
        a.Products.Add(Product(a, brand, "Red Skirt", "ABC-2", ProductStatus.OnSale));
        b.Products.Add(Product(b, brand, "Coat", "DEF-1", ProductStatus.OnSale));
        foreign.Products.Add(Product(foreign, brand, "Secret", "ABC-SECRET", ProductStatus.Sold));
        a.Payouts.Add(new Payout { TotalAmount = 100, Status = PayoutStatus.Completed });
        a.Payouts.Add(new Payout { TotalAmount = 999, Status = PayoutStatus.Pending });
        foreign.Payouts.Add(new Payout { TotalAmount = 5000, Status = PayoutStatus.Completed });
        db.ConsignmentContracts.AddRange(a, b, before, foreign);
        await db.SaveChangesAsync();
        var service = new CustomerConsignmentService(db, new CustomerProfileRepository(db));

        var day = await service.SearchAsync(owner, new ConsignmentSearchRequest { SignedFrom = "2026-11-02", SignedTo = "2026-11-02", ExpiresFrom = "2026-12-02", ExpiresTo = "2026-12-02", Status = "Active" }, default);
        Assert.Equal(2, day!.TotalCount);
        Assert.Equal("HD-Beta", day.Items[0].ContractNumber);
        Assert.Equal("HD-Alpha", day.Items[1].ContractNumber);
        Assert.Equal(3, day.Overview.TotalContracts);
        Assert.Equal(2, day.Overview.OnSaleProducts);
        Assert.Equal(1, day.Overview.SoldProducts);
        Assert.Equal(100m, day.Overview.SettledAmount);
        var query = await service.SearchAsync(owner, new ConsignmentSearchRequest { Query = "  blue  ", PageSize = 1 }, default);
        Assert.Single(query!.Items);
        Assert.Equal(2, query.Items[0].Products.Count);
        Assert.Equal(day.Overview, query.Overview);
        Assert.Equal("HD-Alpha", Assert.Single((await service.SearchAsync(owner, new ConsignmentSearchRequest { Query = "abc-2" }, default))!.Items).ContractNumber);
        Assert.Empty((await service.SearchAsync(owner, new ConsignmentSearchRequest { Query = "secret" }, default))!.Items);
        Assert.Equal(1, (await service.SearchAsync(owner, new ConsignmentSearchRequest { SignedTo = "2026-11-01" }, default))!.TotalCount);
        Assert.Equal(2, (await service.SearchAsync(owner, new ConsignmentSearchRequest { SignedFrom = "2026-11-02" }, default))!.TotalCount);
        Assert.Equal(1, (await service.SearchAsync(owner, new ConsignmentSearchRequest { ExpiresTo = "2026-12-01" }, default))!.TotalCount);
        Assert.Equal("HD-Alpha", Assert.Single((await service.SearchAsync(owner, new ConsignmentSearchRequest { Query = "HD-ALPHA" }, default))!.Items).ContractNumber);
        var empty = await service.SearchAsync(owner, new ConsignmentSearchRequest { Status = "Completed" }, default);
        Assert.Empty(empty!.Items);
        Assert.Equal(0, empty.TotalCount);
        Assert.Equal(0, empty.TotalPages);
        Assert.Equal(day.Overview, empty.Overview);
        var second = await service.SearchAsync(owner, new ConsignmentSearchRequest { Page = 2, PageSize = 1 }, default);
        Assert.Equal("HD-Alpha", Assert.Single(second!.Items).ContractNumber);
        Assert.Equal(3, second.TotalPages);
        Assert.Equal(2, (await service.SearchAsync(owner, new ConsignmentSearchRequest { ExpiresFrom = "2026-12-02" }, default))!.TotalCount);
        var beyond = await service.SearchAsync(owner, new ConsignmentSearchRequest { Page = int.MaxValue, PageSize = 100 }, default);
        Assert.Empty(beyond!.Items);
        Assert.Equal(3, beyond.TotalCount);
        Assert.Equal(1, beyond.TotalPages);
        first.Status = ProductStatus.OnSale;
        a.Payouts.Add(new Payout { TotalAmount = 200, Status = PayoutStatus.Completed });
        await db.SaveChangesAsync();
        var refreshed = await service.SearchAsync(owner, new ConsignmentSearchRequest(), default);
        Assert.Equal(0, refreshed!.Overview.SoldProducts);
        Assert.Equal(3, refreshed.Overview.OnSaleProducts);
        Assert.Equal(300m, refreshed.Overview.SettledAmount);
    }

    [Theory]
    [InlineData("2026-02-30", null, null, 1, 5)]
    [InlineData("2026-11-02", "2026-11-01", null, 1, 5)]
    [InlineData(null, null, "unknown", 1, 5)]
    [InlineData(null, null, null, 0, 5)]
    [InlineData(null, null, null, 1, 101)]
    [InlineData("11/01/2026", null, null, 1, 5)]
    public void InvalidFiltersFailValidation(string? from, string? to, string? status, int page, int pageSize)
    {
        var request = new ConsignmentSearchRequest { SignedFrom = from, SignedTo = to, Status = status, Page = page, PageSize = pageSize };
        var errors = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(request, new ValidationContext(request), errors, true));
    }

    private static AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static Guid AddCustomer(AppDbContext db)
    {
        var id = Guid.NewGuid();
        db.Accounts.Add(new Account { Id = id, AccountType = AccountType.Customer, CustomerProfile = new CustomerProfile { Id = id } });
        return id;
    }
    private static ConsignmentContract Contract(Guid id, string number, string signed, string end) => new()
    {
        CustomerId = id,
        ContractNumber = number,
        SignedAt = DateTimeOffset.Parse(signed, System.Globalization.CultureInfo.InvariantCulture),
        EndsAt = DateTimeOffset.Parse(end, System.Globalization.CultureInfo.InvariantCulture),
        Status = ConsignmentContractStatus.Active
    };
    private static Product Product(ConsignmentContract contract, Brand brand, string name, string barcode, ProductStatus status)
    {
        var product = new Product { Name = name, Barcode = barcode };
        product.AssignConsignmentTerms(contract.Id, 1000, brand);
        product.Status = status;
        return product;
    }
}
