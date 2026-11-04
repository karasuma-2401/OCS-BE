namespace OCS.Api.Entities;

/// <summary>Durable email proof; delivery and cryptography belong in services, not this entity.</summary>
public sealed class OtpChallenge : BaseEntity
{
    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string Purpose { get; set; } = "RegistrationActivation";
    public string CodeHash { get; set; } = string.Empty;
    public string ContinuationTokenHash { get; set; } = string.Empty;
    public DateTimeOffset ContinuationExpiresAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset ResendAvailableAt { get; set; }
    public int FailedAttempts { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset? InvalidatedAt { get; set; }
    // Pending/failed delivery must never be treated as proof; resend replaces the old challenge.
    public string DeliveryStatus { get; set; } = "Pending";
    public DateTimeOffset? SentAt { get; set; }
    public uint Version { get; private set; }
}
