using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OCS.Api.Authorization;
using OCS.Api.Extensions;
using OCS.Api.Models.Buyouts;
using OCS.Api.Services;

namespace OCS.Api.Controllers;

[ApiController]
[Route("api/buyouts")]
[Authorize(Roles = "Admin,Manager,Staff")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BuyoutsController(BuyoutService service) : ControllerBase
{
    [HttpGet("customers")]
    [Authorize(Policy = PermissionCatalog.AppointmentsStaffReception)]
    public async Task<IActionResult> Customers([FromQuery] string? query, CancellationToken ct) =>
        Actor() is Guid id ? (await service.FindCustomersAsync(id, query, ct)).ToActionResult(this) : Unauthorized();

    /// <summary>Ghi nhan san pham da kiem dinh dat tieu chuan.</summary>
    [HttpPost("inspections")]
    [Authorize(Policy = PermissionCatalog.AppointmentsStaffReception)]
    public async Task<IActionResult> Inspect(CreateBuyoutInspectionRequest request, CancellationToken ct) =>
        Actor() is Guid id ? (await service.InspectAsync(id, request, ct)).ToActionResult(this) : Unauthorized();

    /// <summary>Lay danh sach san pham da kiem dinh de lap de nghi.</summary>
    [HttpGet("inspections")]
    [Authorize(Policy = PermissionCatalog.ProductsRead)]
    public async Task<IActionResult> Inspections([FromQuery] Guid? customerId, CancellationToken ct) =>
        (await service.ListInspectionsAsync(customerId, ct)).ToActionResult(this);

    /// <summary>Lap phieu de nghi cho mot san pham da kiem dinh.</summary>
    [HttpPost]
    [Authorize(Policy = PermissionCatalog.ProductsCreate)]
    public async Task<IActionResult> Create(CreateBuyoutRequest request, CancellationToken ct) =>
        Actor() is Guid id ? (await service.CreateAsync(id, request, ct)).ToActionResult(this) : Unauthorized();

    [HttpGet]
    [Authorize(Policy = PermissionCatalog.ProductsRead)]
    public async Task<IActionResult> List(CancellationToken ct) => (await service.ListAsync(ct)).ToActionResult(this);

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.ProductsRead)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => (await service.GetAsync(id, ct)).ToActionResult(this);

    [HttpGet("{id:guid}/receipt")]
    [Authorize(Policy = PermissionCatalog.ProductsRead)]
    public async Task<IActionResult> Receipt(Guid id, CancellationToken ct) => (await service.GetReceiptAsync(id, ct)).ToActionResult(this);

    /// <summary>Quan ly duyet hoac tu choi phieu dang cho.</summary>
    [HttpPost("{id:guid}/review")]
    [Authorize(Roles = "Admin,Manager")]
    [Authorize(Policy = PermissionCatalog.ProductsApprove)]
    public async Task<IActionResult> Review(Guid id, ReviewBuyoutRequest request, CancellationToken ct) =>
        Actor() is Guid actor ? (await service.ReviewAsync(id, actor, request, ct)).ToActionResult(this) : Unauthorized();

    /// <summary>Xac nhan da chi tien va tao san pham thuoc cua hang.</summary>
    [HttpPost("{id:guid}/payment")]
    [Authorize(Policy = PermissionCatalog.ProductsCreate)]
    public async Task<IActionResult> Pay(Guid id, PayBuyoutRequest request, CancellationToken ct) =>
        Actor() is Guid actor ? (await service.PayAsync(id, actor, request, ct)).ToActionResult(this) : Unauthorized();

    private Guid? Actor() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id != Guid.Empty ? id : null;
}
