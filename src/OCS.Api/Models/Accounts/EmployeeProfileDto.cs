namespace OCS.Api.Models.Accounts;

public sealed record EmployeeProfileDto(
    Guid Id,
    string FullName,
    string? Email,
    string PhoneNumber,
    string? Username,
    string JobTitle,
    DateOnly HiredOn,
    DateOnly? LeftOn,
    string EmployeeStatus,
    string AccountStatus,
    IReadOnlyList<string> Roles);
