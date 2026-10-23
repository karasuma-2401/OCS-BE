using OCS.Domain.Entities;
using OCS.Domain.Enums;

namespace OCS.Domain.Tests;

public sealed class ProductTests
{
    [Fact]
    public void ConsignmentPayoutUsesDecimalAndRoundsAtTwoPlaces()
    {
        var brand = new Brand
        {
            Tier = BrandTier.Luxury,
            DefaultCommissionRate = 17.5m
        };
        var product = new Product();

        product.AssignConsignmentTerms(Guid.NewGuid(), 199_999.99m, brand);

        Assert.Equal(164_999.99m, product.CalculateEstimatedConsignorPayout());
        Assert.Equal(35_000.00m, product.CalculateEstimatedStoreCommission());
        Assert.Equal(BrandTier.Luxury, product.AgreedBrandTier);
    }

    [Fact]
    public void BuyoutDoesNotHaveAConsignorPayout()
    {
        var product = new Product();
        product.AssignBuyoutTerms(Guid.NewGuid(), 100_000m, 50_000m);

        Assert.Throws<InvalidOperationException>(() => { product.CalculateEstimatedConsignorPayout(); });
    }

    [Fact]
    public void TermsCannotBeChangedAfterAppraisal()
    {
        var brand = new Brand { DefaultCommissionRate = 20m };
        var product = new Product();
        product.AssignConsignmentTerms(Guid.NewGuid(), 100_000m, brand);
        brand.DefaultCommissionRate = 80m;

        Assert.Equal(80_000m, product.CalculateEstimatedConsignorPayout());
        Assert.Throws<InvalidOperationException>(() => product.AssignConsignmentTerms(Guid.NewGuid(), 1m, brand));
        Assert.Throws<InvalidOperationException>(() => product.AssignBuyoutTerms(Guid.NewGuid(), 1m, 1m));
    }

    [Fact]
    public void ProductMustBeAppraisedAndOfferedBeforeItCanBeSold()
    {
        var product = new Product();
        Assert.Throws<InvalidOperationException>(product.OfferForSale);
        Assert.Throws<InvalidOperationException>(product.MarkSold);
        product.AssignBuyoutTerms(Guid.NewGuid(), 100m, 50m);
        product.OfferForSale();
        product.MarkSold();
        Assert.Equal(ProductStatus.Sold, product.Status);
        Assert.Throws<InvalidOperationException>(product.MarkSold);
        Assert.Throws<InvalidOperationException>(product.OfferForSale);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(10, -1)]
    public void BuyoutRejectsNegativePrices(decimal listedPrice, decimal purchasePrice)
    {
        var product = new Product();
        Assert.Throws<ArgumentOutOfRangeException>(() => product.AssignBuyoutTerms(Guid.NewGuid(), listedPrice, purchasePrice));
        Assert.Null(product.BuyoutId);
    }
}
