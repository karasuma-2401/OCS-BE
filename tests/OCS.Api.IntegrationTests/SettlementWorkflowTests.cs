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
}
