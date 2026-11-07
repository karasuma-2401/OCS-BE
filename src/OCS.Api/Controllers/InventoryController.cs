using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OCS.Api.Authorization;
using OCS.Api.Extensions;
using OCS.Api.Models.Products;
using OCS.Api.Services;

namespace OCS.Api.Controllers;

[ApiController]
[Route("api/inventory")]
[Authorize(Roles = "Admin,Manager,Staff")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class InventoryController(InventoryService inventory) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = PermissionCatalog.ProductsRead)]
    public async Task<IActionResult> List([FromQuery] InventoryQuery query, CancellationToken ct) =>
        (await inventory.ListAsync(query, ct)).ToActionResult(this);

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.ProductsRead)]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct) =>
        (await inventory.DetailAsync(id, ct)).ToActionResult(this);

    [HttpGet("locations")]
    [Authorize(Policy = PermissionCatalog.ProductsRead)]
    public async Task<IActionResult> Locations(CancellationToken ct) => Ok(await inventory.LocationsAsync(ct));

    [HttpPost("locations")]
    [Authorize(Policy = PermissionCatalog.ProductsLocationsManage)]
    public async Task<IActionResult> CreateLocation(CreateInventoryLocationRequest request, CancellationToken ct) =>
        (await inventory.CreateLocationAsync(request, ct)).ToActionResult(this);

    [HttpPost("{id:guid}/review")]
    [Authorize(Policy = PermissionCatalog.ProductsApprove)]
    public async Task<IActionResult> Review(Guid id, InventoryReviewRequest request, CancellationToken ct) =>
        Actor() is Guid actor ? (await inventory.ReviewAsync(id, actor, request, ct)).ToActionResult(this) : Unauthorized();

    [HttpPost("{id:guid}/resubmit")]
    [Authorize(Policy = PermissionCatalog.ProductsCreate)]
    public async Task<IActionResult> Resubmit(Guid id, InventoryVersionRequest request, CancellationToken ct) =>
        Actor() is Guid actor ? (await inventory.ResubmitAsync(id, actor, request, ct)).ToActionResult(this) : Unauthorized();

    [HttpPost("{id:guid}/offer")]
    [Authorize(Policy = PermissionCatalog.ProductsOffer)]
    public async Task<IActionResult> Offer(Guid id, InventoryVersionRequest request, CancellationToken ct) =>
        Actor() is Guid actor ? (await inventory.OfferAsync(id, actor, request, ct)).ToActionResult(this) : Unauthorized();

    [HttpPut("{id:guid}/location")]
    [Authorize(Policy = PermissionCatalog.ProductsLocation)]
    public async Task<IActionResult> ChangeLocation(Guid id, InventoryLocationRequest request, CancellationToken ct) =>
        Actor() is Guid actor ? (await inventory.ChangeLocationAsync(id, actor, request, ct)).ToActionResult(this) : Unauthorized();

    [HttpPut("{id:guid}/price")]
    [Authorize(Policy = PermissionCatalog.ProductsPrice)]
    public async Task<IActionResult> ChangePrice(Guid id, InventoryPriceRequest request, CancellationToken ct) =>
        Actor() is Guid actor ? (await inventory.ChangePriceAsync(id, actor, request, ct)).ToActionResult(this) : Unauthorized();

    [HttpPut("{id:guid}/details")]
    [Authorize(Policy = PermissionCatalog.ProductsCreate)]
    public async Task<IActionResult> ChangeDetails(Guid id, InventoryInfoRequest request, CancellationToken ct) =>
        Actor() is Guid actor ? (await inventory.ChangeInfoAsync(id, actor, request, ct)).ToActionResult(this) : Unauthorized();

    private Guid? Actor() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
