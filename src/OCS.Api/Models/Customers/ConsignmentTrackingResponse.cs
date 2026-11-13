namespace OCS.Api.Models.Customers;

/// <summary>Thong tin san pham ky gui va tien du kien neu ban dung gia niem yet.</summary>
/// <param name="AgreedCommissionRate">Ty le hoa hong theo phan tram; null neu chua co du lieu.</param>
/// <param name="EstimatedPayout">So tien du kien khach nhan; null khi thieu ty le hoa hong.</param>
public sealed record ConsignmentProductResponse(
    Guid Id,
    string Barcode,
    string Name,
    string Status,
    decimal ListedPrice,
    decimal? AgreedCommissionRate,
    decimal? EstimatedPayout,
    bool DetailAvailable = true);

/// <summary>Thong tin theo doi mot hop dong ky gui cua khach hang.</summary>
/// <param name="EstimatedPayout">Tong du kien cho tat ca san pham; null neu co san pham thieu uoc tinh.</param>
/// <param name="SettledAmount">Tong so tien cua cac khoan quyet toan da hoan tat.</param>
public sealed record ConsignmentTrackingResponse(
    Guid ContractId,
    string ContractNumber,
    Guid? AppointmentId,
    DateTimeOffset SignedAt,
    DateTimeOffset EndsAt,
    string Status,
    string ExpiryHandling,
    IReadOnlyList<ConsignmentProductResponse> Products,
    decimal? EstimatedPayout,
    decimal SettledAmount,
    ConsignmentDocumentMetadata? ContractDocument = null);

public sealed record ConsignmentDocumentMetadata(string FileName, string ContentType, DateTimeOffset? UploadedAt);

public sealed record ConsignmentProductDetailResponse(
    Guid Id, string Barcode, string Name, string Status, decimal ListedPrice,
    decimal? AgreedCommissionRate, decimal? EstimatedPayout, string? Description,
    string? BrandName, string? CategoryName, string? Size, string? Condition,
    IReadOnlyList<string> ImageUrls);
