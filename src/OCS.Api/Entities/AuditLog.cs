using OCS.Api.Consts;

namespace OCS.Api.Entities;

public sealed class AuditLog
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid? ActorAccountId { get; init; }

    public AuditActorType ActorType { get; init; }

    public required string Action { get; init; }

    public required string EntityType { get; init; }

    public required string EntityId { get; init; }

    public string Changes { get; init; } = "{}";

    public string? Reason { get; init; }

    public DateTimeOffset OccurredAt { get; init; }
        = DateTimeOffset.UtcNow;

    public DateTimeOffset RecordedAt { get; init; }
        = DateTimeOffset.UtcNow;

    public required string Source { get; init; }

    public string? IpAddress { get; init; }

    public string? CorrelationId { get; init; }

    public Account? ActorAccount { get; private set; }
}