using OCS.Domain.Enums;

namespace OCS.Application.Abstractions.Auditing;

public interface IAuditContext
{
    Guid? ActorAccountId { get; }

    AuditActorType ActorType { get; }

    string Source { get; }

    string? IpAddress { get; }

    string? CorrelationId { get; }
}