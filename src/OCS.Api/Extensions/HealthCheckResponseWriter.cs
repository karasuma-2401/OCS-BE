using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace OCS.Api.Extensions;

internal static class HealthCheckResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var response = new
        {
            service = "OCS API",
            status = report.Status.ToString(),
            timestamp = DateTimeOffset.UtcNow,
            durationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 2),
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description,
                    durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 2)
                })
        };

        return context.Response.WriteAsJsonAsync(
            response,
            cancellationToken: context.RequestAborted);
    }
}
