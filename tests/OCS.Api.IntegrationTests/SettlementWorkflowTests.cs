using System.Reflection;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Models.Settlements;
using OCS.Api.Services;
using OCS.Api.Storage;

namespace OCS.Api.IntegrationTests;

public sealed class SettlementWorkflowTests
{
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static (AppDbContext Db, SettlementService Service, ConsignmentContract Contract, SaleInvoice Invoice) Setup(bool activeContract = false)
    {
        var now = DateTimeOffset.UtcNow;
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var brand = new Brand { Name = "Brand", DefaultCommissionRate = 20m };
        var category = new Category { Name = "Category" };
        var contract = new ConsignmentContract
        {
            ContractNumber = "KG-SETTLEMENT",
            SignedAt = now.AddDays(-30),
            EndsAt = activeContract ? now.AddDays(30) : now.AddDays(-1),
            BankCodeSnapshot = "VCB",
            BankAccountNumberSnapshot = "123456789",
            BankAccountHolderSnapshot = "NGUYEN VAN A",
            Status = ConsignmentContractStatus.Active
        };
        var product = new Product { Name = "Coat", Barcode = "BC-SETTLEMENT", Size = "M", BrandId = brand.Id, CategoryId = category.Id };
        product.AssignConsignmentTerms(contract.Id, 1000000m, brand);
        product.OfferForSale();
        product.MarkSold();
        var invoice = new SaleInvoice
        {
            InvoiceNumber = "INV-SETTLEMENT",
            PaymentStatus = InvoicePaymentStatus.Paid,
            TotalAmount = 1000000m,
            Lines = [new SaleInvoiceLine { ProductId = product.Id, SalePrice = 1000000m }]
        };
        db.AddRange(brand, category, contract, product, invoice);
        db.SaveChanges();
        db.ChangeTracker.Clear();
        var store = new PrivateProductImageStore(new ServiceCollection().BuildServiceProvider(), Options.Create(new R2Options()));
        return (db, new SettlementService(db, new FixedClock(now), store), contract, invoice);
    }

    [Fact]
    public async Task FinalSettlementIncludesManagerFeeAndLocksAfterApproval()
    {
        var (db, service, contract, _) = Setup();
        await using (db)
        {
            var actor = Guid.NewGuid();
            var preview = await service.PreviewAsync(contract.Id, default);
            Assert.Null(preview.Data!.BlockReason);
            Assert.Equal("ReadyFinal", preview.Data.EligibilityKind);
            Assert.Contains(preview.Data.EligibilityFactors!, x => x.Contains("Đã hết hạn"));
            Assert.Equal(800000m, preview.Data.TotalAmount);

            var created = await service.CreatePayoutAsync(contract.Id, actor, new CreatePayoutRequest(50000m, "Phí đóng gói"), default);
            Assert.True(created.IsSuccess);
            Assert.Equal(750000m, created.Data!.TotalAmount);
            Assert.Contains(created.Data.Lines, x => x.Type == "ServiceFee" && x.Amount == -50000m);
            Assert.False((await service.CreatePayoutAsync(contract.Id, actor, new CreatePayoutRequest(0, null), default)).IsSuccess);

            var revised = await service.UpdateServiceFeeAsync(created.Data.Id, actor,
                new UpdatePayoutServiceFeeRequest(30000m, "Phí đóng gói", "Điều chỉnh sau kiểm tra"), default);
            Assert.Equal(770000m, revised.Data!.TotalAmount);
            var payment = await service.UpdatePaymentAsync(created.Data.Id, actor,
                new UpdatePayoutPaymentRequest("ACB", "987654321", "NGUYEN VAN A", "Khách đổi ngân hàng"), default);
            Assert.Equal("ACB", payment.Data!.BankCode);
            Assert.Contains((await service.HistoryAsync(created.Data.Id, default)).Data!, x => x.Reason == "Khách đổi ngân hàng");

            var approved = await service.ApproveAsync(created.Data.Id, actor, default);
            Assert.Equal("Approved", approved.Data!.Status);
            Assert.Equal(actor, approved.Data.ApprovedByEmployeeId);
            Assert.NotNull(approved.Data.ApprovedAt);
            Assert.False((await service.ApproveAsync(created.Data.Id, actor, default)).IsSuccess);
            Assert.False((await service.RejectAsync(created.Data.Id, actor, new RejectPayoutRequest("Không đồng ý"), default)).IsSuccess);
            Assert.False((await service.UpdatePaymentAsync(created.Data.Id, actor,
                new UpdatePayoutPaymentRequest("ACB", "999", "NGUYEN VAN B", "Đổi tài khoản"), default)).IsSuccess);
            Assert.False((await service.UpdateServiceFeeAsync(created.Data.Id, actor,
                new UpdatePayoutServiceFeeRequest(0, null, "Không thu nữa"), default)).IsSuccess);
        }
    }

