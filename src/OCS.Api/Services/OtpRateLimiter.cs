using System.Globalization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using OCS.Api.Configuration;
using OCS.Api.Interfaces.Services;
using StackExchange.Redis;

namespace OCS.Api.Services;

public sealed class OtpRateLimiter(
    IDistributedCache cache,
    IOptions<OtpOptions> otpOptions,
    IConnectionMultiplexer? redis = null) : IOtpRateLimiter
{
    private static string GetCacheKey(string clientIp) => $"ocs:ratelimit:otp:ip:{clientIp}";

    public async Task<bool> AllowIpRequestAsync(string? clientIp, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await AtomicRateCounter.AcquireAsync(cache, redis, GetCacheKey(clientIp?.Trim() ?? "unknown"), otpOptions.Value.MaxSendsPerIpPerHour);
    }

    public Task RecordIpRequestAsync(string? clientIp, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
