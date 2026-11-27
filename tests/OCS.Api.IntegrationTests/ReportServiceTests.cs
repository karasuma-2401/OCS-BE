using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OCS.Api.Authorization;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Models.Reports;
using OCS.Api.Services;

namespace OCS.Api.IntegrationTests;

public sealed class ReportServiceTests
{
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public async Task OverviewAndReportsCalculateMetricsAccuratelyWithoutDoubleCounting()
    {
        // 2026-11-27 10:00:00 +07:00
        var nowUtc = new DateTimeOffset(2026, 11, 27, 3, 0, 0, TimeSpan.Zero);
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        await using (db)
        {
            var brand = new Brand { Name = "Brand", DefaultCommissionRate = 20m };
            var category = new Category { Name = "Category" };
            var cashierAccount = new Account
            {
                AccountType = AccountType.Internal,
                FullName = "Thu ngân A",
                Username = "cashier_a"
            };
            var cashier = new EmployeeProfile { Id = cashierAccount.Id, Account = cashierAccount, JobTitle = "Cashier" };
            var customerAccount = new Account
            {
                AccountType = AccountType.Customer,
                FullName = "Khách B",
                Username = "cust_b"
            };
            var customer = new CustomerProfile { Id = customerAccount.Id, Account = customerAccount };

            // 1) Active OnSale snapshot (2 items)
            var onSaleConsign = new Product { Name = "OnSale 1", Barcode = "OS-1", Size = "M", BrandId = brand.Id, CategoryId = category.Id, Status = ProductStatus.OnSale };
            var onSaleBuyout = new Product { Name = "OnSale 2", Barcode = "OS-2", Size = "L", BrandId = brand.Id, CategoryId = category.Id, Status = ProductStatus.OnSale };

            // 2) Sold Consignment item (Cash, 1,000,000đ)
            var contract = new ConsignmentContract
            {
                ContractNumber = "KG-REP-1",
                CustomerId = customer.Id,
                CustomerNameSnapshot = "Khách B",
                SignedAt = nowUtc.AddDays(-10),
                EndsAt = nowUtc.AddDays(20),
                Status = ConsignmentContractStatus.Active
            };
            var consignSold = new Product { Name = "Áo Ký Gửi", Barcode = "KG-1", Size = "M", BrandId = brand.Id, CategoryId = category.Id };
            consignSold.AssignConsignmentTerms(contract.Id, 1_000_000m, brand);
            consignSold.OfferForSale();
            consignSold.MarkSold();

            // 3) Buyout item (listedPrice 700,000đ, purchasePrice 400,000đ -> profit 300,000đ)
            var buyout = new Buyout
            {
                BuyoutNumber = "BO-REP-1",
                CustomerId = customer.Id,
                CreatedByEmployeeId = cashier.Id,
                TotalPurchasePrice = 400_000m,
                Status = BuyoutStatus.Approved,
                PaidAt = nowUtc.AddHours(-1)
            };
            var buyoutSold = new Product { Name = "Túi Mua Đứt", Barcode = "BO-1", Size = "Free", BrandId = brand.Id, CategoryId = category.Id };
            buyoutSold.AssignBuyoutTerms(buyout.Id, 700_000m, 400_000m);
            buyoutSold.OfferForSale();
            buyoutSold.MarkSold();

            // Invoices & FinancialTransactions:
            // - 1 Cash Success + Paid
            // - 1 VietQR Success + Paid
            // - 1 Pending + Unpaid
            // - 1 Success + Unpaid (must NOT be counted in PosRevenue)
            var invCash = new SaleInvoice
            {
                InvoiceNumber = "INV-01",
                SoldByEmployeeId = cashier.Id,
                TotalAmount = 1_000_000m,
                PaymentStatus = InvoicePaymentStatus.Paid,
                Lines = [new SaleInvoiceLine { ProductId = consignSold.Id, SalePrice = 1_000_000m }]
            };
            var txCash = new FinancialTransaction
            {
                SaleInvoiceId = invCash.Id,
                Type = FinancialTransactionType.Income,
                PaymentMethod = PaymentMethod.Cash,
                Amount = 1_000_000m,
                Status = FinancialTransactionStatus.Success,
                OccurredAt = new DateTimeOffset(2026, 11, 26, 17, 30, 0, TimeSpan.Zero), // 2026-11-27 00:30 VN
                PerformedByEmployeeId = cashier.Id
            };

            var invQr = new SaleInvoice
            {
                InvoiceNumber = "INV-02",
                SoldByEmployeeId = cashier.Id,
                TotalAmount = 700_000m,
                PaymentStatus = InvoicePaymentStatus.Paid,
                Lines = [new SaleInvoiceLine { ProductId = buyoutSold.Id, SalePrice = 700_000m }]
            };
            var txQr = new FinancialTransaction
            {
                SaleInvoiceId = invQr.Id,
                Type = FinancialTransactionType.Income,
                PaymentMethod = PaymentMethod.VietQr,
                Amount = 700_000m,
                Status = FinancialTransactionStatus.Success,
                OccurredAt = nowUtc, // 2026-11-27 10:00 VN
                PerformedByEmployeeId = cashier.Id
            };

            var invPending = new SaleInvoice
            {
                InvoiceNumber = "INV-03",
                SoldByEmployeeId = cashier.Id,
                TotalAmount = 500_000m,
                PaymentStatus = InvoicePaymentStatus.Unpaid
            };
            var txPending = new FinancialTransaction
            {
                SaleInvoiceId = invPending.Id,
                Type = FinancialTransactionType.Income,
                PaymentMethod = PaymentMethod.VietQr,
                Amount = 500_000m,
                Status = FinancialTransactionStatus.Pending,
                OccurredAt = nowUtc,
                PerformedByEmployeeId = cashier.Id
            };

            var invUnpaidSuccess = new SaleInvoice
            {
                InvoiceNumber = "INV-04-UNPAID",
                SoldByEmployeeId = cashier.Id,
                TotalAmount = 900_000m,
                PaymentStatus = InvoicePaymentStatus.Unpaid
            };
            var txUnpaidSuccess = new FinancialTransaction
            {
                SaleInvoiceId = invUnpaidSuccess.Id,
                Type = FinancialTransactionType.Income,
                PaymentMethod = PaymentMethod.Cash,
                Amount = 900_000m,
                Status = FinancialTransactionStatus.Success,
                OccurredAt = nowUtc,
                PerformedByEmployeeId = cashier.Id
            };

            // Completed Payout stores Commission & ServiceFee as negative amounts (matching SettlementService)
            var completedPayout = new Payout
            {
                PayoutNumber = "PAY-DONE",
                ConsignmentContractId = contract.Id,
                TotalAmount = 770_000m,
                Status = PayoutStatus.Completed,
                CompletedAt = nowUtc,
                Details =
                [
                    new PayoutDetail { Type = PayoutLineType.SaleProceeds, Description = "Tiền bán", Amount = 1_000_000m, ProductId = consignSold.Id },
                    new PayoutDetail { Type = PayoutLineType.Commission, Description = "Hoa hồng", Amount = -200_000m, ProductId = consignSold.Id },
                    new PayoutDetail { Type = PayoutLineType.ServiceFee, Description = "Phí dịch vụ", Amount = -30_000m }
                ]
            };

            var transferredPayout = new Payout
            {
                PayoutNumber = "PAY-WAIT",
                ConsignmentContractId = contract.Id,
                TotalAmount = 300_000m,
                Status = PayoutStatus.Transferred,
                TransferredAt = nowUtc,
                Details = [new PayoutDetail { Type = PayoutLineType.Commission, Description = "Chờ XN", Amount = -100_000m }]
            };

            db.AddRange(brand, category, cashierAccount, cashier, customerAccount, customer,
                onSaleConsign, onSaleBuyout, contract, consignSold, buyout, buyoutSold,
                invCash, txCash, invQr, txQr, invPending, txPending, invUnpaidSuccess, txUnpaidSuccess,
                completedPayout, transferredPayout);
            await db.SaveChangesAsync();

            var service = new ReportService(db, new FixedClock(nowUtc));
            var filter = new ReportFilterRequest(new DateOnly(2026, 11, 27), new DateOnly(2026, 11, 27), "hour");

            var overview = (await service.GetOverviewAsync(filter, default)).Data!;
            Assert.Equal(2, overview.ActiveOnSaleCount);
            Assert.Equal(1_700_000m, overview.PosRevenue); // excludes Pending and Unpaid-invoice Success
            Assert.Equal(2, overview.PosInvoiceCount);
            Assert.Equal(1, overview.PendingPosTransactionCount);
            Assert.Equal(200_000m, overview.CompletedCommission); // positive convention
            Assert.Equal(1, overview.CompletedPayoutCount);
            Assert.Equal(400_000m, overview.BuyoutPurchaseCost);
            Assert.Equal(700_000m, overview.BuyoutResaleRevenue);
            Assert.Equal(300_000m, overview.BuyoutResaleProfit);
            Assert.Equal(1, overview.SoldBuyoutProductCount);

            var posReport = (await service.GetPosRevenueAsync(filter, default)).Data!;
            Assert.Equal(2, posReport.Breakdown.Count);
            Assert.Equal(2, posReport.Details.TotalCount);
            Assert.DoesNotContain(posReport.Details.Items, x => x.InvoiceNumber == "INV-04-UNPAID");

            var commReport = (await service.GetConsignmentCommissionsAsync(filter, default)).Data!;
            Assert.Equal(200_000m, commReport.TotalCommission);
            var commDetail = Assert.Single(commReport.Details.Items);
            Assert.Equal(200_000m, commDetail.CommissionAmount);
            Assert.Equal(30_000m, commDetail.ServiceFeeAmount);

            var buyoutReport = (await service.GetBuyoutProfitAsync(filter, default)).Data!;
            Assert.Equal(300_000m, buyoutReport.ResaleProfit);
            Assert.Single(buyoutReport.Details.Items);

            var csv = (await service.ExportCsvAsync("pos", filter, default)).Data;
            var csvText = Encoding.UTF8.GetString(csv.Content);
            Assert.Contains("INV-01", csvText);
            Assert.Contains("INV-02", csvText);
            Assert.DoesNotContain("INV-03", csvText);
            Assert.DoesNotContain("INV-04-UNPAID", csvText);
        }
    }

