using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OCS.Api.Storage;

namespace OCS.Api.HealthChecks;

internal sealed class R2HealthCheck(
    IServiceProvider serviceProvider,
    IOptions<R2Options> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var r2Options = options.Value;
        var client = serviceProvider.GetService<IAmazonS3>();

        if (client is null || string.IsNullOrWhiteSpace(r2Options.BucketName))
        {
            return HealthCheckResult.Unhealthy("Cloudflare R2 is not configured.");
        }

        try
        {
            await client.GetBucketLocationAsync(
                new GetBucketLocationRequest { BucketName = r2Options.BucketName },
                cancellationToken);

            return HealthCheckResult.Healthy("Cloudflare R2 is reachable.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Cloudflare R2 health check failed.", exception);
        }
    }
}
