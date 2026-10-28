using OCS.Api.Entities;
using OCS.Api.Consts;

namespace OCS.Api.Entities;

public sealed class Brand : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public BrandTier Tier { get; set; } = BrandTier.NoBrand;

    public decimal DefaultCommissionRate { get; set; }

    public bool IsActive { get; set; } = true;
}