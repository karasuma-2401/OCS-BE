namespace OCS.Api.Interfaces.Services;

public interface IOtpRateLimiter
{
    Task<bool> AllowIpRequestAsync(string? clientIp, CancellationToken cancellationToken = default);
    Task RecordIpRequestAsync(string? clientIp, CancellationToken cancellationToken = default);
}

