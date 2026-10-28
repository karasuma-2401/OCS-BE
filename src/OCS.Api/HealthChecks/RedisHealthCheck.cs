using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace OCS.Api.HealthChecks;

internal sealed class RedisHealthCheck(
    IDistributedCache cache,
    IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(configuration["Redis:ConnectionString"]))
        {
            return HealthCheckResult.Degraded(
                "Redis is not configured; distributed memory cache is active.");
        }

        try
        {
            await cache.GetAsync("health:redis", cancellationToken);
            return HealthCheckResult.Healthy("Redis is reachable.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Redis health check failed.", exception);
        }
    }
}
