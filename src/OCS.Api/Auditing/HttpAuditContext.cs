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

    public string Source =>
        httpContextAccessor.HttpContext?.Request.Path.Value is { } path && !string.IsNullOrWhiteSpace(path)
            ? path
            : systemContext.Source;

    public string? IpAddress =>
        httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? systemContext.IpAddress;

    public string? CorrelationId =>
        Activity.Current?.Id
        ?? httpContextAccessor.HttpContext?.TraceIdentifier
        ?? systemContext.CorrelationId;
}
