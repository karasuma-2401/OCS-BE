using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OCS.Api.Authorization;
using OCS.Api.Common;
using OCS.Api.Configuration;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Controllers;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Pos;
using OCS.Api.Services;

namespace OCS.Api.IntegrationTests;

public sealed class VietQrPaymentTests
{
    [Fact]
    public void PosPaymentsControllerRequiresInternalRolesAndScreenPosPermission()
    {
        var attributes = typeof(PosPaymentsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .ToArray();

        Assert.Contains(attributes, attr => attr.Roles == "Admin,Manager,Staff");
        Assert.Contains(attributes, attr => attr.Policy == PermissionCatalog.ScreenPos);
    }

    [Fact]
    public async Task AdapterGeneratesValidVietQrQuickLinkAndNeverMarksAsPaidOnCreation()
    {
        var options = Options.Create(new VietQrOptions
        {
            BankCode = "MB",
            AccountNumber = "090123456789",
            AccountHolderName = "Cửa Hàng Ký Gửi OCS",
            Template = "compact2",
            ExpiryMinutes = 10,
            ReferencePrefix = "OCS"
        });

        var adapter = new VietQrQuickLinkAdapter(options);
        var now = new DateTimeOffset(2026, 11, 8, 14, 0, 0, TimeSpan.Zero);

        var createResult = await adapter.CreateQrPaymentAsync(new QrPaymentAdapterRequest(
            TransactionId: Guid.NewGuid(),
            SaleInvoiceId: Guid.NewGuid(),
            InvoiceNumber: "HD-001",
            Amount: 650000m,
            PaymentReference: "ocs-261108-ab12cd",
            ExpiresAt: now.AddMinutes(10)));

        Assert.True(createResult.IsSuccess);
        var data = createResult.Data!;
        Assert.Equal("VietQrQuickLink", data.ProviderName);
        Assert.Equal("OCS261108AB12CD", data.PaymentReference);
        Assert.Equal("OCS261108AB12CD", data.TransferContent);
        Assert.Equal("MB", data.BankCode);
        Assert.Equal("090123456789", data.BankAccountNumber);
        Assert.Equal("CUA HANG KY GUI OCS", data.BankAccountHolderName);
        Assert.Contains("MB-090123456789-compact2.png", data.QrCodeUrl, StringComparison.Ordinal);
        Assert.Contains("amount=650000", data.QrCodeUrl, StringComparison.Ordinal);
        Assert.Contains("addInfo=OCS261108AB12CD", data.QrCodeUrl, StringComparison.Ordinal);
        Assert.Contains("vietqr://MB/090123456789", data.QrPayload, StringComparison.Ordinal);

        var statusResult = await adapter.QueryPaymentStatusAsync(data.PaymentReference);
        Assert.True(statusResult.IsSuccess);
        Assert.Equal(FinancialTransactionStatus.Pending, statusResult.Data!.Status);
    }

    [Fact]
    public async Task InitiateCreatesPendingTransactionAndKeepsInvoiceUnpaid()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 8, 14, 0, 0, TimeSpan.Zero));
        var (db, employeeId, invoice, service) = await CreateTestHarnessAsync(clock);
        await using (db)
        {
            var result = await service.InitiateAsync(employeeId, new InitiateVietQrPaymentRequest(invoice.Id));

            Assert.True(result.IsSuccess);
            var dto = result.Data!;
            Assert.Equal(invoice.Id, dto.SaleInvoiceId);
            Assert.Equal("HD-POS-001", dto.InvoiceNumber);
            Assert.Equal("650000.00", dto.Amount);
            Assert.Equal("Pending", dto.Status);
            Assert.Equal("Unpaid", dto.InvoicePaymentStatus);
            Assert.False(dto.IsExpired);
            Assert.Equal(600, dto.RemainingSeconds);
            Assert.StartsWith("OCS261108", dto.PaymentReference, StringComparison.Ordinal);
            Assert.Equal(dto.PaymentReference, dto.TransferContent);
            Assert.NotEmpty(dto.QrCodeUrl);
            Assert.NotEmpty(dto.QrPayload);

            var savedInvoice = await db.SaleInvoices.SingleAsync(x => x.Id == invoice.Id);
            Assert.Equal(InvoicePaymentStatus.Unpaid, savedInvoice.PaymentStatus);

            var savedTx = await db.FinancialTransactions.SingleAsync(x => x.Id == dto.TransactionId);
            Assert.Equal(FinancialTransactionStatus.Pending, savedTx.Status);
            Assert.Equal(PaymentMethod.VietQr, savedTx.PaymentMethod);
            Assert.Equal(FinancialTransactionType.Income, savedTx.Type);
            Assert.Equal(650000m, savedTx.Amount);
            Assert.Equal(dto.PaymentReference, savedTx.PaymentReference);
        }
    }

    [Fact]
    public async Task InitiateReusesActivePendingSessionAndRotatesOnForceNewOrExpiry()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 8, 14, 0, 0, TimeSpan.Zero));
        var (db, employeeId, invoice, service) = await CreateTestHarnessAsync(clock);
        await using (db)
        {
            var first = await service.InitiateAsync(employeeId, new InitiateVietQrPaymentRequest(invoice.Id));
            Assert.True(first.IsSuccess);

            clock.Advance(TimeSpan.FromMinutes(3));
            var reused = await service.InitiateAsync(employeeId, new InitiateVietQrPaymentRequest(invoice.Id, ForceNew: false));
            Assert.True(reused.IsSuccess);
            Assert.Equal(first.Data!.TransactionId, reused.Data!.TransactionId);
            Assert.Equal(first.Data.PaymentReference, reused.Data.PaymentReference);
            Assert.Equal(420, reused.Data.RemainingSeconds);

            var forced = await service.InitiateAsync(employeeId, new InitiateVietQrPaymentRequest(invoice.Id, ForceNew: true));
            Assert.True(forced.IsSuccess);
            Assert.NotEqual(first.Data.TransactionId, forced.Data!.TransactionId);
            Assert.NotEqual(first.Data.PaymentReference, forced.Data.PaymentReference);

            var oldTx = await db.FinancialTransactions.SingleAsync(x => x.Id == first.Data.TransactionId);
            Assert.Equal(FinancialTransactionStatus.Failed, oldTx.Status);

            clock.Advance(TimeSpan.FromMinutes(11));
            var afterExpiry = await service.InitiateAsync(employeeId, new InitiateVietQrPaymentRequest(invoice.Id));
            Assert.True(afterExpiry.IsSuccess);
            Assert.NotEqual(forced.Data.TransactionId, afterExpiry.Data!.TransactionId);

            var expiredForcedTx = await db.FinancialTransactions.SingleAsync(x => x.Id == forced.Data.TransactionId);
            Assert.Equal(FinancialTransactionStatus.Failed, expiredForcedTx.Status);
        }
    }

    [Fact]
    public async Task InitiateRejectsMissingOrAlreadyPaidInvoice()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 8, 14, 0, 0, TimeSpan.Zero));
        var (db, employeeId, invoice, service) = await CreateTestHarnessAsync(clock);
        await using (db)
        {
            var missing = await service.InitiateAsync(employeeId, new InitiateVietQrPaymentRequest(Guid.NewGuid()));
            Assert.False(missing.IsSuccess);
            Assert.Equal("POS.INVOICE_NOT_FOUND", missing.Error!.Code);

            invoice.PaymentStatus = InvoicePaymentStatus.Paid;
            await db.SaveChangesAsync();

            var alreadyPaid = await service.InitiateAsync(employeeId, new InitiateVietQrPaymentRequest(invoice.Id));
            Assert.False(alreadyPaid.IsSuccess);
            Assert.Equal("POS.INVOICE_ALREADY_PAID", alreadyPaid.Error!.Code);
        }
    }

    [Fact]
    public async Task GetStatusMarksExpiredPendingTransactionAsFailed()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 8, 14, 0, 0, TimeSpan.Zero));
        var (db, employeeId, invoice, service) = await CreateTestHarnessAsync(clock);
        await using (db)
        {
            var created = await service.InitiateAsync(employeeId, new InitiateVietQrPaymentRequest(invoice.Id));
            Assert.True(created.IsSuccess);

            clock.Advance(TimeSpan.FromMinutes(5));
            var midCheck = await service.GetStatusAsync(created.Data!.TransactionId);
            Assert.True(midCheck.IsSuccess);
            Assert.Equal("Pending", midCheck.Data!.Status);
            Assert.False(midCheck.Data.IsExpired);
            Assert.Equal(300, midCheck.Data.RemainingSeconds);

            clock.Advance(TimeSpan.FromMinutes(6));
            var expiredCheck = await service.GetStatusAsync(created.Data.TransactionId);
            Assert.True(expiredCheck.IsSuccess);
            Assert.Equal("Failed", expiredCheck.Data!.Status);
            Assert.Equal("Unpaid", expiredCheck.Data.InvoicePaymentStatus);
            Assert.True(expiredCheck.Data.IsExpired);
            Assert.Equal(0, expiredCheck.Data.RemainingSeconds);
        }
    }

    [Fact]
    public async Task WebhookReconcilesPaymentMarksInvoicePaidAndProductsSoldAndIsIdempotent()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 8, 14, 0, 0, TimeSpan.Zero));
        var (db, employeeId, invoice, service) = await CreateTestHarnessAsync(clock);
        await using (db)
        {
            var initiated = await service.InitiateAsync(employeeId, new InitiateVietQrPaymentRequest(invoice.Id));
            Assert.True(initiated.IsSuccess);
            var reference = initiated.Data!.PaymentReference;

            var webhookPayload = new VietQrWebhookPayloadDto(
                Gateway: "MBBank",
                TransactionDate: "2026-11-08 21:02:15",
                AccountNumber: "0900000001",
                Content: $"MBVCB.88991122.{reference}.CT tu NGUYEN VAN A",
                TransferType: "in",
                TransferAmount: 650000m,
                ReferenceCode: "FT26312998877");

            var firstProcess = await service.ProcessWebhookAsync(webhookPayload);
            Assert.True(firstProcess.IsSuccess);
            Assert.True(firstProcess.Data!.Processed);
            Assert.False(firstProcess.Data.AlreadyPaid);
            Assert.Equal(reference, firstProcess.Data.PaymentReference);
            Assert.Equal("Paid", firstProcess.Data.InvoicePaymentStatus);

            var updatedTx = await db.FinancialTransactions.SingleAsync(x => x.Id == initiated.Data.TransactionId);
            Assert.Equal(FinancialTransactionStatus.Success, updatedTx.Status);
            Assert.Equal("FT26312998877", updatedTx.BankTransactionReference);

            var updatedInvoice = await db.SaleInvoices
                .Include(x => x.Lines)
                    .ThenInclude(l => l.Product)
                .SingleAsync(x => x.Id == invoice.Id);
            Assert.Equal(InvoicePaymentStatus.Paid, updatedInvoice.PaymentStatus);
            Assert.All(updatedInvoice.Lines, line => Assert.Equal(ProductStatus.Sold, line.Product!.Status));

            var histories = await db.ProductStatusHistories.ToListAsync();
            Assert.Single(histories);
            Assert.Equal(ProductStatus.OnSale, histories[0].PreviousStatus);
            Assert.Equal(ProductStatus.Sold, histories[0].NewStatus);

            // Duplicate webhook should be idempotent
            var secondProcess = await service.ProcessWebhookAsync(webhookPayload);
            Assert.True(secondProcess.IsSuccess);
            Assert.True(secondProcess.Data!.Processed);
            Assert.True(secondProcess.Data.AlreadyPaid);
        }
    }

    [Fact]
    public async Task WebhookRejectsUnderpaidAmountAndKeepsInvoiceUnpaid()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 8, 14, 0, 0, TimeSpan.Zero));
        var (db, employeeId, invoice, service) = await CreateTestHarnessAsync(clock);
        await using (db)
        {
            var initiated = await service.InitiateAsync(employeeId, new InitiateVietQrPaymentRequest(invoice.Id));
            Assert.True(initiated.IsSuccess);

            var underpaidPayload = new VietQrWebhookPayloadDto(
                Gateway: "MBBank",
                Code: initiated.Data!.PaymentReference,
                Content: initiated.Data.PaymentReference,
                TransferType: "in",
                TransferAmount: 500000m,
                ReferenceCode: "FT-UNDERPAID");

            var result = await service.ProcessWebhookAsync(underpaidPayload);
            Assert.False(result.IsSuccess);
            Assert.Equal("POS.UNDERPAID_AMOUNT", result.Error!.Code);

            var unchangedInvoice = await db.SaleInvoices.SingleAsync(x => x.Id == invoice.Id);
            Assert.Equal(InvoicePaymentStatus.Unpaid, unchangedInvoice.PaymentStatus);
        }
    }

    [Fact]
    public async Task SimulateWebhookCompletesPaymentInDevelopmentEnvironment()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 8, 14, 0, 0, TimeSpan.Zero));
        var (db, employeeId, invoice, service) = await CreateTestHarnessAsync(clock);
        await using (db)
        {
            var initiated = await service.InitiateAsync(employeeId, new InitiateVietQrPaymentRequest(invoice.Id));
            Assert.True(initiated.IsSuccess);

            var simulated = await service.SimulateWebhookAsync(initiated.Data!.TransactionId);
            Assert.True(simulated.IsSuccess);
            Assert.Equal("Success", simulated.Data!.Status);
            Assert.Equal("Paid", simulated.Data.InvoicePaymentStatus);
            Assert.False(simulated.Data.IsExpired);
        }
    }

    private static async Task<(AppDbContext Db, Guid EmployeeId, SaleInvoice Invoice, VietQrPaymentService Service)> CreateTestHarnessAsync(
        TimeProvider timeProvider)
    {
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(dbOptions);
        var employeeId = Guid.NewGuid();
        var account = new Account
        {
            Id = employeeId,
            FullName = "Cashier Staff",
            PhoneNumber = "0900000999",
            AccountType = AccountType.Internal,
            Status = AccountStatus.Active
        };
        var employee = new EmployeeProfile
        {
            Id = employeeId,
            Account = account
        };
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Barcode = "SP-POS-TEST-001",
            Name = "Áo khoác dạ Tweed",
            Size = "M",
            ConditionPercentage = 95,
            ListedPrice = 650000m,
            Status = ProductStatus.OnSale
        };
        var invoice = new SaleInvoice
        {
            Id = Guid.NewGuid(),
            InvoiceNumber = "HD-POS-001",
            SoldByEmployeeId = employeeId,
            TotalAmount = 650000m,
            PaymentStatus = InvoicePaymentStatus.Unpaid,
            Lines =
            [
                new SaleInvoiceLine
                {
                    ProductId = product.Id,
                    Product = product,
                    SalePrice = 650000m
                }
            ]
        };

        db.Accounts.Add(account);
        db.EmployeeProfiles.Add(employee);
        db.Products.Add(product);
        db.SaleInvoices.Add(invoice);
        await db.SaveChangesAsync();

        var vietQrOptions = Options.Create(new VietQrOptions
        {
            BankCode = "MB",
            AccountNumber = "0900000001",
            AccountHolderName = "CUA HANG KY GUI OCS",
            Template = "compact2",
            ExpiryMinutes = 10,
            ReferencePrefix = "OCS",
            EnableWebhookSimulation = true
        });

        var adapter = new VietQrQuickLinkAdapter(vietQrOptions);
        var env = new FakeHostEnvironment { EnvironmentName = Environments.Development };
        var service = new VietQrPaymentService(db, adapter, vietQrOptions, timeProvider, env);

        return (db, employeeId, invoice, service);
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "OCS.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class FakeClock(DateTimeOffset initialUtcNow) : TimeProvider
    {
        private DateTimeOffset currentUtcNow = initialUtcNow;

        public override DateTimeOffset GetUtcNow() => currentUtcNow;

        public void Advance(TimeSpan delta) => currentUtcNow = currentUtcNow.Add(delta);
    }
}