    [Fact]
    public async Task ExportCsvReturnsAllRowsBeyond200PageLimit()
    {
        var nowUtc = new DateTimeOffset(2026, 11, 27, 5, 0, 0, TimeSpan.Zero);
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        await using (db)
        {
            var cashierAccount = new Account { AccountType = AccountType.Internal, FullName = "Thu ngân", Username = "cashier_bulk" };
            var cashier = new EmployeeProfile { Id = cashierAccount.Id, Account = cashierAccount, JobTitle = "Cashier" };
            db.AddRange(cashierAccount, cashier);

            for (var i = 1; i <= 205; i++)
            {
                var inv = new SaleInvoice
                {
                    InvoiceNumber = $"INV-BULK-{i:000}",
                    SoldByEmployeeId = cashier.Id,
                    TotalAmount = 100_000m,
                    PaymentStatus = InvoicePaymentStatus.Paid
                };
                var tx = new FinancialTransaction
                {
                    SaleInvoiceId = inv.Id,
                    Type = FinancialTransactionType.Income,
                    PaymentMethod = PaymentMethod.Cash,
                    Amount = 100_000m,
                    Status = FinancialTransactionStatus.Success,
                    OccurredAt = nowUtc.AddSeconds(i),
                    PerformedByEmployeeId = cashier.Id
                };
                db.AddRange(inv, tx);
            }
            await db.SaveChangesAsync();

            var service = new ReportService(db, new FixedClock(nowUtc));
            var filter = new ReportFilterRequest(new DateOnly(2026, 11, 27), new DateOnly(2026, 11, 27), "day");

            var csv = (await service.ExportCsvAsync("pos", filter, default)).Data;
            var lines = Encoding.UTF8.GetString(csv.Content)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            Assert.Equal(206, lines.Length); // 1 header + 205 data rows
        }
    }

