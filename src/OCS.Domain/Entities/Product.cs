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

    public ICollection<ProductImage> Images { get; set; }
        = new List<ProductImage>();

    public ICollection<ProductStatusHistory> StatusHistories { get; set; }
        = new List<ProductStatusHistory>();

    public SaleInvoiceLine? SaleInvoiceLine { get; set; }

    /// <summary>
    /// Captures the commission tier and rate at appraisal time, so future edits to a brand's default rate
    /// cannot change the agreed terms or the fixed listed price of this consignment item.
    /// </summary>
    public void AssignConsignmentTerms(decimal fixedListedPrice, Brand brand)
    {
        ArgumentNullException.ThrowIfNull(brand);

        ArgumentOutOfRangeException.ThrowIfNegative(fixedListedPrice);

        if (brand.DefaultCommissionRate is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(brand), "The brand commission rate must be between 0 and 100.");
        }

        SourceType = ProductSourceType.Consignment;
        ListedPrice = fixedListedPrice;
        AgreedBrandTier = brand.Tier;
        AgreedCommissionRate = brand.DefaultCommissionRate;
    }

    /// <summary>Calculates the amount payable to the consignor if this item sells at its fixed listed price.</summary>
    public decimal CalculateEstimatedConsignorPayout()
    {
        if (SourceType != ProductSourceType.Consignment || AgreedCommissionRate is null)
        {
            throw new InvalidOperationException("Only consignment products with an agreed commission rate have a consignor payout.");
        }

        return decimal.Round(
            ListedPrice * (100m - AgreedCommissionRate.Value) / 100m,
            2,
            MidpointRounding.AwayFromZero);
    }

    public decimal CalculateEstimatedStoreCommission()
    {
        if (SourceType != ProductSourceType.Consignment || AgreedCommissionRate is null)
        {
            throw new InvalidOperationException("Only consignment products with an agreed commission rate have a store commission.");
        }

        return decimal.Round(
            ListedPrice * AgreedCommissionRate.Value / 100m,
            2,
            MidpointRounding.AwayFromZero);
    }
}
