using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OCS.Api.Authorization;
using OCS.Api.Extensions;
using OCS.Api.Models.SystemConfig;
using OCS.Api.Services;

namespace OCS.Api.Controllers;

[ApiController]
[Route("api/audit-logs")]
[Authorize(Policy = PermissionCatalog.AuditLogsRead)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuditLogsController(AuditLogQueryService auditLogs) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] AuditLogFilterRequest filter, CancellationToken ct) =>
        (await auditLogs.SearchAsync(filter, ct)).ToActionResult(this);
}
