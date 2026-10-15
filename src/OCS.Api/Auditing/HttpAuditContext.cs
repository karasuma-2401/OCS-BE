using System.Diagnostics;
using System.Security.Claims;
using OCS.Application.Abstractions.Auditing;
using OCS.Domain.Enums;

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

    public AuditActorType ActorType => httpContextAccessor.HttpContext is { } context
        ? context.User.Identity?.IsAuthenticated == true ? AuditActorType.User : AuditActorType.Anonymous
        : systemContext.ActorType;

    // POS/MiniApp identification needs a trusted server policy; a client header is not proof.
    public string Source => httpContextAccessor.HttpContext is null ? systemContext.Source : "BackendApi";

    public string? IpAddress => httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? CorrelationId => httpContextAccessor.HttpContext is null
        ? systemContext.CorrelationId
        : Activity.Current?.TraceId.ToString() ?? httpContextAccessor.HttpContext.TraceIdentifier;
}
