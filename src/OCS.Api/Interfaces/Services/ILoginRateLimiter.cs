namespace OCS.Api.Interfaces.Services;

public interface ILoginRateLimiter
{
    Task<bool> AllowIpRequestAsync(string? clientIp, CancellationToken cancellationToken = default);
    Task RecordFailedAttemptAsync(string? clientIp, CancellationToken cancellationToken = default);
}

