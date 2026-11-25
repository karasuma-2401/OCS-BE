using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OCS.Api.Authorization;
using OCS.Api.Extensions;
using OCS.Api.Models.Incidents;
using OCS.Api.Services;

namespace OCS.Api.Controllers;

[ApiController]
[Route("api/incidents")]
[Authorize(Roles = "Admin,Manager,Staff")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class IncidentsController(IncidentService incidents) : ControllerBase
{
    [HttpGet("products/by-barcode")]
    [Authorize(Policy = PermissionCatalog.IncidentsRead)]
    public async Task<IActionResult> FindProduct([FromQuery] string barcode, CancellationToken ct) =>
        (await incidents.FindProductAsync(barcode, ct)).ToActionResult(this);

    [HttpGet]
    [Authorize(Policy = PermissionCatalog.IncidentsRead)]
    public async Task<IActionResult> List([FromQuery] IncidentQuery query, CancellationToken ct) =>
        (await incidents.ListAsync(query, ct)).ToActionResult(this);

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.IncidentsRead)]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct) =>
        (await incidents.DetailAsync(id, ct)).ToActionResult(this);

    [HttpPost]
    [Authorize(Policy = PermissionCatalog.IncidentsCreate)]
    public async Task<IActionResult> Create(CreateIncidentRequest request, CancellationToken ct) =>
        Actor() is Guid actor ? (await incidents.CreateAsync(actor, request, ct)).ToActionResult(this) : Unauthorized();

    [HttpPost("{id:guid}/resubmit")]
    [Authorize(Policy = PermissionCatalog.IncidentsCreate)]
    public async Task<IActionResult> Resubmit(Guid id, ResubmitIncidentRequest request, CancellationToken ct) =>
        Actor() is Guid actor ? (await incidents.ResubmitAsync(id, actor, request, ct)).ToActionResult(this) : Unauthorized();

    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = "Admin,Manager")]
    [Authorize(Policy = PermissionCatalog.IncidentsReview)]
    public async Task<IActionResult> Approve(Guid id, ApproveIncidentRequest request, CancellationToken ct) =>
        Actor() is Guid actor ? (await incidents.ApproveAsync(id, actor, request, ct)).ToActionResult(this) : Unauthorized();

    [HttpPost("{id:guid}/request-recheck")]
    [Authorize(Roles = "Admin,Manager")]
    [Authorize(Policy = PermissionCatalog.IncidentsReview)]
    public async Task<IActionResult> RequestRecheck(Guid id, RequestIncidentRecheckRequest request, CancellationToken ct) =>
        Actor() is Guid actor ? (await incidents.RequestRecheckAsync(id, actor, request, ct)).ToActionResult(this) : Unauthorized();

    private Guid? Actor() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id != Guid.Empty ? id : null;
}
