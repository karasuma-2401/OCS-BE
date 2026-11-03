using System.Globalization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using OCS.Api.Configuration;
using OCS.Api.Interfaces.Services;

namespace OCS.Api.Services;

public sealed class LoginRateLimiter(
    IDistributedCache cache,
    IOptions<AuthOptions> authOptions) : ILoginRateLimiter
{
    private static string GetCacheKey(string clientIp) => $"ocs:ratelimit:login:ip:{clientIp}";

    public async Task<bool> AllowIpRequestAsync(string? clientIp, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientIp))
        {
            return true;
        }

        var key = GetCacheKey(clientIp.Trim());
        var cached = await cache.GetStringAsync(key, cancellationToken);
        if (cached is not null && int.TryParse(cached, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
        {
            return count < authOptions.Value.MaxLoginAttemptsPerIpPerHour;
        }

        return true;
    }

    public async Task RecordFailedAttemptAsync(string? clientIp, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientIp))
        {
            return;
        }

        var key = GetCacheKey(clientIp.Trim());
        var cached = await cache.GetStringAsync(key, cancellationToken);
        var newCount = 1;
        if (cached is not null && int.TryParse(cached, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
        {
            newCount = count + 1;
        }

        await cache.SetStringAsync(
            key,
            newCount.ToString(CultureInfo.InvariantCulture),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
            },
            cancellationToken);
    }
}

