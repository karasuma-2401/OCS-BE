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
    }
}
