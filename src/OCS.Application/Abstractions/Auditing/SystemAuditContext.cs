using OCS.Domain.Enums;

namespace OCS.Application.Abstractions.Auditing;

/// <summary>Explicit audit identity for a scoped background operation.</summary>
public sealed class SystemAuditContext : IAuditContext
{
    public Guid? ActorAccountId => null;

    public AuditActorType ActorType => AuditActorType.System;

    public string Source { get; private set; } = string.Empty;

    public string? IpAddress => null;

    public string? CorrelationId { get; private set; }

    public void Initialize(string source, string? correlationId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(source.Length, 50);
        if (correlationId?.Length > 128)
        {
            throw new ArgumentException("Correlation ID must not exceed 128 characters.", nameof(correlationId));
        }

        Source = source;
        CorrelationId = correlationId;
    }
}
