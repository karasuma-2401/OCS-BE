using OCS.Domain.Entities;
using OCS.Domain.Enums;

namespace OCS.Application.Tests.Domain;

public sealed class ProductPayoutTests
{
    [Fact]
    public void ConsignmentPayoutUsesDecimalAndRoundsAtTwoPlaces()
    {
        var brand = new Brand
        {
            Tier = BrandTier.Luxury,
            DefaultCommissionRate = 17.5m
        };
        var product = new Product
        {
            SourceType = ProductSourceType.Consignment
        };

        product.AssignConsignmentTerms(199_999.99m, brand);

        Assert.Equal(164_999.99m, product.CalculateEstimatedConsignorPayout());
        Assert.Equal(35_000.00m, product.CalculateEstimatedStoreCommission());
        Assert.Equal(BrandTier.Luxury, product.AgreedBrandTier);
    }

    [Fact]
    public void BuyoutDoesNotHaveAConsignorPayout()
    {
        var product = new Product
        {
            SourceType = ProductSourceType.Buyout,
            ListedPrice = 100_000m
        };

        Assert.Throws<InvalidOperationException>(() => { product.CalculateEstimatedConsignorPayout(); });
    }
}
