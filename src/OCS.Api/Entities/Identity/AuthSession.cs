namespace OCS.Api.Entities;

/// <summary>Absolute session lifetime and revocation; the JWT carries Id in its sid claim.</summary>
public sealed class AuthSession : BaseEntity
{
    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public uint Version { get; private set; }
}
