using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OCS.Api.Common;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Models.Incidents;

namespace OCS.Api.Services;

public sealed class IncidentService(AppDbContext db, TimeProvider clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private IQueryable<IncidentReport> Details() => db.IncidentReports
        .Include(x => x.Product).ThenInclude(x => x.ConsignmentContract)
        .Include(x => x.Product).ThenInclude(x => x.Images)
        .Include(x => x.ApprovedByEmployee).ThenInclude(x => x!.Account);

    public async Task<Result<IncidentProductLookup>> FindProductAsync(string barcode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(barcode) || barcode.Trim().Length > 100)
            return Invalid<IncidentProductLookup>("INCIDENT.BARCODE", "Mã sản phẩm không hợp lệ.");
        var value = barcode.Trim();
        var product = await db.Products.AsNoTracking()
            .Include(x => x.ConsignmentContract).Include(x => x.Images)
            .SingleOrDefaultAsync(x => x.Barcode == value, ct);
        if (product is null || product.SourceType != ProductSourceType.Consignment)
            return NotFound<IncidentProductLookup>("Không tìm thấy sản phẩm ký gửi theo barcode này.");
        return Result.Success(new IncidentProductLookup(product.Id, product.Barcode, product.Name,
            product.Status.ToString(), product.AgreedListedPrice ?? product.ListedPrice,
            product.ConsignmentContract?.ContractNumber, product.ConsignmentContract?.CustomerNameSnapshot,
            product.Images.OrderBy(x => x.SortOrder).Select(x => x.Url).ToArray()));
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1304", Justification = "EF Core translates ToLower into SQL LOWER.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1311", Justification = "EF Core translates ToLower into SQL LOWER.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1862", Justification = "StringComparison overloads are not translated by EF Core PostgreSQL.")]
    public async Task<Result<PagedResponse<IncidentResponse>>> ListAsync(IncidentQuery query, CancellationToken ct)
    {
        if (query.Page < 1 || query.PageSize is < 1 or > 100 || (long)(query.Page - 1) * query.PageSize > int.MaxValue)
            return Invalid<PagedResponse<IncidentResponse>>("INCIDENT.PAGING", "Phân trang không hợp lệ.");
        IQueryable<IncidentReport> rows = Details().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!Enum.TryParse<IncidentStatus>(query.Status, true, out var status) || !Enum.IsDefined(status))
                return Invalid<PagedResponse<IncidentResponse>>("INCIDENT.STATUS", "Trạng thái biên bản không hợp lệ.");
            rows = rows.Where(x => x.Status == status);
        }
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var q = query.Q.Trim().ToLowerInvariant();
            rows = rows.Where(x => x.Product.Barcode.ToLower().Contains(q) || x.Product.Name.ToLower().Contains(q) ||
                x.Product.ConsignmentContract!.ContractNumber.ToLower().Contains(q) ||
                (x.Product.ConsignmentContract!.CustomerNameSnapshot != null && x.Product.ConsignmentContract.CustomerNameSnapshot.ToLower().Contains(q)));
        }
        var count = await rows.CountAsync(ct);
        var items = await rows.OrderBy(x => x.Status).ThenByDescending(x => x.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).AsSplitQuery().ToListAsync(ct);
        return Result.Success(new PagedResponse<IncidentResponse>(items.Select(item => Map(item)).ToArray(), query.Page, query.PageSize, count));
    }

    public async Task<Result<IncidentResponse>> DetailAsync(Guid id, CancellationToken ct)
    {
        var report = await Details().AsNoTracking().AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (report is null) return NotFound<IncidentResponse>("Không tìm thấy biên bản sự cố.");
        var idText = id.ToString();
        var history = await db.AuditLogs.AsNoTracking().Where(x => x.EntityType == nameof(IncidentReport) && x.EntityId == idText)
            .OrderBy(x => x.OccurredAt).Select(x => new IncidentAuditEvent(x.Id, x.Action,
                x.ActorAccountId == null ? null : x.ActorAccountId.ToString(), x.ActorType.ToString(), x.Reason, x.Changes, x.OccurredAt))
            .ToArrayAsync(ct);
        return Result.Success(Map(report, history: history));
    }

    public async Task<Result<IncidentResponse>> CreateAsync(Guid actor, CreateIncidentRequest request, CancellationToken ct)
    {
        if (request.ProductId == Guid.Empty || !Enum.IsDefined(request.Type) ||
            string.IsNullOrWhiteSpace(request.Description) || request.Description.Trim().Length > 2000)
            return Invalid<IncidentResponse>("INCIDENT.REQUEST", "Thông tin biên bản chưa hợp lệ.");
        var evidence = NormalizeEvidence(request.EvidenceUrls);
        if (evidence is null || evidence.Length == 0)
            return Invalid<IncidentResponse>("INCIDENT.EVIDENCE", "Cần tải lên ít nhất một ảnh chứng cứ hợp lệ.");

        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var product = await db.Products.Include(x => x.ConsignmentContract).SingleOrDefaultAsync(x => x.Id == request.ProductId, ct);
        if (product is null || product.SourceType != ProductSourceType.Consignment || product.ConsignmentContractId is null)
            return NotFound<IncidentResponse>("Chỉ lập biên bản cho sản phẩm ký gửi.");
        if (product.Status != ProductStatus.OnSale)
            return Conflict<IncidentResponse>("Sản phẩm không còn ở trạng thái đang bán.");
        if (await db.IncidentReports.AnyAsync(x => x.ProductId == product.Id, ct))
            return Conflict<IncidentResponse>("Sản phẩm đã có biên bản sự cố.");

        var now = clock.GetUtcNow();
        var report = new IncidentReport
        {
            ProductId = product.Id, CreatedByEmployeeId = actor, Type = request.Type,
            Description = request.Description.Trim(), EvidenceUrls = JsonSerializer.Serialize(evidence, JsonOptions),
            Status = IncidentStatus.Pending, CreatedAt = now
        };
        product.Status = ProductStatus.IncidentPending;
        product.InventoryVersion = Guid.NewGuid();
        product.UpdatedAt = now;
        db.IncidentReports.Add(report);
        db.ProductStatusHistories.Add(new ProductStatusHistory
        {
            ProductId = product.Id, PreviousStatus = ProductStatus.OnSale, NewStatus = ProductStatus.IncidentPending,
            ChangedByAccountId = actor, OccurredAt = now, Reason = "Tạm giữ sản phẩm trong khi thẩm định biên bản sự cố"
        });
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return Result.Success(Map(report, product));
    }

    public async Task<Result<IncidentResponse>> ResubmitAsync(Guid id, Guid actor, ResubmitIncidentRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Trim().Length > 2000)
            return Invalid<IncidentResponse>("INCIDENT.DESCRIPTION", "Mô tả sự cố không hợp lệ.");
        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var report = await Details().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (report is null) return NotFound<IncidentResponse>("Không tìm thấy biên bản sự cố.");
        if (report.Status != IncidentStatus.NeedsRecheck) return Conflict<IncidentResponse>("Biên bản không chờ bổ sung.");
        var existing = ReadEvidence(report.EvidenceUrls);
        var added = request.EvidenceUrls is null ? [] : NormalizeEvidence(request.EvidenceUrls);
        if (added is null) return Invalid<IncidentResponse>("INCIDENT.EVIDENCE", "Đường dẫn ảnh chứng cứ không hợp lệ.");
        var evidence = existing.Concat(added).Distinct(StringComparer.Ordinal).ToArray();
        if (evidence.Length > 10) return Invalid<IncidentResponse>("INCIDENT.EVIDENCE_LIMIT", "Tối đa 10 ảnh cho mỗi biên bản.");
        report.Description = request.Description.Trim();
        report.EvidenceUrls = JsonSerializer.Serialize(evidence, JsonOptions);
        report.Status = IncidentStatus.Pending;
        report.ReviewNote = null;
        report.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return Result.Success(Map(report));
    }

    public async Task<Result<IncidentResponse>> ApproveAsync(Guid id, Guid actor, ApproveIncidentRequest request, CancellationToken ct)
    {
        if (request.CompensationAmount <= 0 || request.Note?.Length > 1000)
            return Invalid<IncidentResponse>("INCIDENT.COMPENSATION", "Số tiền bồi thường hoặc ghi chú không hợp lệ.");
        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var report = await Details().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (report is null) return NotFound<IncidentResponse>("Không tìm thấy biên bản sự cố.");
        if (report.Status != IncidentStatus.Pending) return Conflict<IncidentResponse>("Biên bản không còn chờ duyệt.");
        var agreedPrice = report.Product.AgreedListedPrice ?? report.Product.ListedPrice;
        if (request.CompensationAmount > agreedPrice)
            return Invalid<IncidentResponse>("INCIDENT.COMPENSATION_CAP", "Mức bồi thường không được vượt giá đã thỏa thuận.");
        if (request.CompensationAmount < agreedPrice && string.IsNullOrWhiteSpace(request.Note))
            return Invalid<IncidentResponse>("INCIDENT.COMPENSATION_REASON", "Cần ghi lý do khi bồi thường thấp hơn giá đã thỏa thuận.");

        var now = clock.GetUtcNow();
        report.Status = IncidentStatus.Approved;
        report.CompensationAmount = request.CompensationAmount;
        report.ApprovedByEmployeeId = actor;
        report.ApprovedAt = now;
        report.ReviewNote = request.Note?.Trim();
        report.Product.Status = ProductStatus.Compensated;
        report.Product.InventoryVersion = Guid.NewGuid();
        report.Product.UpdatedAt = now;
        db.ProductStatusHistories.Add(new ProductStatusHistory
        {
            ProductId = report.ProductId, PreviousStatus = ProductStatus.IncidentPending,
            NewStatus = ProductStatus.Compensated, ChangedByAccountId = actor, OccurredAt = now,
            Reason = $"Đã duyệt bồi thường {request.CompensationAmount:N2}"
        });
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return Result.Success(Map(report));
    }

    public async Task<Result<IncidentResponse>> RequestRecheckAsync(Guid id, Guid actor, RequestIncidentRecheckRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000)
            return Invalid<IncidentResponse>("INCIDENT.RECHECK_REASON", "Cần ghi lý do yêu cầu kiểm tra lại.");
        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var report = await Details().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (report is null) return NotFound<IncidentResponse>("Không tìm thấy biên bản sự cố.");
        if (report.Status != IncidentStatus.Pending) return Conflict<IncidentResponse>("Biên bản không còn chờ duyệt.");
        report.Status = IncidentStatus.NeedsRecheck;
        report.ReviewNote = request.Reason.Trim();
        report.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return Result.Success(Map(report));
    }

    private static string[]? NormalizeEvidence(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count > 10) return null;
        var normalized = new List<string>();
        foreach (var raw in values)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw.Length > 2048) return null;
            var value = raw.Trim();
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
                value = uri.AbsolutePath;
            if (!value.StartsWith("/api/storage/inspections/", StringComparison.OrdinalIgnoreCase)) return null;
            var key = value["/api/storage/inspections/".Length..];
            if (string.IsNullOrWhiteSpace(key) || key.Contains('\\') || key.Split('/').Any(part => part is "" or "." or "..") ||
                value.Contains('?') || value.Contains('#')) return null;
            normalized.Add(value);
        }
        return normalized.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string[] ReadEvidence(string values)
    {
        try { return JsonSerializer.Deserialize<string[]>(values, JsonOptions) ?? []; }
        catch (JsonException) { return []; }
    }

    private static IncidentResponse Map(IncidentReport report, Product? product = null, IReadOnlyList<IncidentAuditEvent>? history = null)
    {
        product ??= report.Product;
        return new IncidentResponse(report.Id, report.ProductId, product.Barcode, product.Name,
            product.ConsignmentContract?.ContractNumber, product.ConsignmentContract?.CustomerNameSnapshot,
            report.Type.ToString(), report.Status.ToString(), report.Description, ReadEvidence(report.EvidenceUrls),
            product.AgreedListedPrice ?? product.ListedPrice, report.CompensationAmount,
            report.CreatedByEmployeeId, report.ApprovedByEmployeeId, report.ApprovedByEmployee?.Account.FullName, report.CreatedAt,
            report.ApprovedAt, report.ReviewNote, history);
    }

    private static Result<T> Invalid<T>(string code, string message) => Result.Failure<T>(ApplicationError.Validation(code, message));
    private static Result<T> NotFound<T>(string message) => Result.Failure<T>(ApplicationError.NotFound("INCIDENT.NOT_FOUND", message));
    private static Result<T> Conflict<T>(string message) => Result.Failure<T>(ApplicationError.Conflict("INCIDENT.STATE", message));
}
