using OCS.Domain.Enums;

namespace OCS.Application.Features.Brands.GetBrands;

public sealed record BrandListItem(
    Guid Id,
    string Name,
    BrandTier Tier,
    decimal DefaultCommissionRate,
    bool IsActive);
