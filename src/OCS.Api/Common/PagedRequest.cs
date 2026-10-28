using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace OCS.Api.Common;

public sealed class PagedRequest : IValidatableObject
{
    public const int MaximumPageSize = 100;

    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, MaximumPageSize)]
    public int PageSize { get; init; } = 20;

    [ValidateNever]
    [JsonIgnore]
    public int Skip => checked((Page - 1) * PageSize);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Page >= 1 && PageSize >= 1 && (long)(Page - 1) * PageSize > int.MaxValue)
        {
            yield return new ValidationResult("The requested page offset is too large.", [nameof(Page)]);
        }
    }
}