    [Fact]
    public async Task RejectionPreservesOldStatementAndReleasesSourcesForNewPayout()
    {
        var (db, service, contract, _) = Setup();
        await using (db)
        {
            var actor = Guid.NewGuid();
            var original = (await service.CreatePayoutAsync(contract.Id, actor, new CreatePayoutRequest(20000m, "Phí vệ sinh"), default)).Data!;
            var rejected = await service.RejectAsync(original.Id, actor, new RejectPayoutRequest("Cần kiểm tra phí"), default);
            Assert.Equal("Rejected", rejected.Data!.Status);
            Assert.Equal(780000m, rejected.Data.TotalAmount);
            Assert.Equal("Cần kiểm tra phí", rejected.Data.RejectionReason);
            Assert.All(await db.PayoutDetails.Where(x => x.PayoutId == original.Id).ToListAsync(), x => Assert.True(x.IsReleased));

            var replacement = await service.CreatePayoutAsync(contract.Id, actor, new CreatePayoutRequest(0m, null), default);
            Assert.True(replacement.IsSuccess);
            Assert.Equal(800000m, replacement.Data!.TotalAmount);
            Assert.DoesNotContain(replacement.Data.Lines, x => x.Type == "ServiceFee");
            Assert.Equal(2, await db.Payouts.CountAsync());
        }
    }

    [Fact]
    public async Task PendingPaymentOrOpenBatchPreventsFinalSettlement()
    {
        var (db, service, contract, invoice) = Setup();
        await using (db)
        {
            db.FinancialTransactions.Add(new FinancialTransaction
            {
                SaleInvoiceId = invoice.Id,
                Type = FinancialTransactionType.Income,
                Status = FinancialTransactionStatus.Pending,
                Amount = 1000000m,
                PerformedByEmployeeId = Guid.NewGuid()
            });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            Assert.Contains("giao dịch", (await service.PreviewAsync(contract.Id, default)).Data!.BlockReason!);
            Assert.False((await service.CreatePayoutAsync(contract.Id, Guid.NewGuid(), new CreatePayoutRequest(0, null), default)).IsSuccess);
        }

        var (activeDb, activeService, activeContract, _) = Setup(activeContract: true);
        await using (activeDb)
        {
            var extra = new Product { Name = "Shirt", Barcode = "BC-ACTIVE", Size = "M" };
            extra.AssignConsignmentTerms(activeContract.Id, 100000m, new Brand { DefaultCommissionRate = 20m });
            extra.OfferForSale();
            activeDb.Products.Add(extra);
            await activeDb.SaveChangesAsync();
            var blockedPreview = (await activeService.PreviewAsync(activeContract.Id, default)).Data!;
            Assert.Contains("cuối đợt", blockedPreview.BlockReason!);
            Assert.NotEmpty(blockedPreview.Lines);
        }
    }

