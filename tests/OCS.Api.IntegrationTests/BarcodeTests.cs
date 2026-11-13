using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Controllers;
using OCS.Api.Entities;
using OCS.Api.Models.Contracts;
using OCS.Api.Models.Products;
using OCS.Api.Services;

namespace OCS.Api.IntegrationTests;

public sealed class BarcodeTests
{
    [Fact]
    public async Task DraftBarcodeIsCheckedAndPreservedWhenContractIsCreated()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.WordDocumentTemplates.Add(new WordDocumentTemplate { Kind = WordTemplateSamples.Contract, Content = WordTemplateSamples.Create(WordTemplateSamples.Contract), Version = Guid.Empty, UpdatedAt = DateTimeOffset.UtcNow });
        var employee = new EmployeeProfile { Id = Guid.NewGuid() };
        var brand = new Brand { Name = "Nhãn", DefaultCommissionRate = 20 };
        var category = new Category { Name = "Áo" };
        db.AddRange(employee, brand, category);
        await db.SaveChangesAsync();
        var service = new ContractService(db);
        var barcode = await service.GenerateBarcodeAsync(default);
        Assert.Matches("^OCS-[0-9A-F]{16}$", barcode);
        var now = DateTimeOffset.UtcNow;
        var product = new ContractProductRequest { Name = "Áo", Size = "M", Barcode = barcode, BrandId = brand.Id, CategoryId = category.Id, AgreedCommissionRate = 20, ListedPrice = 120000 };
        var request = new CreateContractRequest { RequestId = Guid.NewGuid(), SignedAt = now, EndsAt = now.AddDays(30), ExpiryHandling = "Return", Guest = new ContractGuestRequest { FullName = "Khách", PhoneNumber = "0901234567" }, Products = [product] };
        Assert.True((await service.PreviewAsync(request, employee.Id, default)).IsSuccess);
        Assert.Empty(db.Products);
        var result = await service.CreateAsync(request, employee.Id, default);
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(barcode, (await db.Products.SingleAsync()).Barcode);
        Assert.Equal(barcode, Assert.Single((await service.GetBarcodeProductsAsync(result.Data!.Id, default))!).Barcode);
        var repeated = await service.PreviewAsync(request with { RequestId = Guid.NewGuid() }, employee.Id, default);
        Assert.Equal("PRODUCT.DUPLICATE_BARCODE", repeated.Error!.Code);
        var sameRequest = await service.PreviewAsync(request with { RequestId = Guid.NewGuid(), Products = [product, product] }, employee.Id, default);
        Assert.Equal("PRODUCT.DUPLICATE_BARCODE", sameRequest.Error!.Code);
    }

    [Fact]
    public async Task LabelTemplateChecksDimensionsAndVersion()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var service = new BarcodeLabelTemplateService(db);
        var initial = await service.GetAsync(default);
        Assert.Equal("BARCODE_TEMPLATE.INVALID", (await service.UpdateAsync(initial with { WidthMm = 90, Columns = 4 }, default)).Error!.Code);
        var saved = await service.UpdateAsync(initial with { WidthMm = 60, Columns = 3 }, default);
        Assert.True(saved.IsSuccess);
        Assert.NotEqual(Guid.Empty, saved.Data!.Version);
        Assert.Equal("BARCODE_TEMPLATE.CHANGED", (await service.UpdateAsync(initial, default)).Error!.Code);
        var controller = typeof(BarcodeLabelTemplateController);
        Assert.Equal("Admin,Manager", controller.GetMethod(nameof(BarcodeLabelTemplateController.Update))!.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
    }

    [PostgresFact]
    public async Task MigrationBackfillsMissingOnlyAndEnforcesNormalizedUniqueness()
    {
        await using var database = await TestDatabase.CreateAsync("20261113043305_AddContractPdfBackground");
        await using (var context = database.Context())
        {
            var brand = new Brand { Name = "Nhãn", DefaultCommissionRate = 20 };
            var category = new Category { Name = "Áo" };
            var customerAccount = new Account { AccountType = AccountType.Customer, FullName = "Khách thử", PhoneNumber = "0900000001" };
            var employeeAccount = new Account { AccountType = AccountType.Internal, FullName = "Nhân viên thử", PhoneNumber = "0900000002", Username = "barcode-test" };
            var customer = new CustomerProfile { Id = customerAccount.Id, Account = customerAccount };
            var employee = new EmployeeProfile { Id = employeeAccount.Id, Account = employeeAccount, JobTitle = "Manager", HiredOn = DateOnly.FromDateTime(DateTime.UtcNow) };
            var buyout = new Buyout { BuyoutNumber = "BARCODE-TEST", CustomerId = customer.Id, CreatedByEmployeeId = employee.Id };
            var existing = new Product { Name = "Cũ", Size = "M", Barcode = "EXISTING-1", Brand = brand, Category = category };
            var missing = new Product { Name = "Thiếu mã", Size = "M", Barcode = "", Brand = brand, Category = category };
            existing.AssignBuyoutTerms(buyout.Id, 100000, 50000);
            missing.AssignBuyoutTerms(buyout.Id, 100000, 50000);
            context.AddRange(customer, employee, buyout, existing, missing);
            await context.SaveChangesAsync();
        }
        await database.MigrateAsync();
        await using var check = database.Context();
        var products = await check.Products.OrderBy(x => x.Name).ToListAsync();
        Assert.Equal("EXISTING-1", products.Single(x => x.Name == "Cũ").Barcode);
        Assert.Matches("^OCS-[0-9A-F]{16}$", products.Single(x => x.Name == "Thiếu mã").Barcode);
        products.Single(x => x.Name == "Thiếu mã").Barcode = "existing-1";
        await Assert.ThrowsAsync<DbUpdateException>(() => check.SaveChangesAsync());
    }
}
