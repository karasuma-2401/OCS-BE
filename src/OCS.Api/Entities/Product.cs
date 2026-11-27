using OCS.Api.Entities;
using OCS.Api.Consts;

namespace OCS.Api.Entities;

public sealed class Product : BaseEntity
{
    public string Barcode { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Size { get; set; } = string.Empty;

    public string? Material { get; set; }

    public string? Description { get; set; }

    public int ConditionPercentage { get; set; }

    public Guid BrandId { get; set; }

    public Guid CategoryId { get; set; }

    public Guid? StorageLocationId { get; set; }

    public ProductSourceType SourceType { get; private set; }

    public Guid? ConsignmentContractId { get; private set; }

    public Guid? BuyoutId { get; private set; }

    private decimal listedPrice;

    public decimal ListedPrice
    {
        get => listedPrice;
        set
        {
            if (ConsignmentContractId.HasValue && listedPrice != value)
                throw new InvalidOperationException("The agreed consignment price cannot be changed.");
            listedPrice = value;
        }
    }

    public decimal? AgreedListedPrice { get; private set; }

    public string? AgreementEvidence { get; set; }

    public ProductApprovalStatus ApprovalStatus { get; set; } = ProductApprovalStatus.Pending;

    public Guid InventoryVersion { get; set; } = Guid.NewGuid();
    public bool IsReservedForWithdrawal { get; set; }

    public decimal? PurchasePrice { get; private set; }

    public decimal? AgreedCommissionRate { get; private set; }

    public BrandTier? AgreedBrandTier { get; private set; }

    public ProductStatus Status { get; set; } = ProductStatus.PendingProcessing;

    public Brand Brand { get; set; } = null!;

    public Category Category { get; set; } = null!;

    public StorageLocation? StorageLocation { get; set; }

    public ConsignmentContract? ConsignmentContract { get; private set; }

    public Buyout? Buyout { get; private set; }

    public ICollection<ProductImage> Images { get; set; }
        = new List<ProductImage>();

    public ICollection<ProductStatusHistory> StatusHistories { get; set; }
        = new List<ProductStatusHistory>();

    public SaleInvoiceLine? SaleInvoiceLine { get; set; }

    /// <summary>
    /// Captures the commission tier and rate at appraisal time, so future edits to a brand's default rate
    /// cannot change the agreed terms or the fixed listed price of this consignment item.
    /// </summary>
    public void AssignConsignmentTerms(Guid contractId, decimal fixedListedPrice, Brand brand, decimal? agreedCommissionRate = null)
    {
        EnsureUnassigned();
        if (contractId == Guid.Empty)
        {
            throw new ArgumentException("A consignment contract is required.", nameof(contractId));
        }
        ArgumentNullException.ThrowIfNull(brand);

        ArgumentOutOfRangeException.ThrowIfNegative(fixedListedPrice);

        var rate = agreedCommissionRate ?? brand.DefaultCommissionRate;
        if (rate is < 0 or > 100 || decimal.Round(rate, 2) != rate)
        {
            throw new ArgumentOutOfRangeException(nameof(agreedCommissionRate), "The commission rate must be between 0 and 100.");
        }

        ListedPrice = fixedListedPrice;
        AgreedListedPrice = fixedListedPrice;
        SourceType = ProductSourceType.Consignment;
        ConsignmentContractId = contractId;
        BrandId = brand.Id;
        AgreedBrandTier = brand.Tier;
        AgreedCommissionRate = rate;
    }

    public void AssignBuyoutTerms(Guid buyoutId, decimal listedPrice, decimal purchasePrice)
    {
        EnsureUnassigned();
        if (buyoutId == Guid.Empty)
        {
            throw new ArgumentException("A buyout is required.", nameof(buyoutId));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(listedPrice);
        ArgumentOutOfRangeException.ThrowIfNegative(purchasePrice);
        SourceType = ProductSourceType.Buyout;
        BuyoutId = buyoutId;
        ListedPrice = listedPrice;
        PurchasePrice = purchasePrice;
    }

    public void RecordVerifiedAgreement(decimal agreedPrice, string evidence)
    {
        if (SourceType != ProductSourceType.Consignment || ConsignmentContractId is null ||
            agreedPrice != ListedPrice || (AgreedListedPrice.HasValue && AgreedListedPrice != agreedPrice) ||
            string.IsNullOrWhiteSpace(evidence))
            throw new InvalidOperationException("The verified agreement must match the current consignment price.");
        AgreedListedPrice = agreedPrice;
        AgreementEvidence = evidence.Trim();
    }

    public void OfferForSale()
    {
        if (Status != ProductStatus.PendingProcessing || ConsignmentContractId is null && BuyoutId is null)
        {
            throw new InvalidOperationException("Only an appraised product can be offered for sale.");
        }

        Status = ProductStatus.OnSale;
    }

    public void MarkSold()
    {
        if (Status != ProductStatus.OnSale)
        {
            throw new InvalidOperationException("Only a product on sale can be sold.");
        }

        Status = ProductStatus.Sold;
    }

    private void EnsureUnassigned()
    {
        if (Status != ProductStatus.PendingProcessing || ConsignmentContractId is not null || BuyoutId is not null)
        {
            throw new InvalidOperationException("Product terms have already been fixed.");
        }
    }

    public decimal CalculateEstimatedConsignorPayout()
    {
        if (SourceType != ProductSourceType.Consignment || AgreedCommissionRate is null)
        {
            throw new InvalidOperationException("Only consignment products with an agreed commission rate have a consignor payout.");
        }

        return decimal.Round(
            ListedPrice * (100m - AgreedCommissionRate.Value) / 100m,
            0,
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
            0,
            MidpointRounding.AwayFromZero);
    }

    public bool CanRequestEarlyWithdrawal(DateTimeOffset now) => GetEarlyWithdrawalBlockReason(now) is null;

    public string? GetEarlyWithdrawalBlockReason(DateTimeOffset now)
    {
        if (SourceType != ProductSourceType.Consignment) return "Món này không phải hàng ký gửi.";
        if (Status != ProductStatus.OnSale)
            return Status switch
            {
                ProductStatus.PendingProcessing => "Món này đang Chờ xử lý, chưa được đưa lên bán. Cửa hàng cần hoàn tất xử lý và chuyển món sang Đang bán trước khi bạn có thể rút sớm.",
                ProductStatus.Sold => "Món này đã bán nên không thể rút sớm.",
                ProductStatus.Expired => "Món này đã hết hạn ký gửi. Vui lòng theo dõi quy trình xử lý hàng hết hạn.",
                ProductStatus.EarlyWithdrawn => "Món này đã được bàn giao cho bạn qua yêu cầu rút sớm.",
                ProductStatus.Returned => "Món này đã được trả lại cho bạn.",
                ProductStatus.Donated => "Món này đã được quyên góp nên không thể rút sớm.",
                _ => "Món này không còn ở trạng thái Đang bán nên không thể rút sớm."
            };
        if (IsReservedForWithdrawal) return "Món này đang được giữ để bàn giao theo yêu cầu rút hàng đã duyệt.";
        if (AgreedCommissionRate is not (>= 0 and <= 100))
            return "Món này chưa có tỷ lệ hoa hồng đã thỏa thuận hợp lệ. Vui lòng liên hệ cửa hàng để đối chiếu hợp đồng.";
        if (ConsignmentContract is not { Status: ConsignmentContractStatus.Active })
            return "Hợp đồng của món này không còn hiệu lực nên không thể rút sớm.";
        if (ConsignmentContract.SignedAt > now) return "Hợp đồng chưa đến thời điểm bắt đầu ký gửi nên chưa thể rút sớm.";
        if (ConsignmentContract.EndsAt <= now) return "Hợp đồng đã hết thời hạn ký gửi. Vui lòng theo dõi quy trình xử lý hàng hết hạn.";
        return null;
    }

    public decimal CalculateEarlyWithdrawalFee(decimal? overrideRate = null)
    {
        var rate = overrideRate ?? AgreedCommissionRate;
        if (SourceType != ProductSourceType.Consignment || rate is not (>= 0 and <= 100))
            throw new InvalidOperationException("The product must have a valid early withdrawal fee or commission rate.");
        return decimal.Round(ListedPrice * rate.Value / 100m, 2, MidpointRounding.AwayFromZero);
    }
}
