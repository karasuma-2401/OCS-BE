using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
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
        var invoice = new SaleInvoice
        {
            Id = Guid.NewGuid(),
            InvoiceNumber = "HD-POS-001",
            SoldByEmployeeId = employeeId,
            TotalAmount = 650000m,
            PaymentStatus = InvoicePaymentStatus.Unpaid
        };

        db.Accounts.Add(account);
        db.EmployeeProfiles.Add(employee);
        db.SaleInvoices.Add(invoice);
        await db.SaveChangesAsync();

        var vietQrOptions = Options.Create(new VietQrOptions
        {
            BankCode = "MB",
            AccountNumber = "0900000001",
            AccountHolderName = "CUA HANG KY GUI OCS",
            Template = "compact2",
            ExpiryMinutes = 10,
            ReferencePrefix = "OCS"
        });

        var adapter = new VietQrQuickLinkAdapter(vietQrOptions);
        var service = new VietQrPaymentService(db, adapter, vietQrOptions, timeProvider);

        return (db, employeeId, invoice, service);
    }

    private sealed class FakeClock(DateTimeOffset initialUtcNow) : TimeProvider
    {
        private DateTimeOffset currentUtcNow = initialUtcNow;

        public override DateTimeOffset GetUtcNow() => currentUtcNow;

        public void Advance(TimeSpan delta) => currentUtcNow = currentUtcNow.Add(delta);
    }
}
