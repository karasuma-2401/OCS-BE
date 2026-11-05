using System.ComponentModel.DataAnnotations;
using OCS.Api.Common;

namespace OCS.Api.Contracts.Products;

/// <summary>Filters and pagination options for the public product catalog.</summary>
public sealed class GetPublicProductsRequest : PagedRequest
{
    [StringLength(100, ErrorMessage = "The field Q must be a string with a maximum length of 100.")]
    public string? Q { get; init; }

    public Guid? CategoryId { get; init; }

    public Guid? BrandId { get; init; }

    [StringLength(30, ErrorMessage = "The field Size must be a string with a maximum length of 30.")]
    public string? Size { get; init; }

    [Range(0, (double)decimal.MaxValue, ErrorMessage = "The field MinPrice must be greater than or equal to 0.")]
    public decimal? MinPrice { get; init; }

    [Range(0, (double)decimal.MaxValue, ErrorMessage = "The field MaxPrice must be greater than or equal to 0.")]
    public decimal? MaxPrice { get; init; }

    [Range(0, 100, ErrorMessage = "The field MinCondition must be between 0 and 100.")]
    public int? MinCondition { get; init; }

    [StringLength(30, ErrorMessage = "The field Sort must be a string with a maximum length of 30.")]
    public string? Sort { get; init; }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
        {
            yield return result;
        }

        if (MinPrice.HasValue && MinPrice.Value < 0)
        {
            yield return new ValidationResult(
                "The field MinPrice must be greater than or equal to 0.",
                [nameof(MinPrice)]);
        }

        if (MaxPrice.HasValue && MaxPrice.Value < 0)
        {
            yield return new ValidationResult(
                "The field MaxPrice must be greater than or equal to 0.",
                [nameof(MaxPrice)]);
        }

        if (MinPrice.HasValue && MaxPrice.HasValue && MinPrice.Value > MaxPrice.Value)
        {
            yield return new ValidationResult(
                "The minPrice cannot be greater than maxPrice.",
                [nameof(MinPrice), nameof(MaxPrice)]);
        }

        if (MinCondition.HasValue && MinCondition.Value is < 0 or > 100)
        {
            yield return new ValidationResult(
                "The field MinCondition must be between 0 and 100.",
                [nameof(MinCondition)]);
        }

        if (!string.IsNullOrWhiteSpace(Sort))
        {
            var normalizedSort = Sort.Trim().ToLowerInvariant();
            if (normalizedSort is not ("newest" or "priceasc" or "pricedesc"))
            {
                yield return new ValidationResult(
                    "The field Sort must be one of: newest, priceAsc, priceDesc.",
                    [nameof(Sort)]);
            }
        }
    }
}
