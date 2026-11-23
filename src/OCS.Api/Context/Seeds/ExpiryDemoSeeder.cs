using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OCS.Api.Consts;
using OCS.Api.Entities;
using OCS.Api.Models.Products;
using OCS.Api.Services;

namespace OCS.Api.Context.Seeds;

/// <summary>Refreshes only the expiry samples created with deterministic IDs.</summary>
public sealed class ExpiryDemoSeeder(AppDbContext db, SystemAuditContext auditContext)
{
    private static readonly int[] Kept = [1, 2, 3, 4, 5, 11, 12, 13, 14, 15];
    private static readonly int[] Removed = [6, 7, 8, 9, 10, 16, 17, 18, 19, 20];
    private static readonly string[] Customers =
    ["Nguyễn Minh Anh", "Trần Thu Hà", "Lê Hoàng Phúc", "Phạm Ngọc Linh", "Võ Thảo Vy",
     "Đỗ Hải Yến", "Bùi Khánh Ngọc", "Huỳnh Gia Bảo", "Mai Thanh Trúc", "Đặng Nhật Minh"];
    private static readonly string[] Names =
    ["Áo sơ mi linen trắng", "Chân váy midi kem", "Đầm hoa nhí xanh", "Áo cardigan be",
     "Áo khoác denim", "Quần ống rộng đen", "Đầm lụa cổ vuông", "Áo blouse tay phồng",
     "Áo thun cổ tròn", "Chân váy xếp ly", "Sơ mi kẻ xanh", "Quần jeans ống đứng",
     "Đầm suông màu nâu", "Áo len cổ tim", "Áo khoác kaki", "Quần culottes kem",
     "Đầm dài tay xanh rêu", "Áo polo trắng", "Áo kiểu cổ nơ", "Chân váy chữ A"];
    private static readonly string[] Materials =
    ["Linen", "Cotton", "Voan", "Len", "Denim", "Kaki", "Lụa", "Cotton", "Cotton", "Polyester"];

    private static Guid Id(int kind, int n) => Guid.Parse($"7700000{kind}-0000-0000-0000-{n:000000000000}");
    private static string ContractNo(int slot) => $"KG-2026-{900 + slot:0000}";
    private static string Barcode(int slot, int item) => $"OCS-2026-{900 + slot:0000}-{item:00}";
    private static bool Handled(int slot, int item) => slot is 4 or 8 or 10 || item == 1 && slot is 3 or 7 or 9;
    private static bool DonationHandover(int slot) => slot is 7 or 8 or 9 or 10;

