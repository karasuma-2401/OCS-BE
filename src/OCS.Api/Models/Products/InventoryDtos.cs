namespace OCS.Api.Models.Products;

public sealed record InventoryQuery(int Page = 1, int PageSize = 20, string? Q = null,
    string? Status = null, string? Approval = null, Guid? LocationId = null,
    bool? UnassignedLocation = null, string? SourceType = null, string? Sort = null);

public sealed record InventorySummaryDto(int TotalCount, int PendingApprovalCount, int ReadyToOfferCount,
    int UnassignedLocationCount, int OnSaleCount, int ExpiredCount, int PendingProcessingCount = 0);

public sealed record InventoryLocationDto(Guid Id, string Code, string Name, int ProductCount = 0);
public sealed record InventoryImageDto(Guid Id, string Url, string ImageType, int SortOrder);
public sealed record InventoryEventDto(string Action, string? PreviousValue, string? NewValue,
    string? Reason, Guid? ActorAccountId, DateTimeOffset OccurredAt, string? ActorName = null);
public sealed record InventoryProductDto(Guid Id, string Barcode, string Name, string Size,
    string? Description, int ConditionPercentage, string Brand, string Category, decimal ListedPrice,
    string SourceType, string? SourceReference, string Status, string ApprovalStatus, Guid Version,
    InventoryLocationDto? Location, string? CoverImageUrl);
public sealed record InventoryDetailDto(InventoryProductDto Product, string? AgreementEvidence,
    decimal? AgreedListedPrice, IReadOnlyList<InventoryImageDto> Images, IReadOnlyList<InventoryEventDto> History,
    decimal? PurchasePrice);

public sealed record InventoryReviewRequest(Guid ExpectedVersion, bool Approve, string? Reason,
    decimal? VerifiedPrice, string? AgreementEvidence);
public sealed record InventoryVersionRequest(Guid ExpectedVersion);
public sealed record InventoryLocationRequest(Guid ExpectedVersion, Guid LocationId);
public sealed record InventoryPriceRequest(Guid ExpectedVersion, decimal Price, string? Reason);
public sealed record InventoryInfoRequest(Guid ExpectedVersion, string? Name, string? Size,
    string? Description, int ConditionPercentage);
public sealed record CreateInventoryLocationRequest(string? Code, string? Name);
public sealed record BulkInventoryItemVersion(Guid Id, Guid ExpectedVersion);
public sealed record BulkInventoryLocationRequest(IReadOnlyList<BulkInventoryItemVersion> Items, Guid LocationId);
public sealed record BulkInventoryOfferRequest(IReadOnlyList<BulkInventoryItemVersion> Items);
public sealed record BulkInventoryFailureDto(Guid Id, string Message);
public sealed record BulkInventoryResultDto(int SucceededCount, IReadOnlyList<BulkInventoryFailureDto> Failures);