    [Fact]
    public async Task ReportsEndpointsReturn403WithoutReportsReadPermissionAnd200WithPermission()
    {
        Assert.Contains(PermissionCatalog.ScreenReports, PermissionCatalog.DefaultsFor("Admin"));
        Assert.Contains(PermissionCatalog.ReportsRead, PermissionCatalog.DefaultsFor("Admin"));
        Assert.Contains(PermissionCatalog.ReportsRead, PermissionCatalog.DefaultsFor("Manager"));
        Assert.DoesNotContain(PermissionCatalog.ReportsRead, PermissionCatalog.DefaultsFor("Staff"));
        Assert.False(PermissionCatalog.CanAssign("Staff", PermissionCatalog.ReportsRead));

        var databaseName = Guid.NewGuid().ToString();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "",
                ["Redis:ConnectionString"] = "",
                ["R2:ServiceUrl"] = "",
                ["Jwt:Issuer"] = "test",
                ["Jwt:Audience"] = "test",
                ["Jwt:Key"] = "test-only-key-with-at-least-32-bytes"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
                services.RemoveAll<AppDbContext>();
                services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName));
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "ReportTest";
                    options.DefaultChallengeScheme = "ReportTest";
                    options.DefaultForbidScheme = "ReportTest";
                }).AddScheme<AuthenticationSchemeOptions, PermissionHeaderAuthHandler>("ReportTest", _ => { });
            });
        });

        var staffUserId = Guid.NewGuid();
        var managerUserId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.AddRange(
                new Account { Id = staffUserId, FullName = "Nhân viên", PhoneNumber = "0900000001" },
                new AccountRole { AccountId = staffUserId, RoleId = new Guid("20000000-0000-0000-0000-000000000002") },
                new Account { Id = managerUserId, FullName = "Quản lý", PhoneNumber = "0900000002" },
                new AccountRole { AccountId = managerUserId, RoleId = new Guid("20000000-0000-0000-0000-000000000001") });
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-Bypass-Csrf", "true");

        // 1. Staff has screens.reports ONLY (missing reports.read) -> 403 Forbidden
        using var forbiddenReq = new HttpRequestMessage(HttpMethod.Get, "/api/reports/overview");
        forbiddenReq.Headers.Add("X-Test-UserId", staffUserId.ToString());
        forbiddenReq.Headers.Add("X-Test-Role", "Staff");
        using var forbiddenRes = await client.SendAsync(forbiddenReq);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenRes.StatusCode);

        using var forbiddenExport = new HttpRequestMessage(HttpMethod.Get, "/api/reports/export?reportType=pos");
        forbiddenExport.Headers.Add("X-Test-UserId", staffUserId.ToString());
        forbiddenExport.Headers.Add("X-Test-Role", "Staff");
        using var forbiddenExportRes = await client.SendAsync(forbiddenExport);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenExportRes.StatusCode);

        // 2. Manager has reports.read -> 200 OK
        using var okReq = new HttpRequestMessage(HttpMethod.Get, "/api/reports/overview");
        okReq.Headers.Add("X-Test-UserId", managerUserId.ToString());
        okReq.Headers.Add("X-Test-Role", "Manager");
        using var okRes = await client.SendAsync(okReq);
        Assert.Equal(HttpStatusCode.OK, okRes.StatusCode);
    }

    private sealed class PermissionHeaderAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var userId = Request.Headers.TryGetValue("X-Test-UserId", out var uid) ? uid.ToString() : Guid.NewGuid().ToString();
            var role = Request.Headers.TryGetValue("X-Test-Role", out var r) ? r.ToString() : "Staff";
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId), new Claim(ClaimTypes.Role, role)],
                Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
