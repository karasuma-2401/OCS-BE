using System.Diagnostics;
using System.Security.Claims;
using OCS.Api.Interfaces.Services;
using OCS.Api.Services;
using OCS.Api.Consts;

namespace OCS.Api.Auditing;

/// <summary>Reads identity from the authenticated server context, never from request payloads.</summary>
public sealed class HttpAuditContext(
    IHttpContextAccessor httpContextAccessor,
    SystemAuditContext systemContext) : IAuditContext
{
    public Guid? ActorAccountId
    {
        get
        {
            var user = httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            var subject = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
            if (!Guid.TryParse(subject, out var accountId) || accountId == Guid.Empty)
            {
                throw new InvalidOperationException("An authenticated audit actor must have a valid account ID claim.");
            }

            return accountId;
        }
    }

    public AuditActorType ActorType =>
        httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true
            ? AuditActorType.User
            : systemContext.ActorType;

    public string Source
    {
        get
        {
            var request = httpContextAccessor.HttpContext?.Request;
            var controller = request?.RouteValues["controller"]?.ToString();
            var action = request?.RouteValues["action"]?.ToString();
            // Route values are stable and avoid putting an account ID into the 50-character audit source.
            var source = !string.IsNullOrWhiteSpace(controller) && !string.IsNullOrWhiteSpace(action)
                ? $"{controller}.{action}"
                : request?.Path.Value ?? systemContext.Source;
            return source.Length <= 50 ? source : source[..50];
        }
    }

    public string? IpAddress =>
        httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? systemContext.IpAddress;

    public string? CorrelationId =>
        Activity.Current?.Id
        ?? httpContextAccessor.HttpContext?.TraceIdentifier
        ?? systemContext.CorrelationId;
}
