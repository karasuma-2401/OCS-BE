using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OCS.Api.Common;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Models.Products;

namespace OCS.Api.Services;

public sealed class ExpiryProcessingService(AppDbContext db, TimeProvider clock,
    IConfiguration configuration)
{
    private IQueryable<ExpiryProcessing> Details() => db.ExpiryProcessings
        .Include(x => x.Items).ThenInclude(x => x.Product)
        .ThenInclude(x => x.ConsignmentContract);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1304", Justification = "EF Core translates ToLower into database LOWER.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1311", Justification = "EF Core translates ToLower into database LOWER.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1862", Justification = "StringComparison overloads are not translated by EF Core PostgreSQL.")]
    public async Task<Result<PagedResponse<ExpiryDto>>> ListAsync(ExpiryQuery filter, CancellationToken ct)
    {
        if (filter.Page < 1 || filter.PageSize is < 1 or > 100 ||
            (long)(filter.Page - 1) * filter.PageSize > int.MaxValue)
            return Result.Failure<PagedResponse<ExpiryDto>>(ApplicationError.Validation("EXPIRY.PAGING", "Phân trang không hợp lệ."));
        if (filter.CustomerId == Guid.Empty || filter.CustomerQuery?.Length > 200)
            return Result.Failure<PagedResponse<ExpiryDto>>(ApplicationError.Validation("EXPIRY.CUSTOMER_FILTER", "Bộ lọc khách hàng không hợp lệ."));
        IQueryable<ExpiryProcessing> query = Details().AsNoTracking().Where(x => x.Items.Any());
        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            if (!Enum.TryParse<ExpiryProcessingStatus>(filter.Status, true, out var status) || !Enum.IsDefined(status))
                return Result.Failure<PagedResponse<ExpiryDto>>(ApplicationError.Validation("EXPIRY.STATUS", "Trạng thái không hợp lệ."));
            query = query.Where(x => x.Status == status);
        }
        if (!string.IsNullOrWhiteSpace(filter.Type))
        {
            if (!Enum.TryParse<ExpiryProcessingType>(filter.Type, true, out var type) || !Enum.IsDefined(type))
                return Result.Failure<PagedResponse<ExpiryDto>>(ApplicationError.Validation("EXPIRY.TYPE", "Hình thức xử lý không hợp lệ."));
            query = query.Where(x => x.Type == type);
        }
        if (filter.DeadlineFrom.HasValue) query = query.Where(x => x.CollectionDeadline >= filter.DeadlineFrom);
        if (filter.DeadlineTo.HasValue) query = query.Where(x => x.CollectionDeadline <= filter.DeadlineTo);
        if (filter.CustomerId.HasValue)
            query = query.Where(x => x.Items.Any(i => i.Product.ConsignmentContract!.CustomerId == filter.CustomerId));
        if (!string.IsNullOrWhiteSpace(filter.CustomerQuery))
        {
            var customerQuery = filter.CustomerQuery.Trim().ToLowerInvariant();
            query = query.Where(x => x.Items.Any(i =>
                (i.Product.ConsignmentContract!.CustomerNameSnapshot != null &&
                    i.Product.ConsignmentContract.CustomerNameSnapshot.ToLower().Contains(customerQuery)) ||
                (i.Product.ConsignmentContract!.CustomerPhoneSnapshot != null &&
                    i.Product.ConsignmentContract.CustomerPhoneSnapshot.Contains(customerQuery))));
        }
        if (!string.IsNullOrWhiteSpace(filter.Q))
        {
            var q = filter.Q.Trim().ToLowerInvariant();
            query = query.Where(x => x.Items.Any(i =>
                i.Product.Barcode.ToLower().Contains(q) || i.Product.Name.ToLower().Contains(q) ||
                i.Product.ConsignmentContract!.ContractNumber.ToLower().Contains(q)));
        }
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Status).ThenBy(x => x.CollectionDeadline)
            .ThenByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize).AsSplitQuery().ToListAsync(ct);
        return Result.Success(new PagedResponse<ExpiryDto>(rows.Select(Map).ToArray(), filter.Page, filter.PageSize, count));
    }

    public async Task<Result<ExpiryDto>> DetailAsync(Guid id, CancellationToken ct)
    {
        var item = await Details().AsNoTracking().AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, ct);
        return item is null || item.Items.Count == 0 ? Missing() : Result.Success(Map(item));
    }

    public async Task<Result<IReadOnlyList<DonationReceiptDto>>> ReceiptsAsync(Guid id, CancellationToken ct)
    {
        if (!await db.ExpiryProcessings.AsNoTracking().AnyAsync(x => x.Id == id && x.Items.Any(), ct))
            return Result.Failure<IReadOnlyList<DonationReceiptDto>>(ApplicationError.NotFound("EXPIRY.NOT_FOUND", "Không tìm thấy phiếu xử lý."));
        var receipts = await db.DonationReceipts.AsNoTracking().Where(x => x.ExpiryProcessingId == id)
            .OrderBy(x => x.HandledAt).ThenBy(x => x.Id).ToListAsync(ct);
        return Result.Success<IReadOnlyList<DonationReceiptDto>>(receipts.Select(MapReceipt).ToArray());
    }

    public async Task<Result<DonationReceiptDto>> ReceiptAsync(Guid id, Guid receiptId, CancellationToken ct)
    {
        var receipt = await db.DonationReceipts.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == receiptId && x.ExpiryProcessingId == id, ct);
        return receipt is null
            ? Result.Failure<DonationReceiptDto>(ApplicationError.NotFound("EXPIRY.RECEIPT_NOT_FOUND", "Không tìm thấy biên nhận quyên góp."))
            : Result.Success(MapReceipt(receipt));
    }

    public Task<Result<ExpiryDto>> ConfirmReturnAsync(Guid id, Guid actor, ConfirmReturnRequest request, CancellationToken ct) =>
        ConfirmAsync(id, actor, request.Barcodes, false, null, null, null, ct);

    public Task<Result<ExpiryDto>> ConfirmDonationAsync(Guid id, Guid actor, ConfirmDonationRequest request, CancellationToken ct) =>
        ConfirmAsync(id, actor, request.Barcodes, true, request.RecipientOrganization, request.Evidence, request.Note, ct);

    private async Task<Result<ExpiryDto>> ConfirmAsync(Guid id, Guid actor, IReadOnlyList<string>? barcodes,
        bool donate, string? organization, string? evidence, string? note, CancellationToken ct)
    {
        if (barcodes is null || barcodes.Count == 0 || barcodes.Any(string.IsNullOrWhiteSpace) ||
            barcodes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != barcodes.Count)
            return Invalid("EXPIRY.BARCODES", "Cần quét ít nhất một barcode, không được trùng.");
        if (donate && (string.IsNullOrWhiteSpace(organization) || organization.Length > 200 ||
            string.IsNullOrWhiteSpace(evidence) || evidence.Length > 1000 || note?.Length > 1000))
            return Invalid("EXPIRY.DONATION_EVIDENCE", "Cần tên tổ chức nhận và bằng chứng bàn giao hợp lệ.");
        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var item = await Details().AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return Missing();
        if (item.Type != (donate ? ExpiryProcessingType.Donate : ExpiryProcessingType.Return) ||
            item.Status is not ExpiryProcessingStatus.Pending and not ExpiryProcessingStatus.OverdueDonated)
            return Conflict("Phiếu không còn chờ xử lý theo hình thức này.");
        var selected = item.Items.Where(x => barcodes.Contains(x.Product.Barcode, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (selected.Length != barcodes.Count || selected.Any(x => x.Product.Status != ProductStatus.Expired))
            return Conflict("Barcode không thuộc phiếu hoặc sản phẩm đã được xử lý.");
        var now = clock.GetUtcNow();
        foreach (var line in selected)
        {
            line.HandledByEmployeeId = actor;
            line.HandledAt = now;
            line.RecipientOrganization = donate ? organization!.Trim() : null;
            line.Evidence = donate ? evidence!.Trim() : null;
            line.Note = note?.Trim();
            line.Product.Status = donate ? ProductStatus.Donated : ProductStatus.Returned;
            line.Product.InventoryVersion = Guid.NewGuid();
            line.Product.UpdatedAt = now;
            db.ProductStatusHistories.Add(new ProductStatusHistory
            {
                ProductId = line.ProductId, PreviousStatus = ProductStatus.Expired,
                NewStatus = line.Product.Status, ChangedByAccountId = actor, OccurredAt = now,
                Reason = donate ? $"Bàn giao quyên góp cho {organization!.Trim()}" : "Bàn giao trả khách ký gửi"
            });
        }
        item.ProcessedByEmployeeId = actor;
        item.UpdatedAt = now;
        if (donate)
        {
            item.RecipientOrganization = organization!.Trim();
            item.Evidence = evidence!.Trim();
            item.Note = note?.Trim();
            var contract = selected[0].Product.ConsignmentContract!;
            var receipt = new DonationReceipt
            {
                ExpiryProcessingId = item.Id,
                ContractNumber = contract.ContractNumber,
                CustomerName = contract.CustomerNameSnapshot,
                RecipientOrganization = organization.Trim(),
                Evidence = evidence.Trim(),
                Note = note?.Trim(),
                HandledByEmployeeId = actor,
                HandledAt = now,
                ProductsJson = JsonSerializer.Serialize(selected.Select(x =>
                    new DonationReceiptProductDto(x.ProductId, x.Product.Barcode, x.Product.Name,
                        x.Product.ListedPrice)).ToArray())
            };
            receipt.ReceiptNumber = $"QG-{receipt.Id:N}";
            db.DonationReceipts.Add(receipt);
        }
        if (item.Items.All(x => x.Product.Status is ProductStatus.Returned or ProductStatus.Donated))
        {
            item.Status = ExpiryProcessingStatus.Completed;
            item.ConfirmedAt = now;
            if (donate) AddNotification(item, "Cảm ơn bạn đã quyên góp", "Sản phẩm ký gửi của bạn đã được bàn giao quyên góp.", "donated");
        }
        try
        {
            await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "40001" or "23505" })
        {
            return Conflict("Sản phẩm vừa được xử lý ở nơi khác. Vui lòng tải lại phiếu.");
        }
        catch (PostgresException ex) when (ex.SqlState is "40001" or "23505")
        {
            return Conflict("Sản phẩm vừa được xử lý ở nơi khác. Vui lòng tải lại phiếu.");
        }
        return Result.Success(Map(item));
    }

    public async Task<Result<ExpiryDto>> MarkUnclaimedAsync(Guid id, Guid actor, MarkUnclaimedRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Basis) || request.Basis.Length > 1000)
            return Invalid("EXPIRY.BASIS", "Cần ghi căn cứ khách từ bỏ quyền sở hữu.");
        if (!bool.TryParse(configuration["ExpiryProcessing:AllowUnclaimedDonation"], out var enabled) || !enabled)
            return Conflict("Chính sách chuyển hàng không nhận sang quyên góp chưa được cấu hình.");
        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var item = await Details().AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return Missing();
        if (item.Type != ExpiryProcessingType.Return || item.Status != ExpiryProcessingStatus.Pending ||
            item.Items.All(x => x.Product.Status != ProductStatus.Expired))
            return Conflict("Phiếu không còn hàng chờ khách nhận.");
        var now = clock.GetUtcNow();
        if (!item.CollectionDeadline.HasValue || now < item.CollectionDeadline.Value)
            return Conflict("Chưa có hạn nhận hàng hợp lệ hoặc chưa quá hạn nhận.");
        item.Type = ExpiryProcessingType.Donate;
        item.Status = ExpiryProcessingStatus.OverdueDonated;
        item.UnclaimedApprovedByEmployeeId = actor;
        item.UnclaimedApprovedAt = now;
        item.UnclaimedBasis = request.Basis.Trim();
        item.UpdatedAt = now;
        try
        {
            await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "40001" or "23505" })
        {
            return Conflict("Phiếu vừa được cập nhật ở nơi khác. Vui lòng tải lại.");
        }
        catch (PostgresException ex) when (ex.SqlState is "40001" or "23505")
        {
            return Conflict("Phiếu vừa được cập nhật ở nơi khác. Vui lòng tải lại.");
        }
        return Result.Success(Map(item));
    }

    public async Task<int> ExpireAsync(CancellationToken ct)
    {
        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var now = clock.GetUtcNow();
        var undated = int.TryParse(configuration["ExpiryProcessing:CollectionDays"], out var collectionDays) && collectionDays is > 0 and <= 365
            ? await db.ExpiryProcessings.Include(x => x.Items)
                .ThenInclude(x => x.Product).ThenInclude(x => x.ConsignmentContract)
                .Where(x => x.Type == ExpiryProcessingType.Return && x.Status == ExpiryProcessingStatus.Pending &&
                    x.CollectionDeadline == null && x.Items.Any()).ToListAsync(ct)
            : [];
        var deadlinesUpdated = false;
        foreach (var processing in undated)
        {
            var endsAt = processing.Items.First().Product.ConsignmentContract?.EndsAt;
            if (endsAt is not null && GetDeadline(endsAt.Value) is { } deadline)
            {
                processing.CollectionDeadline = deadline;
                processing.UpdatedAt = now;
                deadlinesUpdated = true;
            }
        }
        var products = await db.Products.Include(x => x.ConsignmentContract)
            .Where(x => x.Status == ProductStatus.OnSale && x.ConsignmentContractId != null &&
                x.ConsignmentContract!.EndsAt <= now).ToListAsync(ct);
        if (products.Count == 0)
        {
            if (deadlinesUpdated)
            {
                await db.SaveChangesAsync(ct);
                if (tx is not null) await tx.CommitAsync(ct);
            }
            return 0;
        }
        var contractIds = products.Select(x => x.ConsignmentContractId!.Value).Distinct().ToArray();
        var existing = await db.ExpiryProcessings.Include(x => x.Items).ThenInclude(x => x.Product)
            .Where(x => x.Items.Any(i => i.Product.ConsignmentContractId != null &&
                contractIds.Contains(i.Product.ConsignmentContractId.Value))).ToListAsync(ct);
        foreach (var group in products.GroupBy(x => x.ConsignmentContractId!.Value))
        {
            var contract = group.First().ConsignmentContract!;
            var processing = existing.FirstOrDefault(x => x.Items.Any(i => i.Product.ConsignmentContractId == group.Key));
            if (processing is null)
            {
                processing = new ExpiryProcessing
                {
                    Type = contract.ExpiryHandling == ExpiryHandling.Donate ? ExpiryProcessingType.Donate : ExpiryProcessingType.Return,
                    Status = ExpiryProcessingStatus.Pending,
                    CollectionDeadline = GetDeadline(contract.EndsAt)
                };
                db.ExpiryProcessings.Add(processing);
                if (contract.ExpiryHandling == ExpiryHandling.Return)
                    AddNotification(processing, "Hàng ký gửi đã hết hạn", "Sản phẩm ký gửi của bạn đã hết hạn. Vui lòng liên hệ cửa hàng để nhận lại.", "expired", contract);
            }
            foreach (var product in group)
            {
                product.Status = ProductStatus.Expired;
                product.InventoryVersion = Guid.NewGuid();
                product.UpdatedAt = now;
                if (processing.Items.All(x => x.ProductId != product.Id))
                    processing.Items.Add(new ExpiryProcessingItem { ProductId = product.Id, Product = product });
                db.ProductStatusHistories.Add(new ProductStatusHistory
                {
                    ProductId = product.Id, PreviousStatus = ProductStatus.OnSale,
                    NewStatus = ProductStatus.Expired, OccurredAt = now, Reason = "Hợp đồng ký gửi hết hạn"
                });
            }
        }
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return products.Count;
    }

    private DateTimeOffset? GetDeadline(DateTimeOffset endsAt) =>
        int.TryParse(configuration["ExpiryProcessing:CollectionDays"], out var days) && days is > 0 and <= 365
            ? endsAt.AddDays(days) : null;

    private void AddNotification(ExpiryProcessing processing, string title, string content, string key,
        ConsignmentContract? contract = null)
    {
        contract ??= processing.Items.FirstOrDefault()?.Product.ConsignmentContract;
        if (contract?.CustomerId is not Guid recipient) return;
        db.Notifications.Add(new Notification
        {
            Id = DeterministicId(processing.Id, key), RecipientAccountId = recipient,
            Title = title, Content = content, Type = NotificationType.System,
            SentAt = clock.GetUtcNow(), CreatedAt = clock.GetUtcNow()
        });
    }

    private static Guid DeterministicId(Guid id, string key)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"expiry:{id}:{key}"));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static ExpiryDto Map(ExpiryProcessing processing)
    {
        var contract = processing.Items.First().Product.ConsignmentContract!;
        return new ExpiryDto(processing.Id, contract.Id, contract.ContractNumber,
            contract.CustomerNameSnapshot, contract.CustomerPhoneSnapshot, contract.EndsAt,
            processing.Type.ToString(), processing.Status.ToString(), processing.CollectionDeadline,
            processing.RecipientOrganization, processing.Evidence, processing.Note,
            processing.ConfirmedAt, processing.UnclaimedApprovedAt, processing.UnclaimedBasis,
            processing.Items.Select(x => new ExpiryProductDto(x.ProductId, x.Product.Barcode,
                x.Product.Name, x.Product.Status.ToString(), x.Product.ListedPrice,
                x.HandledByEmployeeId, x.HandledAt, x.RecipientOrganization, x.Evidence, x.Note)).ToArray());
    }

    private static DonationReceiptDto MapReceipt(DonationReceipt receipt) => new(
        receipt.Id, receipt.ReceiptNumber, receipt.ExpiryProcessingId, receipt.ContractNumber,
        receipt.CustomerName, receipt.RecipientOrganization, receipt.Evidence, receipt.Note,
        receipt.HandledByEmployeeId, receipt.HandledAt,
        JsonSerializer.Deserialize<DonationReceiptProductDto[]>(receipt.ProductsJson) ?? []);

    private static Result<ExpiryDto> Missing() => Result.Failure<ExpiryDto>(ApplicationError.NotFound("EXPIRY.NOT_FOUND", "Không tìm thấy phiếu xử lý."));
    private static Result<ExpiryDto> Invalid(string code, string message) => Result.Failure<ExpiryDto>(ApplicationError.Validation(code, message));
    private static Result<ExpiryDto> Conflict(string message) => Result.Failure<ExpiryDto>(ApplicationError.Conflict("EXPIRY.CONFLICT", message));
}
