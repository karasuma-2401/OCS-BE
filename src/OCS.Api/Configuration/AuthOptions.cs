namespace OCS.Api.Configuration;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public int MaxFailedLoginAttempts { get; set; } = 5;
    public int LockoutDurationMinutes { get; set; } = 15;
    public int MaxLoginAttemptsPerIpPerHour { get; set; } = 60;
    public int SessionExpirySeconds { get; set; } = 28800; // 8 hours
}

