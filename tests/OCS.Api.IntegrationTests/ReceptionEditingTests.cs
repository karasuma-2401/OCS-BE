using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Models.Consignments;
using OCS.Api.Models.Contracts;
using OCS.Api.Services;
using OCS.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace OCS.Api.IntegrationTests;

public sealed class ReceptionEditingTests
{
    private static ContractImageRequest Signature() => new() { ContentType = "image/png", Data = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=" };
    [Fact]
    public async Task ReceptionUsesEachAgreedRateAndAcceptsCoverAlsoListedAmongImages()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var staff = new Account { AccountType = AccountType.Internal, Status = AccountStatus.Active, FullName = "Staff", PhoneNumber = "0900000001" };
        var customer = new Account { AccountType = AccountType.Customer, Status = AccountStatus.Active, FullName = "Customer", PhoneNumber = "0900000003" };
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = staff.Id, Account = staff, Status = EmployeeStatus.Working });
        db.CustomerProfiles.Add(new CustomerProfile { Id = customer.Id, Account = customer });
        var brand = new Brand { Name = "Brand", DefaultCommissionRate = 20 };
        var category = new Category { Name = "Category" };
        db.AddRange(brand, category);
        await db.SaveChangesAsync();
        var image = "/api/storage/products/reception-test.jpg";
        var service = new ConsignmentService(db, null!);
        var request = new CreateReceptionRequest
        {
            CustomerInfo = new ReceptionCustomerInfoDto { FullName = customer.FullName, PhoneNumber = customer.PhoneNumber },
            ActualItemCount = 2,
            Note = "Ghi chú đã xác nhận",
            Signature = Signature(),
            TemplateVersion = Guid.Empty,
            Items = [
                new CreateReceptionItemDto { Name = "A", CategoryId = category.Id, BrandId = brand.Id, Size = "M", Material = "Linen", ListedPrice = 100000, CommissionRate = 12, CoverImageUrl = image, ImageUrls = [image] },
                new CreateReceptionItemDto { Name = "B", CategoryId = category.Id, BrandId = brand.Id, Size = "L", ListedPrice = 100000, CommissionRate = 25 }
            ]
        };
        var result = await service.CreateReceptionAsync(staff.Id, request);
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(163000m, result.Data!.TotalCustomerPayout);
        Assert.Equal(12m, (await db.Products.SingleAsync(p => p.Name == "A")).AgreedCommissionRate);
        Assert.Equal(25m, (await db.Products.SingleAsync(p => p.Name == "B")).AgreedCommissionRate);
        Assert.Single(db.ProductImages);
        var document = Assert.Single(db.ContractDocuments);
        Assert.Equal(Convert.FromBase64String(Signature().Data), document.SignatureContent);
        using var snapshot = System.Text.Json.JsonDocument.Parse(document.Content);
        Assert.Equal(2, snapshot.RootElement.GetProperty("products").GetArrayLength());
        Assert.Equal(image, snapshot.RootElement.GetProperty("products")[0].GetProperty("images")[0].GetString());
        var changed = await db.Products.SingleAsync(p => p.Name == "A");
        changed.Name = "Tên đã đổi trong kho";
        await db.SaveChangesAsync();
        var controller = new ContractPdfController(new ContractService(db), null!, db)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], "test")) } }
        };
        var response = Assert.IsType<OkObjectResult>(await controller.Web(result.Data.ContractId, default));
        var json = System.Text.Json.JsonSerializer.SerializeToElement(response.Value, System.Text.Json.JsonSerializerOptions.Web);
        Assert.Equal("A", json.GetProperty("products")[0].GetProperty("name").GetString());
        Assert.Equal("Brand", json.GetProperty("products")[0].GetProperty("brand").GetString());
        Assert.Equal("Linen", json.GetProperty("products")[0].GetProperty("material").GetString());
        Assert.Equal(image, json.GetProperty("products")[0].GetProperty("images")[0].GetString());
        Assert.NotNull(json.GetProperty("signature").GetProperty("data").GetString());
        Assert.Equal("Ghi chú đã xác nhận", json.GetProperty("note").GetString());
    }

    [Fact]
    public async Task ReceptionRejectsStaleCustomerAndUnknownCategoryWithoutSaving()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var staff = new Account { AccountType = AccountType.Internal, FullName = "Staff", PhoneNumber = "0900000001" };
        var customer = new Account { AccountType = AccountType.Customer, FullName = "Customer", PhoneNumber = "0900000003", Email = "new@example.test" };
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = staff.Id, Account = staff });
        db.CustomerProfiles.Add(new CustomerProfile { Id = customer.Id, Account = customer });
        var brand = new Brand { Name = "Brand", DefaultCommissionRate = 20 };
        var category = new Category { Name = "Category" };
        db.AddRange(brand, category);
        await db.SaveChangesAsync();
        var item = new CreateReceptionItemDto { Name = "Áo", BrandId = brand.Id, CategoryId = Guid.NewGuid(), Size = "M", ListedPrice = 100000, CommissionRate = 20 };
        var request = new CreateReceptionRequest { Signature = Signature(), TemplateVersion = Guid.Empty, ActualItemCount = 1, CustomerInfo = new ReceptionCustomerInfoDto { FullName = customer.FullName, PhoneNumber = customer.PhoneNumber, Email = "old@example.test" }, Items = [item] };
        var service = new ConsignmentService(db, null!);
        var unknown = await service.CreateReceptionAsync(staff.Id, request);
        Assert.Equal("CONSIGNMENT.INVALID_CATEGORY", unknown.Error!.Code);
        Assert.NotEqual(category.Id, item.CategoryId);
        item.CategoryId = category.Id;
        var stale = await service.CreateReceptionAsync(staff.Id, request);
        Assert.Equal("CUSTOMER.PROFILE_CHANGED", stale.Error!.Code);
        Assert.Empty(db.ConsignmentContracts);
        Assert.Empty(db.Products);
    }

    [Fact]
    public async Task ReceptionRejectsMissingOrInvalidSignatureBeforeSavingAnything()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var service = new ConsignmentService(db, null!);
        var request = new CreateReceptionRequest { Items = [new CreateReceptionItemDto { Name = "Product" }] };
        var missing = await service.CreateReceptionAsync(Guid.NewGuid(), request);
        Assert.False(missing.IsSuccess);
        Assert.Equal("CONSIGNMENT.SIGNATURE_REQUIRED", missing.Error!.Code);
        request.Signature = new ContractImageRequest { ContentType = "image/png", Data = "bad" };
        var invalid = await service.CreateReceptionAsync(Guid.NewGuid(), request);
        Assert.False(invalid.IsSuccess);
        Assert.Equal("CONSIGNMENT.INVALID_SIGNATURE", invalid.Error!.Code);
        Assert.Empty(db.ConsignmentContracts);
        Assert.Empty(db.Products);
    }
    [Fact]
    public async Task SignedNewCustomerIsSavedWithFullProfileAndContractSnapshot()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var staff = new Account { AccountType = AccountType.Internal, Status = AccountStatus.Active, FullName = "Staff", PhoneNumber = "0900000001" };
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = staff.Id, Account = staff, Status = EmployeeStatus.Working });
        var brand = new Brand { Name = "Brand", DefaultCommissionRate = 20 };
        var category = new Category { Name = "Category" };
        db.AddRange(brand, category);
        await db.SaveChangesAsync();
        var request = new CreateReceptionRequest
        {
            Signature = Signature(),
            TemplateVersion = Guid.Empty,
            ActualItemCount = 1,
            CustomerInfo = new ReceptionCustomerInfoDto { FullName = "Khách mới", PhoneNumber = "0901234567", Email = "guest@example.test", IdentityNumber = "12345", Address = "Địa chỉ", BankCode = "VCB", BankAccountNumber = "123", BankAccountHolderName = "Khách mới" },
            Items = [new CreateReceptionItemDto { Name = "Áo", BrandId = brand.Id, CategoryId = category.Id, Size = "M", ListedPrice = 50000, CommissionRate = 20 }]
        };
        var result = await new ConsignmentService(db, null!).CreateReceptionAsync(staff.Id, request);
        Assert.True(result.IsSuccess, result.Error?.Message);
        var profile = await db.CustomerProfiles.Include(p => p.Account).SingleAsync();
        Assert.Equal("12345", profile.Account.IdentityNumber);
        Assert.Equal("Địa chỉ", profile.Address);
        Assert.Equal("VCB", profile.BankCode);
        var contract = Assert.Single(db.ConsignmentContracts);
        Assert.Equal("12345", contract.CustomerIdentitySnapshot);
        Assert.Equal("Địa chỉ", contract.CustomerAddressSnapshot);
    }

    [Fact]
    public async Task TemplateChangeRejectsSignedSubmissionWithoutPersistingContract()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var staff = new Account { AccountType = AccountType.Internal, Status = AccountStatus.Active, FullName = "Staff", PhoneNumber = "0900000001" };
        var customer = new Account { AccountType = AccountType.Customer, Status = AccountStatus.Active, FullName = "Customer", PhoneNumber = "0900000003" };
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = staff.Id, Account = staff, Status = EmployeeStatus.Working });
        db.CustomerProfiles.Add(new CustomerProfile { Id = customer.Id, Account = customer });
        var brand = new Brand { Name = "Brand", DefaultCommissionRate = 20 };
        var category = new Category { Name = "Category" };
        db.AddRange(brand, category, new ContractTemplate { Id = 1, Version = Guid.NewGuid() });
        await db.SaveChangesAsync();
        var request = new CreateReceptionRequest { Signature = Signature(), TemplateVersion = Guid.Empty, ActualItemCount = 1, CustomerInfo = new ReceptionCustomerInfoDto { FullName = customer.FullName, PhoneNumber = customer.PhoneNumber }, Items = [new CreateReceptionItemDto { Name = "Áo", BrandId = brand.Id, CategoryId = category.Id, Size = "M", ListedPrice = 50000, CommissionRate = 20 }] };
        var result = await new ConsignmentService(db, null!).CreateReceptionAsync(staff.Id, request);
        Assert.False(result.IsSuccess);
        Assert.Equal("CONTRACT_TEMPLATE.CHANGED", result.Error!.Code);
        Assert.Empty(await db.ConsignmentContracts.AsNoTracking().ToListAsync());
    }
    [Fact]
    public async Task BuyoutReceptionPersistsCustomerSignature()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var staff = new Account { AccountType = AccountType.Internal, Status = AccountStatus.Active, FullName = "Staff", PhoneNumber = "0900000001" };
        var customer = new Account { AccountType = AccountType.Customer, Status = AccountStatus.Active, FullName = "Customer", PhoneNumber = "0900000003" };
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = staff.Id, Account = staff, Status = EmployeeStatus.Working });
        db.CustomerProfiles.Add(new CustomerProfile { Id = customer.Id, Account = customer });
        var brand = new Brand { Name = "Brand", DefaultCommissionRate = 20 };
        var category = new Category { Name = "Category" };
        db.AddRange(brand, category);
        await db.SaveChangesAsync();
        var request = new CreateReceptionRequest { IntakeType = "Buyout", Signature = Signature(), ActualItemCount = 1, CustomerInfo = new ReceptionCustomerInfoDto { FullName = customer.FullName, PhoneNumber = customer.PhoneNumber }, Items = [new CreateReceptionItemDto { Name = "Áo", BrandId = brand.Id, CategoryId = category.Id, Size = "M", ListedPrice = 50000, PurchasePrice = 25000 }] };
        var result = await new ConsignmentService(db, null!).CreateReceptionAsync(staff.Id, request);
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(Convert.FromBase64String(Signature().Data), Assert.Single(db.Buyouts).SignatureContent);
    }
    [Fact]
    public async Task EmptyProductCanBeDraftedButCannotBeFinalized()
    {
        var item = new CreateReceptionItemDto { Name = "", Size = "" };
        var propertyResults = new List<ValidationResult>();
        Assert.True(Validator.TryValidateProperty(item.Name, new ValidationContext(item) { MemberName = nameof(item.Name) }, propertyResults));
        Assert.True(Validator.TryValidateProperty(item.Size, new ValidationContext(item) { MemberName = nameof(item.Size) }, propertyResults));
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var service = new ConsignmentService(db, null!);
        var draft = await service.SaveDraftAsync(Guid.NewGuid(), new SaveReceptionDraftRequest { ActualItemCount = 7, Items = [item] });
        Assert.True(draft.IsSuccess);
        Assert.Equal("", Assert.Single(draft.Data!.Items).Name);
        Assert.Equal(7, draft.Data.ActualItemCount);
        var result = await service.CreateReceptionAsync(Guid.NewGuid(), new CreateReceptionRequest { Items = [item] });
        Assert.False(result.IsSuccess);
        Assert.Empty(db.ConsignmentContracts);
    }
}
