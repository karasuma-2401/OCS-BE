namespace OCS.Api.Configuration;

public sealed class OtpOptions
{
    public const string SectionName = "Otp";

    public int ExpiryMinutes { get; set; } = 5;
    public int ResendCooldownSeconds { get; set; } = 60;
    public int MaxFailedAttempts { get; set; } = 5;
    public int ContinuationExpiryMinutes { get; set; } = 15;
    public int MaxSendsPerEmailPerHour { get; set; } = 5;
    public int MaxSendsPerIpPerHour { get; set; } = 20;
    public string? SecretKey { get; set; }
}

