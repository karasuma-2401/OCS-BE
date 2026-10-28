namespace OCS.Api.Models.Products;

public record ProductDto(
    Guid Id,
    string Barcode,
    string Name,
    string? Description,
    decimal ListedPrice,
    string Status,
    string? CategoryName,
    string? BrandName,
    string? BrandTier);

public record CreateProductDto(
    string Name,
    string Barcode,
    string Size,
    decimal ListedPrice,
    string? Description,
    Guid CategoryId,
    Guid BrandId);
