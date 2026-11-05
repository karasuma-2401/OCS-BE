namespace OCS.Api.Contracts.Products;

/// <summary>Public category information shown on a product card.</summary>
public sealed record PublicCategoryResponse(
    Guid Id,
    string Name);

/// <summary>Public brand information shown on a product card.</summary>
public sealed record PublicBrandResponse(
    Guid Id,
    string Name);

/// <summary>Safe product projection for the public landing page catalog.</summary>
public sealed record PublicProductResponse(
    Guid Id,
    string Name,
    string? Description,
    string ListedPrice,
    string Size,
    int ConditionPercentage,
    PublicCategoryResponse Category,
    PublicBrandResponse Brand,
    string? CoverImageUrl);
