namespace OCS.Api.Models.SystemConfig;

public sealed record AuditLogFilterRequest(
    DateOnly? From = null,
    DateOnly? To = null,
    string? EntityType = null,
    string? Action = null,
    string? ActorType = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20);

public sealed record AuditLogItemDto(
    Guid Id,
    Guid? ActorAccountId,
    string? ActorName,
    string? ActorUsername,
    string ActorType,
    string Action,
    string EntityType,
    string EntityId,
    string Changes,
    string? Reason,
    string Source,
    DateTimeOffset OccurredAt);

public sealed record PagedAuditLogsDto(
    IReadOnlyList<AuditLogItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount);
