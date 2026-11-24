using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OCS.Api.Configuration;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Models.Pos;
using OCS.Api.Services;

namespace OCS.Api.IntegrationTests;

public sealed partial class EarlyWithdrawalTests
{
    private static VietQrPaymentService Qr(AppDbContext db)
    {
        var options = Options.Create(new VietQrOptions { BankCode = "MB", AccountNumber = "0900000001",
            AccountHolderName = "OCS TEST", ReferencePrefix = "OCS", ExpiryMinutes = 10 });
        return new(db, new VietQrQuickLinkAdapter(options), options, TimeProvider.System, new QrEnvironment());
    }
    private sealed class QrEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    private static VietQrWebhookPayloadDto Incoming(WithdrawalVietQrSessionDto session, decimal amount) =>
        new(Content: session.PaymentReference, TransferType: "in", TransferAmount: amount, ReferenceCode: "BANK-" + session.TransactionId);

    [Fact]
    public async Task WithdrawalQrReusesSessionAndWebhookCollectsExactlyOnceWithoutSellingProduct()
    {
        var (db, service, customer, employee, product) = Setup();
        await using var cleanup = db;
        var row = (await service.CreateAsync(customer, new([product.Id], "Withdraw"), default)).Data!;
        var qr = Qr(db);
        Assert.False((await qr.InitiateWithdrawalAsync(row.Id, employee, false)).IsSuccess);
        await service.ApproveAsync(row.Id, employee, default);
        Assert.False((await qr.InitiateWithdrawalAsync(row.Id, customer, false)).IsSuccess);
        var session = (await qr.InitiateWithdrawalAsync(row.Id, employee, false)).Data!;
        Assert.Equal(session.TransactionId, (await qr.InitiateWithdrawalAsync(row.Id, employee, false)).Data!.TransactionId);
        Assert.Empty(await db.SaleInvoices.ToListAsync());
        Assert.Null((await db.FinancialTransactions.SingleAsync()).SaleInvoiceId);
        Assert.False((await qr.GetWithdrawalStatusAsync(Guid.NewGuid(), session.TransactionId, employee)).IsSuccess);
        Assert.False((await qr.SimulateWebhookAsync(session.TransactionId)).IsSuccess);
        Assert.False((await service.PayAsync(row.Id, employee, new(row.TotalPenaltyAmount, "Cash", null), default)).IsSuccess);
        Assert.False((await service.RejectAsync(row.Id, employee, new("Reject"), default)).IsSuccess);
        Assert.False((await qr.ProcessWebhookAsync(Incoming(session, row.TotalPenaltyAmount - 1))).IsSuccess);
        Assert.False((await qr.ProcessWebhookAsync(Incoming(session, row.TotalPenaltyAmount + 1))).IsSuccess);
        Assert.Equal("Unpaid", (await qr.GetWithdrawalStatusAsync(row.Id, session.TransactionId, employee)).Data!.PaymentStatus);
        Assert.True((await qr.ProcessWebhookAsync(Incoming(session, row.TotalPenaltyAmount))).IsSuccess);
        Assert.True((await qr.ProcessWebhookAsync(Incoming(session, row.TotalPenaltyAmount))).Data!.AlreadyPaid);
        Assert.Single(await db.FinancialTransactions.Where(t => t.Status == FinancialTransactionStatus.Success).ToListAsync());
        var held = await db.Products.SingleAsync();
        Assert.Equal(ProductStatus.OnSale, held.Status); Assert.True(held.IsReservedForWithdrawal);
        Assert.Equal("Paid", (await qr.GetWithdrawalStatusAsync(row.Id, session.TransactionId, employee)).Data!.PaymentStatus);
        Assert.False((await qr.InitiateWithdrawalAsync(row.Id, employee, true)).IsSuccess);
        Assert.True((await service.HandoverAsync(row.Id, employee, new("Delivered"), default)).IsSuccess);
        Assert.Equal(ProductStatus.EarlyWithdrawn, held.Status);
    }

    [Fact]
    public async Task RotatedQrAcceptsLatePaymentAndFailsOtherSessionWithoutDoubleCollection()
    {
        var (db, service, customer, employee, product) = Setup();
        await using var cleanup = db;
        var row = (await service.CreateAsync(customer, new([product.Id], "Withdraw"), default)).Data!;
        await service.ApproveAsync(row.Id, employee, default);
        var qr = Qr(db);
        var first = (await qr.InitiateWithdrawalAsync(row.Id, employee, false)).Data!;
        var second = (await qr.InitiateWithdrawalAsync(row.Id, employee, true)).Data!;
        Assert.NotEqual(first.PaymentReference, second.PaymentReference);
        Assert.True((await qr.ProcessWebhookAsync(Incoming(first, row.TotalPenaltyAmount))).IsSuccess);
        Assert.False((await qr.ProcessWebhookAsync(Incoming(second, row.TotalPenaltyAmount))).IsSuccess);
        Assert.Equal("Failed", (await qr.GetWithdrawalStatusAsync(row.Id, second.TransactionId, employee)).Data!.Status);
    }

    [Fact]
    public async Task ExpiredQrAllowsRejectionButLateTransferCannotReopenRequest()
    {
        var (db, service, customer, employee, product) = Setup();
        await using var cleanup = db;
        var row = (await service.CreateAsync(customer, new([product.Id], "Withdraw"), default)).Data!;
        await service.ApproveAsync(row.Id, employee, default);
        var qr = Qr(db);
        var session = (await qr.InitiateWithdrawalAsync(row.Id, employee, false)).Data!;
        (await db.FinancialTransactions.SingleAsync()).ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
        Assert.True((await qr.GetWithdrawalStatusAsync(row.Id, session.TransactionId, employee)).Data!.IsExpired);
        Assert.True((await service.RejectAsync(row.Id, employee, new("Customer changed plans"), default)).IsSuccess);
        Assert.False((await qr.ProcessWebhookAsync(Incoming(session, row.TotalPenaltyAmount))).IsSuccess);
        Assert.False((await db.Products.SingleAsync()).IsReservedForWithdrawal);
    }
}
