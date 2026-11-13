using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace OCS.Api.Models.Customers;

public sealed class ConsignmentSearchRequest : IValidatableObject
{
    public string? Query { get; init; }
    public string? Status { get; init; }
    public string? SignedFrom { get; init; }
    public string? SignedTo { get; init; }
    public string? ExpiresFrom { get; init; }
    public string? ExpiresTo { get; init; }
    [Range(1, int.MaxValue)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 5;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Status is not null && Status is not ("Active" or "Completed" or "Cancelled"))
            yield return new ValidationResult("Unsupported contract status.", [nameof(Status)]);
        foreach (var (value, name) in new[] { (SignedFrom, nameof(SignedFrom)), (SignedTo, nameof(SignedTo)), (ExpiresFrom, nameof(ExpiresFrom)), (ExpiresTo, nameof(ExpiresTo)) })
            if (value is not null && !TryDate(value, out _)) yield return new ValidationResult("Use a valid date in YYYY-MM-DD format.", [name]);
        if (TryDate(SignedFrom, out var sf) && TryDate(SignedTo, out var st) && sf > st)
            yield return new ValidationResult("The start date must not be after the end date.", [nameof(SignedFrom), nameof(SignedTo)]);
        if (TryDate(ExpiresFrom, out var ef) && TryDate(ExpiresTo, out var et) && ef > et)
            yield return new ValidationResult("The start date must not be after the end date.", [nameof(ExpiresFrom), nameof(ExpiresTo)]);
    }

    public static bool TryDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}

public sealed record ConsignmentOverview(int TotalContracts, int ActiveContracts, int OnSaleProducts, int SoldProducts, decimal SettledAmount);
public sealed record ConsignmentSearchResponse(IReadOnlyList<ConsignmentTrackingResponse> Items, int Page, int PageSize, int TotalCount, int TotalPages, ConsignmentOverview Overview);
