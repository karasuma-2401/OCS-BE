using System.Text.Json;

namespace OCS.Api.Models.Accounts;

public sealed record AccountAuditLogDto(
    Guid Id,
    Guid? ActorAccountId,
    string Action,
    string EntityType,
    string EntityId,
    JsonElement Changes,
    DateTimeOffset OccurredAt,
    string Source);