    [Fact]
    public async Task IncidentApprovalCreatesPendingPayoutAndAllowsInterimCompensationResettlement()
    {
        var now = DateTimeOffset.UtcNow;
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await using (db)
        {
            var brand = new Brand { Name = "Brand", DefaultCommissionRate = 20m };
            var category = new Category { Name = "Category" };
            var contract = new ConsignmentContract
            {
                ContractNumber = "KG-INC-AUTO",
                SignedAt = now.AddDays(-5),
                EndsAt = now.AddDays(55),
                BankCodeSnapshot = "VCB",
                BankAccountNumberSnapshot = "123456",
                BankAccountHolderSnapshot = "NGUYEN VAN A",
                Status = ConsignmentContractStatus.Active
            };
            var first = new Product { Name = "Blazer", Barcode = "BC-INC-1", Size = "M", BrandId = brand.Id, CategoryId = category.Id };
            first.AssignConsignmentTerms(contract.Id, 650000m, brand);
            first.OfferForSale();
            var second = new Product { Name = "Jacket", Barcode = "BC-INC-2", Size = "L", BrandId = brand.Id, CategoryId = category.Id };
            second.AssignConsignmentTerms(contract.Id, 490000m, brand);
            second.OfferForSale();
            var activeThird = new Product { Name = "Dress", Barcode = "BC-INC-3", Size = "S", BrandId = brand.Id, CategoryId = category.Id };
            activeThird.AssignConsignmentTerms(contract.Id, 300000m, brand);
            activeThird.OfferForSale();
            db.AddRange(brand, category, contract, first, second, activeThird);
            await db.SaveChangesAsync();

            var clock = new FixedClock(now);
            var incidentService = new IncidentService(db, clock);
            var store = new PrivateProductImageStore(new ServiceCollection().BuildServiceProvider(), Options.Create(new R2Options()));
            var settlementService = new SettlementService(db, clock, store);
            var actor = Guid.NewGuid();

            var report1 = (await incidentService.CreateAsync(actor,
                new OCS.Api.Models.Incidents.CreateIncidentRequest(first.Id, IncidentType.Damaged, "Rách tay áo", ["/api/storage/inspections/1.jpg"]), default)).Data!;
            var report2 = (await incidentService.CreateAsync(actor,
                new OCS.Api.Models.Incidents.CreateIncidentRequest(second.Id, IncidentType.Damaged, "Mất nút", ["/api/storage/inspections/2.jpg"]), default)).Data!;

            var approved1 = (await incidentService.ApproveAsync(report1.Id, actor,
                new OCS.Api.Models.Incidents.ApproveIncidentRequest(650000m, null), default)).Data!;
            Assert.NotNull(approved1.PayoutId);
            Assert.Equal("Pending", approved1.PayoutStatus);

            var approved2 = (await incidentService.ApproveAsync(report2.Id, actor,
                new OCS.Api.Models.Incidents.ApproveIncidentRequest(490000m, null), default)).Data!;
            Assert.Equal(approved1.PayoutId, approved2.PayoutId);

            var payouts = (await settlementService.ListPayoutsAsync(default)).Data!;
            var payout = Assert.Single(payouts);
            Assert.Equal(1140000m, payout.TotalAmount);
            Assert.Equal(2, payout.Lines.Count);

            var summary = Assert.Single((await settlementService.ContractsAsync(default)).Data!);
            Assert.Equal(payout.Id, summary.OpenPayoutId);
            Assert.Equal("OpenPayout", summary.EligibilityKind);
            Assert.Equal(2, summary.CompensatedProductCount);
            Assert.Equal(1, summary.ActiveProductCount);

            var rejected = await settlementService.RejectAsync(payout.Id, actor, new RejectPayoutRequest("Điều chỉnh thông tin"), default);
            Assert.True(rejected.IsSuccess);

            var previewAfterReject = (await settlementService.PreviewAsync(contract.Id, default)).Data!;
            Assert.Null(previewAfterReject.BlockReason);
            Assert.True(previewAfterReject.IsCompensationOnly);
            Assert.True(previewAfterReject.IsMidTerm);
            Assert.Equal("ReadyInterim", previewAfterReject.EligibilityKind);
            Assert.Contains(previewAfterReject.EligibilityFactors!, x => x.Contains("bồi thường"));
            Assert.Equal(1140000m, previewAfterReject.TotalAmount);

            var recreated = await settlementService.CreatePayoutAsync(contract.Id, actor, new CreatePayoutRequest(0m, null), default);
            Assert.True(recreated.IsSuccess);
            Assert.Equal(1140000m, recreated.Data!.TotalAmount);

            var approvedPayout = await settlementService.ApproveAsync(recreated.Data.Id, actor, default);
            Assert.True(approvedPayout.IsSuccess);
            Assert.Equal("Approved", approvedPayout.Data!.Status);

            var updatedReports = await db.IncidentReports.AsNoTracking()
                .Where(x => x.Id == report1.Id || x.Id == report2.Id)
                .ToListAsync();
            Assert.All(updatedReports, x => Assert.Equal(IncidentStatus.Compensated, x.Status));

            var detail1 = (await incidentService.DetailAsync(report1.Id, default)).Data!;
            Assert.Equal("Compensated", detail1.Status);
            Assert.Equal("Approved", detail1.PayoutStatus);
        }
    }

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task AutoConfirmUsesConfiguredHoursWithoutRetroactiveChangeAndRespectsOpenIncidents()
    {
        var start = new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);
        var clock = new MutableClock(start);
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await using (db)
        {
            var brand = new Brand { Name = "Brand", DefaultCommissionRate = 20m };
            var category = new Category { Name = "Category" };
            var contract = new ConsignmentContract
            {
                ContractNumber = "KG-AUTO-1",
                SignedAt = start.AddDays(-30),
                EndsAt = start.AddDays(-1),
                BankCodeSnapshot = "VCB",
                BankAccountNumberSnapshot = "123456789",
                BankAccountHolderSnapshot = "NGUYEN VAN A",
                Status = ConsignmentContractStatus.Active
            };
            var product = new Product { Name = "Bag", Barcode = "BC-AUTO-1", Size = "M", BrandId = brand.Id, CategoryId = category.Id };
            product.AssignConsignmentTerms(contract.Id, 1000000m, brand);
            product.OfferForSale();
            product.MarkSold();

            db.AddRange(brand, category, contract, product, new SystemParameter
            {
                Key = SystemParameterKeys.PayoutAutoConfirmHours,
                Value = "24",
                Description = "Auto confirm window in hours"
            });

            var payout = new Payout
            {
                PayoutNumber = "PAY-AUTO-1",
                ConsignmentContractId = contract.Id,
                TotalAmount = 800000m,
                Status = PayoutStatus.Transferred,
                TransferredAt = start,
                AutoConfirmAt = start.AddHours(24)
            };
            var openIncident = new IncidentReport
            {
                ProductId = product.Id,
                CreatedByEmployeeId = Guid.NewGuid(),
                Type = IncidentType.Damaged,
                Description = "Khách khiếu nại trầy xước",
                Status = IncidentStatus.Pending
            };
            db.AddRange(payout, openIncident);
            await db.SaveChangesAsync();

            var store = new PrivateProductImageStore(new ServiceCollection().BuildServiceProvider(), Options.Create(new R2Options()));
            var service = new SettlementService(db, clock, store);
            var job = new PayoutAutoCompletionJob(service, new SystemAuditContext());

            // 1. Chưa đến hạn (23h < 24h)
            clock.Now = start.AddHours(23);
            Assert.Equal(0, await job.ExecuteAsync());
            Assert.Equal(PayoutStatus.Transferred, (await db.Payouts.FindAsync(payout.Id))!.Status);

            // 2. Đổi cấu hình lên 72h sau khi đã chuyển tiền -> không hồi tố payout cũ (vẫn giữ AutoConfirmAt = +24h)
            var param = await db.SystemParameters.SingleAsync(x => x.Key == SystemParameterKeys.PayoutAutoConfirmHours);
            param.Value = "72";
            await db.SaveChangesAsync();

            // 3. Quá hạn (25h >= 24h) nhưng còn khiếu nại mở -> vẫn giữ Transferred
            clock.Now = start.AddHours(25);
            Assert.Equal(0, await job.ExecuteAsync());
            Assert.Equal(PayoutStatus.Transferred, (await db.Payouts.FindAsync(payout.Id))!.Status);

            // 4. Khiếu nại đã được xử lý/bồi thường (Compensated) -> job tự động hoàn tất payout và hợp đồng
            openIncident.Status = IncidentStatus.Compensated;
            await db.SaveChangesAsync();

            Assert.Equal(1, await job.ExecuteAsync());
            var completedPayout = await db.Payouts.FindAsync(payout.Id);
            Assert.Equal(PayoutStatus.Completed, completedPayout!.Status);
            Assert.Null(completedPayout.CustomerConfirmedAt);
            Assert.Equal(clock.Now, completedPayout.CompletedAt);
            Assert.Equal(ConsignmentContractStatus.Completed, (await db.ConsignmentContracts.FindAsync(contract.Id))!.Status);

            // 5. Job chạy lặp lại -> idempotent, trả về 0
            Assert.Equal(0, await job.ExecuteAsync());
        }
    }

    [Fact]
    public async Task AutoConfirmDoesNotCompleteContractWithActiveProductsAndPreservesCustomerConfirmation()
    {
        var start = new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);
        var clock = new MutableClock(start);
        var auditContext = new SystemAuditContext();
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new OCS.Api.Context.Interceptors.AuditSaveChangesInterceptor(auditContext))
            .Options);
        await using (db)
        {
            auditContext.Initialize("Seed");
            var brand = new Brand { Name = "Brand", DefaultCommissionRate = 20m };
            var category = new Category { Name = "Category" };
            var customerId = Guid.NewGuid();
            var contract = new ConsignmentContract
            {
                ContractNumber = "KG-AUTO-2",
                CustomerId = customerId,
                SignedAt = start.AddDays(-5),
                EndsAt = start.AddDays(25),
                Status = ConsignmentContractStatus.Active
            };
            var activeProduct = new Product { Name = "Still On Sale", Barcode = "BC-AUTO-2", Size = "L", BrandId = brand.Id, CategoryId = category.Id };
            activeProduct.AssignConsignmentTerms(contract.Id, 500000m, brand);
            activeProduct.OfferForSale();

            var expiredPayout = new Payout
            {
                PayoutNumber = "PAY-AUTO-2",
                ConsignmentContractId = contract.Id,
                TotalAmount = 300000m,
                Status = PayoutStatus.Transferred,
                TransferredAt = start.AddHours(-50),
                AutoConfirmAt = start.AddHours(-2)
            };
            var customerConfirmedPayout = new Payout
            {
                PayoutNumber = "PAY-AUTO-3",
                ConsignmentContractId = contract.Id,
                TotalAmount = 200000m,
                Status = PayoutStatus.Completed,
                TransferredAt = start.AddHours(-60),
                AutoConfirmAt = start.AddHours(-12),
                CustomerConfirmedAt = start.AddHours(-20),
                CompletedAt = start.AddHours(-20)
            };
            db.AddRange(brand, category, contract, activeProduct, expiredPayout, customerConfirmedPayout);
            await db.SaveChangesAsync();

            var store = new PrivateProductImageStore(new ServiceCollection().BuildServiceProvider(), Options.Create(new R2Options()));
            var service = new SettlementService(db, clock, store);
            var job = new PayoutAutoCompletionJob(service, auditContext);

            Assert.Equal(1, await job.ExecuteAsync());

            // Hợp đồng còn sản phẩm OnSale -> vẫn Active
            Assert.Equal(ConsignmentContractStatus.Active, (await db.ConsignmentContracts.FindAsync(contract.Id))!.Status);
            // Payout khách đã xác nhận trước -> giữ nguyên CustomerConfirmedAt
            var confirmed = await db.Payouts.FindAsync(customerConfirmedPayout.Id);
            Assert.Equal(start.AddHours(-20), confirmed!.CustomerConfirmedAt);
            // Audit interceptor ghi nhận ActorType = System và Source = PayoutAutoCompletionJob
            var systemAudit = await db.AuditLogs.AsNoTracking()
                .Where(x => x.EntityType == nameof(Payout) && x.EntityId == expiredPayout.Id.ToString() && x.Source == nameof(PayoutAutoCompletionJob))
                .SingleOrDefaultAsync();
            Assert.NotNull(systemAudit);
            Assert.Equal(AuditActorType.System, systemAudit.ActorType);
        }
    }

    public class FakeS3Proxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IAmazonS3.GetObjectAsync))
            {
                var response = new GetObjectResponse
                {
                    ResponseStream = new MemoryStream([0xff, 0xd8, 0xff, 0xe0]),
                    ContentLength = 4
                };
                response.Headers.ContentType = "image/jpeg";
                return Task.FromResult(response);
            }

            throw new NotImplementedException(targetMethod?.Name);
        }
    }

    [Theory]
    [InlineData(null, 48)]
    [InlineData("24", 24)]
    [InlineData("invalid", 48)]
    [InlineData("0", 48)]
    [InlineData("-10", 48)]
    [InlineData("999999", 48)]
    public async Task RecordTransferSetsAutoConfirmAtFromSystemParameterAndFallsBackWhenInvalidOrOutOfRange(
        string? configuredValue,
        int expectedHours)
    {
        var now = new DateTimeOffset(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);
        var clock = new FixedClock(now);
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await using (db)
        {
            var contract = new ConsignmentContract
            {
                ContractNumber = $"KG-TR-{expectedHours}",
                SignedAt = now.AddDays(-10),
                EndsAt = now.AddDays(-1),
                Status = ConsignmentContractStatus.Active
            };
            var payout = new Payout
            {
                PayoutNumber = $"PAY-TR-{expectedHours}",
                ConsignmentContractId = contract.Id,
                TotalAmount = 500000m,
                Status = PayoutStatus.Approved,
                BankCode = "VCB",
                BankAccountNumber = "123456789",
                BankAccountHolder = "NGUYEN VAN A"
            };
            db.AddRange(contract, payout);
            if (configuredValue is not null)
            {
                db.SystemParameters.Add(new SystemParameter
                {
                    Key = SystemParameterKeys.PayoutAutoConfirmHours,
                    Value = configuredValue
                });
            }
            await db.SaveChangesAsync();

            var s3 = DispatchProxy.Create<IAmazonS3, FakeS3Proxy>();
            var services = new ServiceCollection().AddSingleton(s3).BuildServiceProvider();
            var store = new PrivateProductImageStore(services, Options.Create(new R2Options { BucketName = "test-bucket" }));
            var service = new SettlementService(db, clock, store);

            var result = await service.RecordTransferAsync(
                payout.Id,
                Guid.NewGuid(),
                new RecordPayoutTransferRequest("/api/storage/inspections/proof.jpg", "FT-123456"),
                default);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(PayoutStatus.Transferred.ToString(), result.Data!.Status);
            Assert.Equal(now, result.Data.TransferredAt);
            Assert.Equal(now.AddHours(expectedHours), result.Data.AutoConfirmAt);
        }
    }
}
