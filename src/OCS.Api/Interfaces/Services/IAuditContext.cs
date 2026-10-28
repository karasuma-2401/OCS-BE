using OCS.Api.Consts;

namespace OCS.Api.Interfaces.Services;

public interface IAuditContext
{
    Guid? ActorAccountId { get; }

    AuditActorType ActorType { get; }

    string Source { get; }

    string? IpAddress { get; }

    string? CorrelationId { get; }
}
