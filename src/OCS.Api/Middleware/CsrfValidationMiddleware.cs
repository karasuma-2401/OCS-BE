using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace OCS.Api.Middleware;

public sealed class CsrfValidationMiddleware(
    RequestDelegate next,
    IAntiforgery antiforgery,
    IHostEnvironment environment)
{
    private static readonly HashSet<string> MutationMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Delete,
        HttpMethods.Patch
    };

    public async Task InvokeAsync(HttpContext context)
    {
        if (MutationMethods.Contains(context.Request.Method) &&
            context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) &&
            !context.Request.Path.StartsWithSegments("/api/webhooks", StringComparison.OrdinalIgnoreCase))
        {
            if (context.GetEndpoint()?.Metadata.GetMetadata<IAntiforgeryMetadata>() is { RequiresValidation: false })
            {
                await next(context);
                return;
            }

            // Allow test bypass in non-production environments when test header is explicitly provided
            if (!environment.IsProduction() &&
                context.Request.Headers.TryGetValue("X-Test-Bypass-Csrf", out var bypass) &&
                string.Equals(bypass, "true", StringComparison.OrdinalIgnoreCase))
            {
                await next(context);
                return;
            }

            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/problem+json";
                context.Response.Headers.CacheControl = "no-store";

                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Anti-forgery token không hợp lệ hoặc đã hết hạn."
                };
                problem.Extensions["code"] = "AUTH.CSRF_INVALID";
                problem.Extensions["traceId"] = context.TraceIdentifier;

                await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
                return;
            }
        }

        await next(context);
    }
}
