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

/// <summary>Public image information shown in the product detail gallery.</summary>
public sealed record PublicProductImageResponse(
    Guid Id,
    string Url,
    string ImageType,
    int SortOrder);

/// <summary>Safe product detail projection for the public product modal/page.</summary>
public sealed record PublicProductDetailResponse(
    Guid Id,
    string Name,
    string? Description,
    string ListedPrice,
    string Size,
    int ConditionPercentage,
    PublicCategoryResponse Category,
    PublicBrandResponse Brand,
    IReadOnlyList<PublicProductImageResponse> Images);
