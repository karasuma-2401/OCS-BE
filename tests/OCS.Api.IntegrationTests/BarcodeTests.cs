using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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
        var result = await service.CreateAsync(request with { Signature = request.Signature ?? new ContractImageRequest { ContentType = "image/png", Data = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=" } }, employee.Id, default);
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
        var brandId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var customerAccountId = Guid.NewGuid();
        var employeeAccountId = Guid.NewGuid();
        var buyoutId = Guid.NewGuid();
        var existingProductId = Guid.NewGuid();
        var missingProductId = Guid.NewGuid();

        await using (var connection = await database.OpenAsync())
        {
            await using var command = new NpgsqlCommand($"""
                INSERT INTO "Brands" ("Id", "Name", "DefaultCommissionRate", "Tier", "IsActive", "CreatedAt")
                VALUES ('{brandId}', 'Nhãn', 20, 'LocalBrand', true, now());

                INSERT INTO "Categories" ("Id", "Name", "IsActive", "CreatedAt")
                VALUES ('{categoryId}', 'Áo', true, now());

                INSERT INTO "Accounts" ("Id", "FullName", "PhoneNumber", "AccountType", "Username", "Status", "CreatedAt")
                VALUES ('{customerAccountId}', 'Khách thử', '0900000001', 'Customer', 'cust_barcode', 'PendingActivation', now()),
                       ('{employeeAccountId}', 'Nhân viên thử', '0900000002', 'Internal', 'barcode-test', 'PendingActivation', now());

                INSERT INTO "CustomerProfiles" ("Id", "Status")
                VALUES ('{customerAccountId}', 'Active');

                INSERT INTO "EmployeeProfiles" ("Id", "JobTitle", "HiredOn", "Status")
                VALUES ('{employeeAccountId}', 'Manager', CURRENT_DATE, 'Active');

                INSERT INTO "Buyouts" ("Id", "BuyoutNumber", "CustomerId", "CreatedByEmployeeId", "Status", "TotalPurchasePrice", "CreatedAt")
                VALUES ('{buyoutId}', 'BARCODE-TEST', '{customerAccountId}', '{employeeAccountId}', 'Pending', 0, now());

                INSERT INTO "Products" ("Id", "Name", "Size", "Barcode", "BrandId", "CategoryId", "SourceType", "BuyoutId", "ListedPrice", "PurchasePrice", "ConditionPercentage", "Status", "ApprovalStatus", "InventoryVersion", "CreatedAt")
                VALUES ('{existingProductId}', 'Cũ', 'M', 'EXISTING-1', '{brandId}', '{categoryId}', 'Buyout', '{buyoutId}', 100000, 50000, 100, 'PendingProcessing', 'Pending', gen_random_uuid(), now()),
                       ('{missingProductId}', 'Thiếu mã', 'M', '', '{brandId}', '{categoryId}', 'Buyout', '{buyoutId}', 100000, 50000, 100, 'PendingProcessing', 'Pending', gen_random_uuid(), now());
                """, connection);
            await command.ExecuteNonQueryAsync();
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
