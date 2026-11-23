using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OCS.Api.Configuration;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Controllers;
using OCS.Api.Entities;
using OCS.Api.Models.Pos;
using OCS.Api.Services;

namespace OCS.Api.IntegrationTests;

public sealed class PosVietQrCartCheckoutTests
{
    [Fact]
    public async Task CheckoutVietQrCreatesUnpaidInvoiceAndInitiatesSession()
    {
        var (db, employeeId, products, controller) = await CreateHarnessAsync();
        await using (db)
        {
            var result = await controller.CheckoutVietQr(
                new PosVietQrCheckoutRequest([products[0].Id, products[1].Id]),
                CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            var session = Assert.IsType<VietQrPaymentSessionDto>(ok.Value);
            Assert.Equal("Pending", session.Status);
            Assert.Equal("Unpaid", session.InvoicePaymentStatus);
            Assert.Equal("550000.00", session.Amount);
            Assert.NotEmpty(session.QrCodeUrl);

            var invoice = await db.SaleInvoices
                .Include(i => i.Lines)
                .SingleAsync(i => i.Id == session.SaleInvoiceId);
            Assert.Equal(InvoicePaymentStatus.Unpaid, invoice.PaymentStatus);
            Assert.Equal(550000m, invoice.TotalAmount);
            Assert.Equal(2, invoice.Lines.Count);
            Assert.All(products, p => Assert.Equal(ProductStatus.OnSale, p.Status));
        }
    }

    [Fact]
    public async Task CheckoutVietQrReusesUnpaidInvoiceWhenCartMatchesAndRotatesWhenCartChanges()
    {
        var (db, employeeId, products, controller) = await CreateHarnessAsync();
        await using (db)
        {
            var firstResult = await controller.CheckoutVietQr(
                new PosVietQrCheckoutRequest([products[0].Id]),
                CancellationToken.None);
            var firstSession = Assert.IsType<VietQrPaymentSessionDto>(Assert.IsType<OkObjectResult>(firstResult).Value);

            var secondResult = await controller.CheckoutVietQr(
                new PosVietQrCheckoutRequest([products[0].Id]),
                CancellationToken.None);
            var secondSession = Assert.IsType<VietQrPaymentSessionDto>(Assert.IsType<OkObjectResult>(secondResult).Value);
            Assert.Equal(firstSession.SaleInvoiceId, secondSession.SaleInvoiceId);
            Assert.Equal(firstSession.TransactionId, secondSession.TransactionId);

            // Cashier adds a second item to cart and regenerates VietQR
            var expandedResult = await controller.CheckoutVietQr(
                new PosVietQrCheckoutRequest([products[0].Id, products[1].Id]),
                CancellationToken.None);
            var expandedSession = Assert.IsType<VietQrPaymentSessionDto>(Assert.IsType<OkObjectResult>(expandedResult).Value);
            Assert.NotEqual(firstSession.SaleInvoiceId, expandedSession.SaleInvoiceId);
            Assert.Equal("550000.00", expandedSession.Amount);

            var staleInvoice = await db.SaleInvoices
                .Include(i => i.Lines)
                .SingleAsync(i => i.Id == firstSession.SaleInvoiceId);
            Assert.Equal(0m, staleInvoice.TotalAmount);
            Assert.Empty(staleInvoice.Lines);

            var staleTx = await db.FinancialTransactions.SingleAsync(t => t.Id == firstSession.TransactionId);
            Assert.Equal(FinancialTransactionStatus.Failed, staleTx.Status);
        }
    }

    [Fact]
    public async Task CheckoutCashSucceedsAfterAbandonedVietQrInvoice()
    {
        var (db, employeeId, products, controller) = await CreateHarnessAsync();
        await using (db)
        {
            var qrResult = await controller.CheckoutVietQr(
                new PosVietQrCheckoutRequest([products[0].Id]),
                CancellationToken.None);
            var qrSession = Assert.IsType<VietQrPaymentSessionDto>(Assert.IsType<OkObjectResult>(qrResult).Value);

            var cashResult = await controller.CheckoutCash(
                new PosCashPaymentRequest([products[0].Id], 300000m),
                CancellationToken.None);
            var cashOk = Assert.IsType<OkObjectResult>(cashResult);
            var cashDto = Assert.IsType<PosCashPaymentResponse>(cashOk.Value);
            Assert.Equal(250000m, cashDto.TotalAmount);
            Assert.Equal(50000m, cashDto.ChangeAmount);

            var staleTx = await db.FinancialTransactions.SingleAsync(t => t.Id == qrSession.TransactionId);
            Assert.Equal(FinancialTransactionStatus.Failed, staleTx.Status);

            var soldProduct = await db.Products.SingleAsync(p => p.Id == products[0].Id);
            Assert.Equal(ProductStatus.Sold, soldProduct.Status);
        }
    }

    [Fact]
    public async Task CheckoutVietQrRejectsSoldProductWithConflict()
    {
        var (db, _, products, controller) = await CreateHarnessAsync();
        await using (db)
        {
            products[0].MarkSold();
            await db.SaveChangesAsync();

            var result = await controller.CheckoutVietQr(
                new PosVietQrCheckoutRequest([products[0].Id]),
                CancellationToken.None);

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status409Conflict, objectResult.StatusCode);
        }
    }

    private static async Task<(AppDbContext Db, Guid EmployeeId, List<Product> Products, PosController Controller)> CreateHarnessAsync()
    {
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(dbOptions);
        var employeeId = Guid.NewGuid();
        var account = new Account
        {
            Id = employeeId,
            FullName = "POS Staff",
            PhoneNumber = "0900000888",
            AccountType = AccountType.Internal,
            Status = AccountStatus.Active
        };
        var employee = new EmployeeProfile
        {
            Id = employeeId,
            Account = account
        };

        var p1 = new Product
        {
            Id = Guid.NewGuid(),
            Barcode = "BC-CART-001",
            Name = "Áo Thun Ký Gửi",
            Size = "M",
            ConditionPercentage = 95,
            ListedPrice = 250000m,
            Status = ProductStatus.OnSale
        };
        var p2 = new Product
        {
            Id = Guid.NewGuid(),
            Barcode = "BC-CART-002",
            Name = "Quần Baggy Caro",
            Size = "L",
            ConditionPercentage = 92,
            ListedPrice = 300000m,
            Status = ProductStatus.OnSale
        };

        db.Accounts.Add(account);
        db.EmployeeProfiles.Add(employee);
        db.Products.AddRange(p1, p2);
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
        var env = new FakeHostEnvironment();
        var posService = new PosService(db);
        var vietQrService = new VietQrPaymentService(db, adapter, vietQrOptions, TimeProvider.System, env);

        var controller = new PosController(posService, vietQrService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, employeeId.ToString()),
                        new Claim(ClaimTypes.Role, "Staff")
                    ], "TestAuth"))
                }
            }
        };

        return (db, employeeId, [p1, p2], controller);
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "OCS.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
