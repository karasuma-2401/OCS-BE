using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OCS.Api.Common;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Models.Products;

namespace OCS.Api.Services;

public sealed class InventoryService(AppDbContext db)
{
    private IQueryable<Product> Products() => db.Products.Include(x => x.Brand).Include(x => x.Category)
        .Include(x => x.StorageLocation).Include(x => x.ConsignmentContract).Include(x => x.Buyout).Include(x => x.Images);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1304", Justification = "EF Core translates ToLower to database LOWER using the database collation.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1311", Justification = "EF Core translates ToLower to database LOWER using the database collation.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1862", Justification = "StringComparison overloads are not translated by EF Core PostgreSQL.")]
    public async Task<Result<PagedResponse<InventoryProductDto>>> ListAsync(InventoryQuery request, CancellationToken ct)
    {
        if (request.Page < 1 || request.PageSize is < 1 or > 100 || (long)(request.Page - 1) * request.PageSize > int.MaxValue)
            return Result.Failure<PagedResponse<InventoryProductDto>>(ApplicationError.Validation("INVENTORY.PAGING", "Thông tin phân trang không hợp lệ."));
        var query = Products().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var keyword = request.Q.Trim().ToLowerInvariant();
            query = query.Where(x =>
                x.Barcode.ToLower().Contains(keyword) ||
                x.Name.ToLower().Contains(keyword) ||
                x.Brand.Name.ToLower().Contains(keyword));
        }
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!Enum.TryParse<ProductStatus>(request.Status, true, out var status) || !Enum.IsDefined(status))
                return Result.Failure<PagedResponse<InventoryProductDto>>(ApplicationError.Validation("INVENTORY.STATUS", "Trạng thái không hợp lệ."));
            query = query.Where(x => x.Status == status);
        }
        if (!string.IsNullOrWhiteSpace(request.Approval))
        {
            if (!Enum.TryParse<ProductApprovalStatus>(request.Approval, true, out var approval) || !Enum.IsDefined(approval))
                return Result.Failure<PagedResponse<InventoryProductDto>>(ApplicationError.Validation("INVENTORY.APPROVAL", "Trạng thái duyệt không hợp lệ."));
            query = query.Where(x => x.ApprovalStatus == approval);
        }
        if (!string.IsNullOrWhiteSpace(request.SourceType))
        {
            if (!Enum.TryParse<ProductSourceType>(request.SourceType, true, out var sourceType) || !Enum.IsDefined(sourceType))
                return Result.Failure<PagedResponse<InventoryProductDto>>(ApplicationError.Validation("INVENTORY.SOURCE_TYPE", "Nguồn hàng không hợp lệ."));
            query = query.Where(x => x.SourceType == sourceType);
        }
        if (request.UnassignedLocation == true)
            query = query.Where(x => x.StorageLocationId == null);
        else if (request.LocationId.HasValue)
            query = query.Where(x => x.StorageLocationId == request.LocationId);

        var count = await query.CountAsync(ct);
        var ordered = (request.Sort?.Trim().ToLowerInvariant()) switch
        {
            "price_asc" => query.OrderBy(x => x.ListedPrice).ThenByDescending(x => x.CreatedAt).ThenBy(x => x.Id),
            "price_desc" => query.OrderByDescending(x => x.ListedPrice).ThenByDescending(x => x.CreatedAt).ThenBy(x => x.Id),
            "condition_desc" => query.OrderByDescending(x => x.ConditionPercentage).ThenByDescending(x => x.CreatedAt).ThenBy(x => x.Id),
            "name_asc" => query.OrderBy(x => x.Name).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id),
        };
        var products = await ordered.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        return Result.Success(new PagedResponse<InventoryProductDto>(products.Select(Map).ToArray(), request.Page, request.PageSize, count));
    }

    public async Task<InventorySummaryDto> SummaryAsync(CancellationToken ct)
    {
        var buckets = await db.Products.AsNoTracking()
            .GroupBy(x => new { x.Status, x.ApprovalStatus, HasLocation = x.StorageLocationId != null })
            .Select(g => new { g.Key.Status, g.Key.ApprovalStatus, g.Key.HasLocation, Count = g.Count() })
            .ToListAsync(ct);

        return new InventorySummaryDto(
            TotalCount: buckets.Sum(x => x.Count),
            PendingApprovalCount: buckets.Where(x => x.Status == ProductStatus.PendingProcessing && x.ApprovalStatus == ProductApprovalStatus.Pending).Sum(x => x.Count),
            ReadyToOfferCount: buckets.Where(x => x.Status == ProductStatus.PendingProcessing && x.ApprovalStatus != ProductApprovalStatus.Rejected).Sum(x => x.Count),
            UnassignedLocationCount: buckets.Where(x => !x.HasLocation && (x.Status == ProductStatus.PendingProcessing || x.Status == ProductStatus.OnSale)).Sum(x => x.Count),
            OnSaleCount: buckets.Where(x => x.Status == ProductStatus.OnSale).Sum(x => x.Count),
            ExpiredCount: buckets.Where(x => x.Status == ProductStatus.Expired).Sum(x => x.Count),
            PendingProcessingCount: buckets.Where(x => x.Status == ProductStatus.PendingProcessing).Sum(x => x.Count));
    }

    public async Task<Result<InventoryDetailDto>> DetailAsync(Guid id, CancellationToken ct)
    {
        var product = await Products().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (product is null) return Missing<InventoryDetailDto>();
        var events = await db.InventoryEvents.AsNoTracking().Where(x => x.ProductId == id && x.Action != "Status")
            .OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id)
            .Select(x => new InventoryEventDto(x.Action, x.PreviousValue, x.NewValue, x.Reason, x.ActorAccountId, x.OccurredAt)).ToListAsync(ct);
        var statusEvents = await db.ProductStatusHistories.AsNoTracking().Where(x => x.ProductId == id)
            .Select(x => new { x.PreviousStatus, x.NewStatus, x.Reason, x.ChangedByAccountId, x.OccurredAt }).ToListAsync(ct);
        events.AddRange(statusEvents.Select(x => new InventoryEventDto("Status", x.PreviousStatus?.ToString(),
            x.NewStatus.ToString(), x.Reason, x.ChangedByAccountId, x.OccurredAt)));
        var actorIds = events.Where(x => x.ActorAccountId.HasValue).Select(x => x.ActorAccountId!.Value).Distinct().ToArray();
        var actorNames = actorIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await db.Accounts.AsNoTracking().Where(x => actorIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
        var resolvedEvents = events.OrderByDescending(x => x.OccurredAt)
            .Select(x => x with { ActorName = x.ActorAccountId.HasValue ? actorNames.GetValueOrDefault(x.ActorAccountId.Value) : null })
            .ToArray();
        var images = product.Images.Where(x => x.ImageType is ProductImageType.Cover or ProductImageType.Detail)
            .OrderBy(x => x.SortOrder).Select(x => new InventoryImageDto(x.Id, x.Url, x.ImageType.ToString(), x.SortOrder)).ToArray();
        return Result.Success(new InventoryDetailDto(Map(product), product.AgreementEvidence, product.AgreedListedPrice, images,
            resolvedEvents, product.PurchasePrice));
    }

    public async Task<List<InventoryLocationDto>> LocationsAsync(CancellationToken ct)
    {
        var locations = await db.StorageLocations.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).ToListAsync(ct);
        var counts = await db.Products.AsNoTracking()
            .Where(x => x.StorageLocationId != null && (x.Status == ProductStatus.PendingProcessing || x.Status == ProductStatus.OnSale))
            .GroupBy(x => x.StorageLocationId!.Value)
            .Select(g => new { LocationId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.LocationId, x => x.Count, ct);
        return locations.Select(x => new InventoryLocationDto(x.Id, x.Code, x.Name, counts.GetValueOrDefault(x.Id, 0))).ToList();
    }

    public async Task<Result<InventoryLocationDto>> CreateLocationAsync(CreateInventoryLocationRequest request, CancellationToken ct)
    {
        var code = request.Code?.Trim(); var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(code) || code.Length > 50 || string.IsNullOrWhiteSpace(name) || name.Length > 200)
            return Result.Failure<InventoryLocationDto>(ApplicationError.Validation("INVENTORY.LOCATION_INVALID", "Nhập mã và tên vị trí hợp lệ."));
        if (await db.StorageLocations.AnyAsync(x => x.Code == code, ct))
            return Result.Failure<InventoryLocationDto>(ApplicationError.Conflict("INVENTORY.LOCATION_EXISTS", "Mã vị trí đã tồn tại."));
        var location = new StorageLocation { Code = code, Name = name };
        db.StorageLocations.Add(location);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        { return Result.Failure<InventoryLocationDto>(ApplicationError.Conflict("INVENTORY.LOCATION_EXISTS", "Mã vị trí đã tồn tại.")); }
        return Result.Success(new InventoryLocationDto(location.Id, location.Code, location.Name));
    }

    public async Task<Result<InventoryProductDto>> ReviewAsync(Guid id, Guid actor, InventoryReviewRequest request, CancellationToken ct)
    {
        var product = await Products().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (product is null) return Missing<InventoryProductDto>();
        if (request.ExpectedVersion == Guid.Empty || product.InventoryVersion != request.ExpectedVersion) return Conflict();
        if (product.Status != ProductStatus.PendingProcessing || product.ApprovalStatus != ProductApprovalStatus.Pending)
            return Invalid("Chỉ sản phẩm đang chờ duyệt mới được xét duyệt.");
        if (!request.Approve)
            return Invalid("Không hỗ trợ từ chối sản phẩm trong kho.");
        var reason = request.Reason?.Trim();
        if (reason?.Length > 1000)
            return Invalid("Nội dung ghi chú không quá 1000 ký tự.");
        if (!ValidSource(product)) return Invalid("Nguồn hàng chưa đủ điều kiện hoặc đã hết hiệu lực.");
        if (product.SourceType == ProductSourceType.Consignment)
        {
            if (!request.VerifiedPrice.HasValue || string.IsNullOrWhiteSpace(request.AgreementEvidence) || request.AgreementEvidence.Trim().Length > 1000)
                return Invalid("Cần đối chiếu giá và ghi bằng chứng thỏa thuận.");
            try { product.RecordVerifiedAgreement(request.VerifiedPrice.Value, request.AgreementEvidence); }
            catch (InvalidOperationException) { return Invalid("Giá đối chiếu không khớp với giá đã thỏa thuận."); }
        }
        var previous = product.ApprovalStatus.ToString();
        product.ApprovalStatus = ProductApprovalStatus.Approved;
        return await SaveAsync(product, actor, "Approval", previous, product.ApprovalStatus.ToString(), reason, ct);
    }

    public async Task<Result<InventoryProductDto>> ResubmitAsync(Guid id, Guid actor, InventoryVersionRequest request, CancellationToken ct)
    {
        var product = await Products().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (product is null) return Missing<InventoryProductDto>();
        if (request.ExpectedVersion == Guid.Empty || product.InventoryVersion != request.ExpectedVersion) return Conflict();
        if (product.Status != ProductStatus.PendingProcessing || product.ApprovalStatus != ProductApprovalStatus.Rejected)
            return Invalid("Chỉ gửi duyệt lại sản phẩm đã bị từ chối.");
        product.ApprovalStatus = ProductApprovalStatus.Pending;
        return await SaveAsync(product, actor, "Approval", "Rejected", "Pending", null, ct);
    }

    public async Task<Result<InventoryProductDto>> OfferAsync(Guid id, Guid actor, InventoryVersionRequest request, CancellationToken ct)
    {
        var product = await Products().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (product is null) return Missing<InventoryProductDto>();
        if (request.ExpectedVersion == Guid.Empty || product.InventoryVersion != request.ExpectedVersion) return Conflict();
        if (!CanOffer(product))
            return Invalid("Sản phẩm cần đang chờ xử lý, không bị từ chối tiếp nhận, có vị trí hoạt động và nguồn hàng hợp lệ trước khi mở bán.");
        product.OfferForSale();
        db.ProductStatusHistories.Add(new ProductStatusHistory
        {
            ProductId = id,
            PreviousStatus = ProductStatus.PendingProcessing,
            NewStatus = ProductStatus.OnSale,
            ChangedByAccountId = actor
        });
        return await SaveAsync(product, actor, "Status", "PendingProcessing", "OnSale", null, ct);
    }

    public async Task<Result<InventoryProductDto>> ChangeLocationAsync(Guid id, Guid actor, InventoryLocationRequest request, CancellationToken ct)
    {
        var product = await Products().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (product is null) return Missing<InventoryProductDto>();
        if (request.ExpectedVersion == Guid.Empty || product.InventoryVersion != request.ExpectedVersion) return Conflict();
        if (product.Status is not (ProductStatus.PendingProcessing or ProductStatus.OnSale))
            return Invalid("Không thể chuyển vị trí sản phẩm đã kết thúc.");
        var location = await db.StorageLocations.SingleOrDefaultAsync(x => x.Id == request.LocationId && x.IsActive, ct);
        if (location is null) return Invalid("Vị trí không tồn tại hoặc đã ngừng hoạt động.");
        if (product.StorageLocationId == location.Id) return Result.Success(Map(product));
        var previous = product.StorageLocation?.Code;
        product.StorageLocation = location;
        product.StorageLocationId = location.Id;
        return await SaveAsync(product, actor, "Location", previous, location.Code, null, ct);
    }

    public async Task<Result<BulkInventoryResultDto>> BulkChangeLocationAsync(Guid actor, BulkInventoryLocationRequest request, CancellationToken ct)
    {
        if (request.Items is null || request.Items.Count is 0 or > 100)
            return Result.Failure<BulkInventoryResultDto>(ApplicationError.Validation("INVENTORY.BULK_EMPTY", "Chọn từ 1 đến 100 sản phẩm để thao tác."));
        var location = await db.StorageLocations.SingleOrDefaultAsync(x => x.Id == request.LocationId && x.IsActive, ct);
        if (location is null)
            return Result.Failure<BulkInventoryResultDto>(ApplicationError.Conflict("INVENTORY.INVALID", "Vị trí không tồn tại hoặc đã ngừng hoạt động."));

        var ids = request.Items.Select(x => x.Id).Distinct().ToArray();
        var products = (await Products().Where(x => ids.Contains(x.Id)).ToListAsync(ct)).ToDictionary(x => x.Id);
        var failures = new List<BulkInventoryFailureDto>();
        var succeeded = 0;

        foreach (var item in request.Items)
        {
            if (!products.TryGetValue(item.Id, out var product))
            {
                failures.Add(new(item.Id, "Không tìm thấy sản phẩm."));
                continue;
            }
            if (item.ExpectedVersion == Guid.Empty || product.InventoryVersion != item.ExpectedVersion)
            {
                failures.Add(new(item.Id, $"{product.Name}: dữ liệu vừa thay đổi."));
                continue;
            }
            if (product.Status is not (ProductStatus.PendingProcessing or ProductStatus.OnSale))
            {
                failures.Add(new(item.Id, $"{product.Name}: không thể đổi vị trí sản phẩm đã kết thúc."));
                continue;
            }
            if (product.StorageLocationId == location.Id)
            {
                succeeded++;
                continue;
            }
            var previous = product.StorageLocation?.Code;
            product.StorageLocation = location;
            product.StorageLocationId = location.Id;
            product.InventoryVersion = Guid.NewGuid();
            db.InventoryEvents.Add(new InventoryEvent
            {
                ProductId = product.Id,
                ActorAccountId = actor,
                Action = "Location",
                PreviousValue = previous,
                NewValue = location.Code
            });
            succeeded++;
        }

        if (db.ChangeTracker.HasChanges())
        {
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException)
            {
                return Result.Failure<BulkInventoryResultDto>(ApplicationError.Conflict("INVENTORY.CONFLICT", "Một số sản phẩm vừa thay đổi. Vui lòng tải lại."));
            }
        }
        return Result.Success(new BulkInventoryResultDto(succeeded, failures));
    }

    public async Task<Result<BulkInventoryResultDto>> BulkOfferAsync(Guid actor, BulkInventoryOfferRequest request, CancellationToken ct)
    {
        if (request.Items is null || request.Items.Count is 0 or > 100)
            return Result.Failure<BulkInventoryResultDto>(ApplicationError.Validation("INVENTORY.BULK_EMPTY", "Chọn từ 1 đến 100 sản phẩm để thao tác."));

        var ids = request.Items.Select(x => x.Id).Distinct().ToArray();
        var products = (await Products().Where(x => ids.Contains(x.Id)).ToListAsync(ct)).ToDictionary(x => x.Id);
        var failures = new List<BulkInventoryFailureDto>();
        var succeeded = 0;

        foreach (var item in request.Items)
        {
            if (!products.TryGetValue(item.Id, out var product))
            {
                failures.Add(new(item.Id, "Không tìm thấy sản phẩm."));
                continue;
            }
            if (item.ExpectedVersion == Guid.Empty || product.InventoryVersion != item.ExpectedVersion)
            {
                failures.Add(new(item.Id, $"{product.Name}: dữ liệu vừa thay đổi."));
                continue;
            }
            if (!CanOffer(product))
            {
                failures.Add(new(item.Id, $"{product.Name}: chưa đủ điều kiện mở bán (cần đang chờ xử lý, không bị từ chối tiếp nhận, có vị trí hoạt động và nguồn hàng hợp lệ)."));
                continue;
            }
            product.OfferForSale();
            product.InventoryVersion = Guid.NewGuid();
            db.ProductStatusHistories.Add(new ProductStatusHistory
            {
                ProductId = product.Id,
                PreviousStatus = ProductStatus.PendingProcessing,
                NewStatus = ProductStatus.OnSale,
                ChangedByAccountId = actor
            });
            db.InventoryEvents.Add(new InventoryEvent
            {
                ProductId = product.Id,
                ActorAccountId = actor,
                Action = "Status",
                PreviousValue = "PendingProcessing",
                NewValue = "OnSale"
            });
            succeeded++;
        }

        if (db.ChangeTracker.HasChanges())
        {
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException)
            {
                return Result.Failure<BulkInventoryResultDto>(ApplicationError.Conflict("INVENTORY.CONFLICT", "Một số sản phẩm vừa thay đổi. Vui lòng tải lại."));
            }
        }
        return Result.Success(new BulkInventoryResultDto(succeeded, failures));
    }

    public async Task<Result<InventoryProductDto>> ChangePriceAsync(Guid id, Guid actor, InventoryPriceRequest request, CancellationToken ct)
    {
        var product = await Products().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (product is null) return Missing<InventoryProductDto>();
        if (request.ExpectedVersion == Guid.Empty || product.InventoryVersion != request.ExpectedVersion) return Conflict();
        if (product.SourceType != ProductSourceType.Buyout || !IsApprovedBuyout(product.Buyout) ||
            product.Status is not (ProductStatus.PendingProcessing or ProductStatus.OnSale))
            return Invalid("Chỉ được đổi giá hàng mua đứt đã duyệt và chưa kết thúc.");
        var reason = request.Reason?.Trim();
        if (request.Price < 0 || request.Price > 9999999999999999.99m || decimal.Round(request.Price, 2) != request.Price || string.IsNullOrWhiteSpace(reason) || reason.Length > 1000)
            return Invalid("Nhập giá hợp lệ và lý do thay đổi không quá 1000 ký tự.");
        if (product.ListedPrice == request.Price) return Result.Success(Map(product));
        var previous = product.ListedPrice.ToString(CultureInfo.InvariantCulture);
        product.ListedPrice = request.Price;
        return await SaveAsync(product, actor, "Price", previous, request.Price.ToString(CultureInfo.InvariantCulture), reason, ct);
    }

    public async Task<Result<InventoryProductDto>> ChangeInfoAsync(Guid id, Guid actor, InventoryInfoRequest request, CancellationToken ct)
    {
        var product = await Products().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (product is null) return Missing<InventoryProductDto>();
        if (request.ExpectedVersion == Guid.Empty || product.InventoryVersion != request.ExpectedVersion) return Conflict();
        if (product.Status is not (ProductStatus.PendingProcessing or ProductStatus.OnSale))
            return Invalid("Chỉ sửa thông tin sản phẩm đang xử lý hoặc đang bán.");
        var name = request.Name?.Trim(); var size = request.Size?.Trim(); var description = request.Description?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || string.IsNullOrWhiteSpace(size) || size.Length > 30 ||
            description?.Length > 2000 || request.ConditionPercentage is < 0 or > 100)
            return Invalid("Kiểm tra tên, kích thước và tình trạng sản phẩm.");
        var oldValue = System.Text.Json.JsonSerializer.Serialize(new { product.Name, product.Size, product.Description, product.ConditionPercentage });
        product.Name = name; product.Size = size; product.Description = description; product.ConditionPercentage = request.ConditionPercentage;
        var newValue = System.Text.Json.JsonSerializer.Serialize(new { product.Name, product.Size, product.Description, product.ConditionPercentage });
        // History values have a bounded length; the audit log preserves the individual fields.
        return await SaveAsync(product, actor, "Details", oldValue[..Math.Min(oldValue.Length, 1000)],
            newValue[..Math.Min(newValue.Length, 1000)], null, ct);
    }

    private static bool CanOffer(Product p) => p.Status == ProductStatus.PendingProcessing &&
        p.ApprovalStatus != ProductApprovalStatus.Rejected && p.StorageLocation is { IsActive: true } &&
        ValidSource(p) && (p.SourceType != ProductSourceType.Consignment || p.AgreedListedPrice == p.ListedPrice);

    private static bool ValidSource(Product p) => !string.IsNullOrWhiteSpace(p.Name) && !string.IsNullOrWhiteSpace(p.Barcode) &&
        !string.IsNullOrWhiteSpace(p.Size) && p.Brand.IsActive && p.Category.IsActive && p.ListedPrice >= 0 &&
        (p.SourceType == ProductSourceType.Consignment
            ? p.ConsignmentContract is { Status: ConsignmentContractStatus.Active } contract && contract.IsWithinTerm(DateTimeOffset.UtcNow)
            : p.SourceType == ProductSourceType.Buyout && IsApprovedBuyout(p.Buyout));

    private static bool IsApprovedBuyout(Buyout? buyout) => buyout is { Status: BuyoutStatus.Approved } &&
        (buyout.ApprovedByEmployeeId is not null || buyout.IsAutoApproved);

    private async Task<Result<InventoryProductDto>> SaveAsync(Product product, Guid actor, string action, string? previous, string? next, string? reason, CancellationToken ct)
    {
        product.InventoryVersion = Guid.NewGuid();
        db.InventoryEvents.Add(new InventoryEvent
        {
            ProductId = product.Id,
            ActorAccountId = actor,
            Action = action,
            PreviousValue = previous,
            NewValue = next,
            Reason = reason
        });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Conflict(); }
        return Result.Success(Map(product));
    }

    private static InventoryProductDto Map(Product p) => new(p.Id, p.Barcode, p.Name, p.Size, p.Description,
        p.ConditionPercentage, p.Brand.Name, p.Category.Name, p.ListedPrice, p.SourceType.ToString(),
        p.SourceType == ProductSourceType.Consignment ? p.ConsignmentContract?.ContractNumber : p.Buyout?.BuyoutNumber, p.Status.ToString(),
        p.ApprovalStatus.ToString(), p.InventoryVersion,
        p.StorageLocation is null ? null : new(p.StorageLocation.Id, p.StorageLocation.Code, p.StorageLocation.Name),
        p.Images.OrderBy(x => x.SortOrder).FirstOrDefault(x => x.ImageType == ProductImageType.Cover)?.Url);
    private static Result<T> Missing<T>() => Result.Failure<T>(ApplicationError.NotFound("PRODUCT.NOT_FOUND", "Không tìm thấy sản phẩm."));
    private static Result<InventoryProductDto> Conflict() => Result.Failure<InventoryProductDto>(ApplicationError.Conflict("INVENTORY.CONFLICT", "Sản phẩm vừa thay đổi. Vui lòng tải lại."));
    private static Result<InventoryProductDto> Invalid(string message) => Result.Failure<InventoryProductDto>(ApplicationError.Conflict("INVENTORY.INVALID", message));
}
