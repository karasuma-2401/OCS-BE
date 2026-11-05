namespace OCS.Api.Models.Accounts;

public sealed record AccountSummaryDto(
    Guid Id,
    string FullName,
    string? Email,
    string PhoneNumber,
    string AccountType,
    string Status,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt,
    bool IsProtected = false,
    string? Username = null
);
