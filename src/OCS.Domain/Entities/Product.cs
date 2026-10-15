using OCS.Domain.Common;
using OCS.Domain.Enums;

namespace OCS.Domain.Entities;

public sealed class Product : BaseEntity
{
    public string Barcode { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Size { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int ConditionPercentage { get; set; }

    public Guid BrandId { get; set; }

    public Guid CategoryId { get; set; }

    public Guid? StorageLocationId { get; set; }

    public ProductSourceType SourceType { get; set; }

    public Guid? ConsignmentContractId { get; set; }

    public Guid? BuyoutId { get; set; }

    public decimal ListedPrice { get; set; }

    public decimal? PurchasePrice { get; set; }

    public decimal? AgreedCommissionRate { get; set; }

    public BrandTier? AgreedBrandTier { get; set; }

    public ProductStatus Status { get; set; } = ProductStatus.PendingProcessing;

    public Brand Brand { get; set; } = null!;

    public Category Category { get; set; } = null!;

    public StorageLocation? StorageLocation { get; set; }

    public ConsignmentContract? ConsignmentContract { get; set; }

    public Buyout? Buyout { get; set; }
}