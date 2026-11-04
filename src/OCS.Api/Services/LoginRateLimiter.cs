using System.Globalization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using OCS.Api.Configuration;
using OCS.Api.Interfaces.Services;
using StackExchange.Redis;

namespace OCS.Api.Services;

public sealed class LoginRateLimiter(
    IDistributedCache cache,
    IOptions<AuthOptions> authOptions,
    IConnectionMultiplexer? redis = null) : ILoginRateLimiter
{
    private static string GetCacheKey(string clientIp) => $"ocs:ratelimit:login:ip:{clientIp}";

    public async Task<bool> AllowIpRequestAsync(string? clientIp, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await AtomicRateCounter.AcquireAsync(cache, redis, GetCacheKey(clientIp?.Trim() ?? "unknown"), authOptions.Value.MaxLoginAttemptsPerIpPerHour);
    }

    public Task RecordFailedAttemptAsync(string? clientIp, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