    public async Task InspectAsync(CancellationToken ct = default)
    {
        var ids = Enumerable.Range(1, 20).Select(n => Id(1, n)).ToArray();
        var contracts = await db.ConsignmentContracts.AsNoTracking().Where(x => ids.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.ContractNumber,
                x.CustomerNameSnapshot,
                Products = x.Products.Select(p => new { p.Barcode, p.Status }).ToArray()
            }).ToListAsync(ct);
        foreach (var contract in contracts.OrderBy(x => x.ContractNumber))
        {
            var process = await db.ExpiryProcessings.AsNoTracking()
                .Where(x => x.Items.Any(i => i.Product.ConsignmentContractId == contract.Id))
                .Select(x => new { x.Type, x.Status, Handled = x.Items.Count(i => i.HandledAt != null) })
                .SingleAsync(ct);
            Console.WriteLine($"{contract.ContractNumber}: {contract.CustomerNameSnapshot}; {process.Type}/{process.Status}; handled={process.Handled}; {string.Join(',', contract.Products.Select(p => p.Status))}");
        }
        var productIds = contracts.SelectMany(x => x.Products.Select(p => p.Barcode)).ToArray();
        var receiptCount = await db.DonationReceipts.CountAsync(x =>
            contracts.Select(c => c.ContractNumber).Contains(x.ContractNumber), ct);
        Console.WriteLine($"Total expiry sample contracts: {contracts.Count}; barcodes: {productIds.Length}; donation receipts: {receiptCount}");
    }

    public async Task<int> SeedAsync(CancellationToken ct = default)
    {
        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToArray();
        if (pending.Length != 0)
            throw new InvalidOperationException($"Apply pending migrations first: {string.Join(", ", pending)}");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var allContractIds = Enumerable.Range(1, 20).Select(n => Id(1, n)).ToArray();
        var allProductIds = Enumerable.Range(1, 40).Select(n => Id(3, n)).ToArray();
        var old = await db.ConsignmentContracts.AsNoTracking()
            .Where(x => allContractIds.Contains(x.Id)).Select(x => x.ContractNumber).ToArrayAsync(ct);
        if (old.Length == 10 && Kept.Select((n, i) => ContractNo(i + 1)).All(old.Contains))
        {
            if (await db.Products.CountAsync(x => x.ConsignmentContractId.HasValue &&
                    allContractIds.Contains(x.ConsignmentContractId.Value), ct) != 20 ||
                await db.ExpiryProcessings.CountAsync(x => x.Items.Any(i => i.Product.ConsignmentContractId.HasValue &&
                    allContractIds.Contains(i.Product.ConsignmentContractId.Value)), ct) != 10)
                throw new InvalidOperationException("The refreshed sample set is incomplete.");
            await tx.CommitAsync(ct);
            return 10;
        }
        if (old.Length != 20 || Enumerable.Range(1, 20).Any(n => !old.Contains($"DEMO-EXP-{n:00}")))
            throw new InvalidOperationException("Expected the original 20 untouched expiry sample contracts.");
        await VerifyUntouchedAsync(allContractIds, allProductIds, ct);
        for (var slot = 1; slot <= 10; slot++)
        {
            var n = Kept[slot - 1];
            if (await db.ConsignmentContracts.AnyAsync(x => x.ContractNumber == ContractNo(slot) && x.Id != Id(1, n), ct) ||
                await db.Products.AnyAsync(x =>
                    (x.Barcode == Barcode(slot, 1) || x.Barcode == Barcode(slot, 2)) &&
                    x.ConsignmentContractId != Id(1, n), ct))
                throw new InvalidOperationException($"Sample contract number or barcode {slot} is already in use.");
        }
        var actor = await db.EmployeeProfiles.Include(x => x.Account)
            .Where(x => x.Status == EmployeeStatus.Working && x.Account.Status == AccountStatus.Active)
            .OrderBy(x => x.Id).FirstAsync(ct);
        var brand = await db.Brands.SingleAsync(x => x.Id == Id(4, 1), ct);
        var category = await db.Categories.SingleAsync(x => x.Id == Id(4, 2), ct);
        if (await db.Brands.AnyAsync(x => x.Name == "An Nhiên Studio" && x.Id != brand.Id, ct) ||
            await db.Categories.AnyAsync(x => x.Name == "Trang phục nữ" && x.Id != category.Id, ct) ||
            await db.Products.AnyAsync(x => (x.BrandId == brand.Id || x.CategoryId == category.Id) &&
                !allProductIds.Contains(x.Id), ct))
            throw new InvalidOperationException("The sample brand or category is used elsewhere or its new name exists.");
        auditContext.Initialize("ExpirySampleRefresh");
        await RemoveExtraAsync(ct);
        brand.Name = "An Nhiên Studio";
        category.Name = "Trang phục nữ";
        var now = DateTimeOffset.UtcNow;
        for (var slot = 1; slot <= 10; slot++)
        {
            var n = Kept[slot - 1];
            var contract = await db.ConsignmentContracts.Include(x => x.Products)
                .SingleAsync(x => x.Id == Id(1, n), ct);
            var process = await db.ExpiryProcessings.Include(x => x.Items)
                .SingleAsync(x => x.Id == Id(2, n), ct);
            FillContract(contract, slot, actor, now);
            FillProcessing(process, slot, actor.Id, now);
            for (var item = 1; item <= 2; item++)
            {
                var product = contract.Products.Single(x => x.Id == Id(3, (n - 1) * 2 + item));
                FillProduct(product, slot, item, category, now);
                await db.ProductStatusHistories.Where(x => x.ProductId == product.Id &&
                    x.PreviousStatus == ProductStatus.OnSale && x.NewStatus == ProductStatus.Expired)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.OccurredAt, contract.EndsAt)
                        .SetProperty(x => x.Reason, "Hợp đồng ký gửi hết hạn"), ct);
                if (Handled(slot, item)) RecordHandover(product, process, actor.Id, now);
            }
            if (DonationHandover(slot)) AddReceipt(contract, process, slot, actor.Id, now);
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return 10;
    }

    private async Task VerifyUntouchedAsync(Guid[] contractIds, Guid[] productIds, CancellationToken ct)
    {
        var processIds = Enumerable.Range(1, 20).Select(n => Id(2, n)).ToArray();
        if (await db.Products.CountAsync(x => productIds.Contains(x.Id) && x.Status == ProductStatus.Expired &&
                EF.Functions.Like(x.Barcode, "DEMO-EXP-%"), ct) != 40 ||
            await db.ExpiryProcessings.CountAsync(x => processIds.Contains(x.Id) &&
                x.Status == ExpiryProcessingStatus.Pending && x.ConfirmedAt == null &&
                x.UnclaimedApprovedAt == null, ct) != 20 ||
            await db.ExpiryProcessingItems.CountAsync(x => processIds.Contains(x.ExpiryProcessingId), ct) != 40 ||
            await db.ProductStatusHistories.CountAsync(x => productIds.Contains(x.ProductId) &&
                x.PreviousStatus == ProductStatus.OnSale && x.NewStatus == ProductStatus.Expired, ct) != 40 ||
            await db.ExpiryProcessingItems.AnyAsync(x => processIds.Contains(x.ExpiryProcessingId) && x.HandledAt != null, ct) ||
            await db.DonationReceipts.AnyAsync(x => processIds.Contains(x.ExpiryProcessingId), ct) ||
            await db.SaleInvoiceLines.AnyAsync(x => productIds.Contains(x.ProductId), ct) ||
            await db.IncidentReports.AnyAsync(x => productIds.Contains(x.ProductId), ct) ||
            await db.EarlyWithdrawalItems.AnyAsync(x => productIds.Contains(x.ProductId), ct) ||
            await db.PayoutDetails.AnyAsync(x => x.ProductId.HasValue && productIds.Contains(x.ProductId.Value), ct) ||
            await db.Payouts.AnyAsync(x => contractIds.Contains(x.ConsignmentContractId), ct) ||
            await db.ContractDocuments.AnyAsync(x => contractIds.Contains(x.ContractId), ct) ||
            await db.ConsignmentDocuments.AnyAsync(x => contractIds.Contains(x.ContractId), ct) ||
            await db.InventoryEvents.AnyAsync(x => productIds.Contains(x.ProductId), ct) ||
            await db.ProductImages.AnyAsync(x => productIds.Contains(x.ProductId), ct))
            throw new InvalidOperationException("The original samples have been used or changed; no rows were modified.");
    }

    private async Task RemoveExtraAsync(CancellationToken ct)
    {
        var contractIds = Removed.Select(n => Id(1, n)).ToArray();
        var processIds = Removed.Select(n => Id(2, n)).ToArray();
        var productIds = Removed.SelectMany(n => Enumerable.Range((n - 1) * 2 + 1, 2).Select(p => Id(3, p))).ToArray();
        await db.ExpiryProcessingItems.Where(x => processIds.Contains(x.ExpiryProcessingId)).ExecuteDeleteAsync(ct);
        await db.ProductStatusHistories.Where(x => productIds.Contains(x.ProductId)).ExecuteDeleteAsync(ct);
        await db.ExpiryProcessings.Where(x => processIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        await db.Products.Where(x => productIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        await db.ConsignmentContracts.Where(x => contractIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
    }

    private static void FillContract(ConsignmentContract c, int slot, EmployeeProfile actor, DateTimeOffset now)
    {
        c.ContractNumber = ContractNo(slot);
        c.CustomerNameSnapshot = Customers[slot - 1];
        c.CustomerPhoneSnapshot = $"099900{900 + slot:0000}";
        c.CustomerEmailSnapshot = $"khachhang{900 + slot}@example.com";
        c.CustomerIdentitySnapshot = $"00000000{900 + slot:0000}";
        c.CustomerAddressSnapshot = slot % 2 == 0 ? "Quận 3, TP. Hồ Chí Minh" : "Quận Bình Thạnh, TP. Hồ Chí Minh";
        c.BankCodeSnapshot = "VCB";
        c.BankAccountNumberSnapshot = $"000000{900 + slot:0000}";
        c.BankAccountHolderSnapshot = Customers[slot - 1].ToUpperInvariant();
        c.TermsSnapshot = "Hàng hết hạn được xử lý theo phương án giao trả hoặc quyên góp ghi trên hợp đồng.";
        c.TemplateTitleSnapshot = "Hợp đồng ký gửi hàng hóa";
        c.OrganizationNameSnapshot = "OCS";
        c.HeaderTextSnapshot = "Thông tin hàng hóa ký gửi và thời hạn bán";
        c.FooterTextSnapshot = "Biên bản bàn giao được lập khi giao trả hoặc quyên góp thực tế.";
        c.EmployeeNameSnapshot = actor.Account.FullName;
        c.EmployeeTitleSnapshot = string.IsNullOrWhiteSpace(actor.JobTitle) ? "Nhân viên ký gửi" : actor.JobTitle;
        c.EmployeePhoneSnapshot = actor.Account.PhoneNumber;
        c.EndsAt = now.AddDays(slot == 1 ? -2 : slot == 6 ? -3 : -(10 + slot));
        c.SignedAt = c.EndsAt.AddDays(-45);
        c.ExpiryHandling = slot <= 5 || slot is 9 or 10 ? ExpiryHandling.Return : ExpiryHandling.Donate;
        c.ActualItemCount = 2;
        c.UpdatedAt = now;
    }

    private static void FillProcessing(ExpiryProcessing p, int slot, Guid actorId, DateTimeOffset now)
    {
        var converted = slot is 5 or 9 or 10;
        p.Type = slot <= 4 ? ExpiryProcessingType.Return : ExpiryProcessingType.Donate;
        p.Status = slot is 4 or 8 or 10 ? ExpiryProcessingStatus.Completed :
            converted ? ExpiryProcessingStatus.OverdueDonated : ExpiryProcessingStatus.Pending;
        p.CollectionDeadline = slot <= 5 || converted ? now.AddDays(slot == 1 ? 5 : -3) : null;
        p.ProcessedByEmployeeId = slot is 3 or 4 or 7 or 8 or 9 or 10 ? actorId : null;
        p.ConfirmedAt = p.Status == ExpiryProcessingStatus.Completed ? now.AddHours(-6) : null;
        p.UnclaimedApprovedByEmployeeId = converted ? actorId : null;
        p.UnclaimedApprovedAt = converted ? now.AddDays(-2) : null;
        p.UnclaimedBasis = converted
            ? "Tình huống kiểm thử: khách không nhận hàng sau hạn; cần đối chiếu căn cứ với hồ sơ thực tế." : null;
        p.RecipientOrganization = DonationHandover(slot) ? "Trung tâm hỗ trợ cộng đồng Ánh Dương" : null;
        p.Evidence = DonationHandover(slot) ? $"Biên bản bàn giao BB-2026-{900 + slot:0000}" : null;
        p.Note = DonationHandover(slot) ? "Đã đối chiếu số lượng và tình trạng sản phẩm khi bàn giao." : null;
        p.UpdatedAt = now;
    }

    private static void FillProduct(Product p, int slot, int item, Category category, DateTimeOffset now)
    {
        var name = Names[(slot - 1) * 2 + item - 1];
        p.Barcode = Barcode(slot, item);
        p.Name = name;
        p.Size = item == 1 ? "M" : "L";
        p.Material = Materials[slot - 1];
        p.Description = $"{name}, tình trạng tốt, đã kiểm tra đường may và phụ kiện.";
        p.ConditionPercentage = 88 + (slot + item) % 10;
        p.CategoryId = category.Id;
        p.ApprovalStatus = ProductApprovalStatus.Approved;
        p.AgreementEvidence = $"Phiếu thỏa thuận giá {ContractNo(slot)}";
        p.UpdatedAt = now;
    }

    private void RecordHandover(Product product, ExpiryProcessing process, Guid actorId, DateTimeOffset now)
    {
        var donated = process.Type == ExpiryProcessingType.Donate;
        product.Status = donated ? ProductStatus.Donated : ProductStatus.Returned;
        product.InventoryVersion = Guid.NewGuid();
        var line = process.Items.Single(x => x.ProductId == product.Id);
        line.HandledByEmployeeId = actorId;
        line.HandledAt = now.AddHours(-6);
        line.RecipientOrganization = donated ? process.RecipientOrganization : null;
        line.Evidence = donated ? process.Evidence : null;
        line.Note = donated ? "Đã bàn giao tại điểm tiếp nhận." : "Khách đã kiểm tra và nhận lại hàng.";
        db.ProductStatusHistories.Add(new ProductStatusHistory
        {
            ProductId = product.Id,
            PreviousStatus = ProductStatus.Expired,
            NewStatus = product.Status,
            OccurredAt = line.HandledAt.Value,
            ChangedByAccountId = actorId,
            Reason = donated ? $"Bàn giao quyên góp theo {process.Evidence}" : "Bàn giao trả người ký gửi"
        });
    }

    private void AddReceipt(ConsignmentContract contract, ExpiryProcessing process, int slot, Guid actorId, DateTimeOffset now)
    {
        db.DonationReceipts.Add(new DonationReceipt
        {
            Id = Id(5, slot),
            ExpiryProcessingId = process.Id,
            ReceiptNumber = $"QG-2026-{900 + slot:0000}",
            ContractNumber = contract.ContractNumber,
            CustomerName = contract.CustomerNameSnapshot,
            RecipientOrganization = process.RecipientOrganization!,
            Evidence = process.Evidence!,
            Note = process.Note,
            HandledByEmployeeId = actorId,
            HandledAt = now.AddHours(-6),
            ProductsJson = JsonSerializer.Serialize(contract.Products.Where(p => p.Status == ProductStatus.Donated)
                .OrderBy(p => p.Barcode).Select(p =>
                    new DonationReceiptProductDto(p.Id, p.Barcode, p.Name, p.ListedPrice)).ToArray())
        });
    }
}
