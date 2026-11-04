namespace OCS.Api.Extensions;

public static class HttpContextExtensions
{
    public static string? GetClientIp(this HttpContext? context)
    {
        if (context is null)
        {
            return null;
        }

        // ForwardedHeadersMiddleware may replace this address only for configured trusted proxies.
        return context.Connection.RemoteIpAddress?.ToString();
    }
}

