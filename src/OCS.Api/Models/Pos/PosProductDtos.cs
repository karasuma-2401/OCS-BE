namespace OCS.Api.Models.Pos;

public record PosProductLookupDto(
    Guid Id,
    string Barcode,
    string Name,
    string? Size,
    decimal ListedPrice,
    string Status,
    bool CanAddToCart,
    string? RejectionReason,
    string? CategoryName,
    string? BrandName,
    string? ImageUrl
);