using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using OCS.Api.Authorization;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Controllers;
using OCS.Api.Entities;
using OCS.Api.Models.Contracts;
using OCS.Api.Services;

namespace OCS.Api.IntegrationTests;

public sealed class ContractServiceTests
{
    [Fact]
    public void ProductTableSupportsLongRowsAcrossPagesAndCustomTemplateAppendix()
    {
        var contract = new ConsignmentContract { ContractNumber = "HD-BANG-001", SignedAt = DateTimeOffset.UtcNow, EndsAt = DateTimeOffset.UtcNow.AddMonths(1) };
        var brand = new Brand { Name = "Thương hiệu", DefaultCommissionRate = 20 };
        var products = Enumerable.Range(1, 12).Select(index =>
        {
            var product = new Product
            {
                Name = $"Sản phẩm {index}",
                Barcode = $"OCS-{index}",
                Size = "M",
                ConditionPercentage = 90,
                Description = string.Join(" ", Enumerable.Repeat("Chi tiết tình trạng sản phẩm tiếng Việt", 35))
            };
            product.AssignConsignmentTerms(contract.Id, 1234567.5m, brand, 17.5m);
            return (product, brand.Name, "Danh mục");
        }).ToList();
        var bytes = ContractPdfRenderer.Render(contract, "Nhân viên", "Nhân viên", "0900000000", products, "Điều khoản", null);
        using var document = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(bytes), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        Assert.True(document.PageCount >= 3);
        var template = new ContractTemplate { PdfBackground = ContractPdfRenderer.CreateSampleBackground() };
        var custom = ContractPdfRenderer.Render(contract, "Nhân viên", "Nhân viên", "0900000000", products, "Điều khoản", null, template: template);
        using var customDocument = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(custom), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        Assert.Equal(document.PageCount + 1, customDocument.PageCount);
    }
    [Fact]
    public async Task PreviewDoesNotSaveAnythingAndTheSameRequestCanBeCreated()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);
        var employee = new EmployeeProfile { Id = Guid.NewGuid() };
        var brand = new Brand { Name = "Nhãn", DefaultCommissionRate = 20 };
        var category = new Category { Name = "Áo" };
        db.AddRange(employee, brand, category);
        await db.SaveChangesAsync();
        var now = DateTimeOffset.UtcNow;
        var request = new CreateContractRequest
        {
            RequestId = Guid.NewGuid(),
            SignedAt = now,
            EndsAt = now.AddDays(30),
            ExpiryHandling = "Return",
            SaveCustomer = true,
            Guest = new ContractGuestRequest { FullName = "Khách xem trước", PhoneNumber = "0901234567" },
            Products = [new ContractProductRequest { Name = "Áo", Size = "M", BrandId = brand.Id, CategoryId = category.Id, AgreedCommissionRate = 15, ListedPrice = 120000 }]
        };
        var service = new ContractService(db);
        var preview = await service.PreviewAsync(request, employee.Id, default);
        Assert.True(preview.IsSuccess, preview.Error?.Message);
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(preview.Data!, 0, 5));
        using (var document = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(preview.Data!), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
            Assert.True(document.PageCount > 0);
        Assert.DoesNotContain(db.ChangeTracker.Entries(), x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
        Assert.Empty(db.CustomerProfiles);
        Assert.Empty(db.ConsignmentContracts);
        Assert.Empty(db.ContractDocuments);
        Assert.Empty(db.Products);
        var created = await service.CreateAsync(request, employee.Id, default);
        Assert.True(created.IsSuccess, created.Error?.Message);
        Assert.Single(db.CustomerProfiles);
        Assert.Single(db.ConsignmentContracts);
        Assert.Single(db.ContractDocuments);
        Assert.StartsWith("OCS-", (await db.Products.SingleAsync()).Barcode);
    }

    [Fact]
    public async Task PreviewUsesTheSameValidationAsCreation()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);
        var employee = new EmployeeProfile { Id = Guid.NewGuid() };
        db.EmployeeProfiles.Add(employee);
        await db.SaveChangesAsync();
        var now = DateTimeOffset.UtcNow;
        var request = new CreateContractRequest { RequestId = Guid.NewGuid(), SignedAt = now, EndsAt = now, ExpiryHandling = "Return", Guest = new ContractGuestRequest { FullName = "Khách", PhoneNumber = "0901234567" } };
        var result = await new ContractService(db).PreviewAsync(request, employee.Id, default);
        Assert.Equal("CONTRACT.INVALID_DATA", result.Error!.Code);
        Assert.Empty(db.ConsignmentContracts);
    }

    [Fact]
    public async Task ChangingEmailRevokesSessionsAndRequiresReactivation()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);
        var account = new Account { AccountType = AccountType.Customer, Status = AccountStatus.Active, FullName = "Khách", PhoneNumber = "0901234567", Email = "old@example.com", EmailVerifiedAt = DateTimeOffset.UtcNow };
        var profile = new CustomerProfile { Id = account.Id, Account = account };
        var session = new AuthSession { AccountId = account.Id, ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) };
        db.AddRange(profile, session);
        await db.SaveChangesAsync();
        var result = await new ContractService(db).UpdateCustomerAsync(account.Id, new UpdateContractCustomerRequest { Version = account.Version, ProfileVersion = profile.Version, Email = "new@example.com" }, default);
        Assert.True(result.IsSuccess);
        Assert.Equal(AccountStatus.PendingActivation, account.Status);
        Assert.Null(account.EmailVerifiedAt);
        Assert.NotNull(session.RevokedAt);
    }

    [Fact]
    public async Task PendingPayoutPreventsUpdatingBankAccount()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);
        var account = new Account { AccountType = AccountType.Customer, Status = AccountStatus.Active, FullName = "Khách", PhoneNumber = "0901234567" };
        var profile = new CustomerProfile { Id = account.Id, Account = account, BankCode = "VCB", BankAccountNumber = "123", BankAccountHolderName = "Khách" };
        var contract = new ConsignmentContract { CustomerId = account.Id, SignedAt = DateTimeOffset.UtcNow, EndsAt = DateTimeOffset.UtcNow.AddDays(1) };
        var payout = new Payout { ConsignmentContract = contract, Status = PayoutStatus.Pending };
        db.AddRange(profile, payout);
        await db.SaveChangesAsync();
        var result = await new ContractService(db).UpdateCustomerAsync(account.Id, new UpdateContractCustomerRequest { Version = account.Version, ProfileVersion = profile.Version, BankCode = "VCB", BankAccountNumber = "456", BankAccountHolderName = "Khách" }, default);
        Assert.Equal("CUSTOMER.BANK_ACCOUNT_LOCKED_FOR_PAYOUT", result.Error!.Code);
        Assert.Equal("123", profile.BankAccountNumber);
    }

    [Fact]
    public async Task CustomerUpdatePreservesNameAndPhoneAndValidatesBankInformation()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);
        var account = new Account { AccountType = AccountType.Customer, Status = AccountStatus.Active, FullName = "Tên gốc", PhoneNumber = "0901234567" };
        var profile = new CustomerProfile { Id = account.Id, Account = account };
        db.CustomerProfiles.Add(profile);
        await db.SaveChangesAsync();
        var service = new ContractService(db);
        var badBank = new UpdateContractCustomerRequest { Version = account.Version, BankCode = "VCB" };
        Assert.Equal("CUSTOMER.INVALID_BANK", (await service.UpdateCustomerAsync(account.Id, badBank, default)).Error!.Code);
        var updated = await service.UpdateCustomerAsync(account.Id, new UpdateContractCustomerRequest { Version = account.Version, Address = "Địa chỉ mới", IdentityNumber = "1234", BankCode = "vcb", BankAccountNumber = "5678", BankAccountHolderName = "Tên chủ" }, default);
        Assert.True(updated.IsSuccess);
        Assert.Equal("Tên gốc", updated.Data!.FullName);
        Assert.Equal("0901234567", updated.Data.PhoneNumber);
        Assert.Equal("Địa chỉ mới", updated.Data.Address);
        Assert.Equal("VCB", updated.Data.BankCode);
        Assert.Equal("CUSTOMER.CONCURRENT_UPDATE", (await service.UpdateCustomerAsync(account.Id, new UpdateContractCustomerRequest { Version = account.Version + 1 }, default)).Error!.Code);
    }

    [Fact]
    public async Task WalkInCustomerCanBeFoundAndContractCreatedWithoutAppointment()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);
        var employee = new EmployeeProfile { Id = Guid.NewGuid() };
        var account = new Account { AccountType = AccountType.Customer, Status = AccountStatus.Active, FullName = "Khách trực tiếp", PhoneNumber = "0901234567" };
        var customer = new CustomerProfile { Id = account.Id, Account = account, Status = CustomerStatus.Active };
        db.AddRange(employee, customer);
        await db.SaveChangesAsync();
        var service = new ContractService(db);
        var lookup = await service.FindCustomerByPhoneAsync("0901234567", default);
        Assert.True(lookup.IsSuccess);
        Assert.Equal(customer.Id, lookup.Data!.Id);
        Assert.Equal("CUSTOMER.NOT_FOUND", (await service.FindCustomerByPhoneAsync("0900000000", default)).Error!.Code);
        Assert.Equal("CUSTOMER.INVALID_PHONE", (await service.FindCustomerByPhoneAsync("123", default)).Error!.Code);
        var now = DateTimeOffset.UtcNow;
        var request = new CreateContractRequest { CustomerId = customer.Id, RequestId = Guid.NewGuid(), SignedAt = now, EndsAt = now.AddDays(30), ExpiryHandling = "Donate" };
        var result = await service.CreateAsync(request, employee.Id, default);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Data!.AppointmentId);
        Assert.Equal(customer.Id, result.Data.CustomerId);
        Assert.Equal("Donate", result.Data.ExpiryHandling);
        Assert.Equal("CONTRACT.ALREADY_EXISTS", (await service.CreateAsync(request, employee.Id, default)).Error!.Code);
        Assert.Equal("CONTRACT.INVALID_CUSTOMER", (await service.CreateAsync(request with { RequestId = null }, employee.Id, default)).Error!.Code);
        Assert.Equal("CONTRACT.INVALID_CUSTOMER", (await service.CreateAsync(request with { AppointmentId = Guid.NewGuid() }, employee.Id, default)).Error!.Code);
    }

    [Fact]
    public async Task CreatesContractFromCheckedInAppointmentAndRejectsDuplicate()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);
        var employee = new EmployeeProfile { Id = Guid.NewGuid() };
        var customerAccount = new Account { AccountType = AccountType.Customer, Status = AccountStatus.Active, FullName = "Khách", PhoneNumber = "0901234568" };
        var customerProfile = new CustomerProfile { Id = customerAccount.Id, Account = customerAccount };
        var customerId = customerAccount.Id;
        var appointment = new Appointment { CustomerId = customerId, Status = AppointmentStatus.CheckedIn };
        db.AddRange(employee, appointment, customerProfile);
        await db.SaveChangesAsync();
        var service = new ContractService(db);
        var now = DateTimeOffset.UtcNow;
        var request = new CreateContractRequest { AppointmentId = appointment.Id, SignedAt = now, EndsAt = now.AddDays(30), ExpiryHandling = "Return" };

        var created = await service.CreateAsync(request, employee.Id, default);
        Assert.True(created.IsSuccess);
        Assert.Equal(customerId, created.Data!.CustomerId);
        Assert.Equal(employee.Id, created.Data.CreatedByEmployeeId);
        Assert.Equal(appointment.Id, created.Data.AppointmentId);
        Assert.Equal("Active", created.Data.Status);
        Assert.Equal("Return", created.Data.ExpiryHandling);
        Assert.True((await service.GetAsync(created.Data.Id, default)).IsSuccess);
        Assert.Equal("CONTRACT.ALREADY_EXISTS", (await service.CreateAsync(request, employee.Id, default)).Error!.Code);
        Assert.Equal(AppointmentStatus.CheckedIn, (await db.Appointments.SingleAsync()).Status);
        Assert.Equal(1, await db.ConsignmentContracts.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GuestAndProductsAreStoredWithOptionalCustomerProfile(bool saveCustomer)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);
        var employee = new EmployeeProfile { Id = Guid.NewGuid() };
        var brand = new Brand { Name = "Nhãn", DefaultCommissionRate = 20 };
        var category = new Category { Name = "Áo" };
        db.AddRange(employee, brand, category);
        await db.SaveChangesAsync();
        var now = DateTimeOffset.UtcNow;
        var request = new CreateContractRequest
        {
            RequestId = Guid.NewGuid(),
            SignedAt = now,
            EndsAt = now.AddDays(30),
            ExpiryHandling = "Return",
            SaveCustomer = saveCustomer,
            Terms = "Nội dung do nhân viên nhập",
            Guest = new ContractGuestRequest { FullName = "Khách vãng lai", PhoneNumber = "0901234567", Address = "Địa chỉ", BankCode = "VCB", BankAccountNumber = "123456", BankAccountHolderName = "Khách" },
            Products = [new ContractProductRequest { Name = "Áo", Size = "M", ConditionPercentage = 90, ListedPrice = 100000, AgreedCommissionRate = 15.25m, BrandId = brand.Id, CategoryId = category.Id,
                Images = [new ContractImageRequest { ContentType = "image/png", Data = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=" }, new ContractImageRequest { ContentType = "image/png", Data = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=" }] }]
        };
        var result = await new ContractService(db).CreateAsync(request, employee.Id, default);
        Assert.True(result.IsSuccess, result.Error?.Message);
        var contract = await db.ConsignmentContracts.SingleAsync();
        Assert.Equal("Khách vãng lai", contract.CustomerNameSnapshot);
        Assert.Equal("123456", contract.BankAccountNumberSnapshot);
        Assert.Equal(saveCustomer, contract.CustomerId.HasValue);
        Assert.Equal(saveCustomer ? 1 : 0, await db.CustomerProfiles.CountAsync());
        var product = await db.Products.SingleAsync();
        Assert.StartsWith("OCS-", product.Barcode);
        Assert.Equal(contract.Id, product.ConsignmentContractId);
        Assert.Equal(90, product.ConditionPercentage);
        Assert.Equal(15.25m, product.AgreedCommissionRate);
        var document = await db.ContractDocuments.SingleAsync();
        Assert.Equal(contract.Id, document.ContractId);
        Assert.Equal(request.Terms, document.Terms);
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(document.Content, 0, 5));
        Assert.Equal(document.Content, await new ContractService(db).GetPdfAsync(contract.Id, default));
        Assert.Equal(ProductStatus.PendingProcessing, product.Status);
        var images = await db.ProductImages.OrderBy(x => x.SortOrder).ToListAsync();
        Assert.Equal(2, images.Count);
        var image = images[0];
        Assert.Equal(ProductImageType.Cover, image.ImageType);
        Assert.Equal(ProductImageType.Detail, images[1].ImageType);
        Assert.NotEmpty(image.Content!);
        var loaded = await new ContractService(db).GetImageAsync(image.Id, default);
        Assert.NotNull(loaded);
        Assert.False(loaded.IsPublic);
        Assert.Equal("image/png", loaded.ContentType);
        product.Status = ProductStatus.OnSale;
        await db.SaveChangesAsync();
        Assert.True((await new ContractService(db).GetImageAsync(image.Id, default))!.IsPublic);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("-1")]
    [InlineData("100.01")]
    [InlineData("12.345")]
    public async Task RejectsMissingOrInvalidAgreedCommission(string? value)
    {
        decimal? commission = value is null ? null : decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);
        var employee = new EmployeeProfile { Id = Guid.NewGuid() };
        var brand = new Brand { Name = "Nhãn", DefaultCommissionRate = 20 };
        var category = new Category { Name = "Áo" };
        db.AddRange(employee, brand, category);
        await db.SaveChangesAsync();
        var now = DateTimeOffset.UtcNow;
        var request = new CreateContractRequest
        {
            RequestId = Guid.NewGuid(),
            SignedAt = now,
            EndsAt = now.AddDays(1),
            ExpiryHandling = "Return",
            Guest = new ContractGuestRequest { FullName = "Khách", PhoneNumber = "0901234567" },
            Products = [new ContractProductRequest { Name = "Áo", Size = "M", BrandId = brand.Id, CategoryId = category.Id, AgreedCommissionRate = commission }]
        };
        var result = await new ContractService(db).CreateAsync(request, employee.Id, default);
        Assert.Equal("CONTRACT.INVALID_PRODUCTS", result.Error!.Code);
        Assert.Empty(db.ConsignmentContracts);
        Assert.Empty(db.ContractDocuments);
    }

    [Fact]
    public async Task ExistingCustomerChangesStayOnContractAndSignatureIsStoredInPdf()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);
        var employee = new EmployeeProfile { Id = Guid.NewGuid(), JobTitle = "Nhân viên" };
        var account = new Account { AccountType = AccountType.Customer, Status = AccountStatus.Active, FullName = "Khách gốc", PhoneNumber = "0901234567" };
        var profile = new CustomerProfile { Id = account.Id, Account = account, Address = "Địa chỉ gốc" };
        db.AddRange(employee, profile);
        await db.SaveChangesAsync();
        var now = DateTimeOffset.UtcNow;
        var signature = new ContractImageRequest { ContentType = "image/png", Data = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=" };
        var request = new CreateContractRequest
        {
            CustomerId = profile.Id,
            RequestId = Guid.NewGuid(),
            SignedAt = now,
            EndsAt = now.AddDays(30),
            ExpiryHandling = "Return",
            CustomerDetails = new ContractCustomerDetailsRequest { Address = "Địa chỉ chỉ trên hợp đồng", BankCode = "VCB", BankAccountNumber = "123456", BankAccountHolderName = "Khách" },
            Signature = signature,
            Terms = string.Join('\n', Enumerable.Repeat("Điều khoản dự thảo có tiếng Việt.", 90))
        };
        var result = await new ContractService(db).CreateAsync(request, employee.Id, default);
        Assert.True(result.IsSuccess, result.Error?.Message);
        var saved = await db.ConsignmentContracts.SingleAsync();
        Assert.Equal("Khách gốc", saved.CustomerNameSnapshot);
        Assert.Equal("0901234567", saved.CustomerPhoneSnapshot);
        Assert.Equal("Địa chỉ chỉ trên hợp đồng", saved.CustomerAddressSnapshot);
        Assert.Equal("Địa chỉ gốc", profile.Address);
        var document = await db.ContractDocuments.SingleAsync();
        Assert.Equal(Convert.FromBase64String(signature.Data), document.SignatureContent);
        using var input = new MemoryStream(document.Content);
        using var pdf = PdfSharp.Pdf.IO.PdfReader.Open(input, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        Assert.True(pdf.PageCount > 1);
    }

    [Fact]
    public async Task InvalidSignatureDoesNotCreateContract()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);
        var employee = new EmployeeProfile { Id = Guid.NewGuid() };
        db.EmployeeProfiles.Add(employee);
        await db.SaveChangesAsync();
        var now = DateTimeOffset.UtcNow;
        var request = new CreateContractRequest
        {
            RequestId = Guid.NewGuid(),
            SignedAt = now,
            EndsAt = now.AddDays(1),
            ExpiryHandling = "Return",
            Guest = new ContractGuestRequest { FullName = "Khách", PhoneNumber = "0901234567" },
            Signature = new ContractImageRequest { ContentType = "image/png", Data = "not-base64" }
        };
        var result = await new ContractService(db).CreateAsync(request, employee.Id, default);
        Assert.Equal("CONTRACT.INVALID_SIGNATURE", result.Error!.Code);
        Assert.Empty(db.ConsignmentContracts);
        Assert.Empty(db.ContractDocuments);
    }

    [Fact]
    public async Task RejectsInvalidFieldsAndAppointmentsNotYetReceived()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);
        var employee = new EmployeeProfile { Id = Guid.NewGuid() };
        var appointment = new Appointment { CustomerId = Guid.NewGuid(), Status = AppointmentStatus.Scheduled };
        db.AddRange(employee, appointment);
        await db.SaveChangesAsync();
        var service = new ContractService(db);
        var now = DateTimeOffset.UtcNow;
        var valid = new CreateContractRequest { AppointmentId = appointment.Id, SignedAt = now, EndsAt = now.AddDays(1), ExpiryHandling = "Donate" };

        Assert.Equal("CONTRACT.INVALID_DATA", (await service.CreateAsync(valid with { EndsAt = now }, employee.Id, default)).Error!.Code);
        Assert.Equal("CONTRACT.INVALID_DATA", (await service.CreateAsync(valid with { ExpiryHandling = "Other" }, employee.Id, default)).Error!.Code);
        Assert.Equal("CONTRACT.EMPLOYEE_REQUIRED", (await service.CreateAsync(valid, Guid.NewGuid(), default)).Error!.Code);
        Assert.Equal("CONTRACT.INVALID_APPOINTMENT", (await service.CreateAsync(valid, employee.Id, default)).Error!.Code);
        Assert.Equal("APPOINTMENT.NOT_FOUND", (await service.CreateAsync(valid with { AppointmentId = Guid.NewGuid() }, employee.Id, default)).Error!.Code);
        Assert.Empty(db.ConsignmentContracts);
    }

    [Fact]
    public void ContractControllerRequiresInternalRoleAndReceptionPermission()
    {
        var attributes = typeof(ContractsController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().ToArray();
        Assert.Contains(attributes, x => x.Policy == PermissionCatalog.AppointmentsStaffReception);
        Assert.Contains(attributes, x => x.Roles == "Admin,Manager,Staff");
        var update = typeof(ContractsController).GetMethod(nameof(ContractsController.UpdateCustomer))!;
        Assert.Contains(update.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>(), x => x.Policy == PermissionCatalog.CustomersUpdate);
    }
}
