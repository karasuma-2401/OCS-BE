namespace OCS.Api.Contracts.Brands;

public sealed record BrandResponse(
    Guid Id,
    string Name,
    string Tier,
    string DefaultCommissionRate,
    bool IsActive);
