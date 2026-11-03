namespace OCS.Api.Extensions;

public static class HttpContextExtensions
{
    public static string? GetClientIp(this HttpContext? context)
    {
        if (context is null)
        {
            return null;
        }

        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
        {
            var first = forwardedFor.ToString().Split(',')[0].Trim();
            if (!string.IsNullOrWhiteSpace(first))
            {
                return first;
            }
        }

        return context.Connection.RemoteIpAddress?.ToString();
    }
}

