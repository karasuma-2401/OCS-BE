using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OCS.Api.Authorization;
using OCS.Api.Extensions;
using OCS.Api.Models.Products;
using OCS.Api.Services;

namespace OCS.Api.Controllers;

[ApiController]
[Route("api/expiry-processings")]
[Authorize(Roles = "Admin,Manager,Staff")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ExpiryProcessingsController(ExpiryProcessingService processing) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = PermissionCatalog.ExpiryRead)]
    public async Task<IActionResult> List([FromQuery] ExpiryQuery query, CancellationToken ct) =>
        (await processing.ListAsync(query, ct)).ToActionResult(this);

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.ExpiryRead)]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct) =>
        (await processing.DetailAsync(id, ct)).ToActionResult(this);

    [HttpGet("{id:guid}/donation-receipts")]
    [Authorize(Policy = PermissionCatalog.ExpiryRead)]
    public async Task<IActionResult> DonationReceipts(Guid id, CancellationToken ct) =>
        (await processing.ReceiptsAsync(id, ct)).ToActionResult(this);

    [HttpGet("{id:guid}/donation-receipts/{receiptId:guid}")]
    [Authorize(Policy = PermissionCatalog.ExpiryRead)]
    public async Task<IActionResult> DonationReceipt(Guid id, Guid receiptId, CancellationToken ct) =>
        (await processing.ReceiptAsync(id, receiptId, ct)).ToActionResult(this);

    [HttpPost("{id:guid}/confirm-return")]
    [Authorize(Policy = PermissionCatalog.ExpiryReturn)]
    public async Task<IActionResult> ConfirmReturn(Guid id, ConfirmReturnRequest request, CancellationToken ct) =>
        Actor() is Guid actor ? (await processing.ConfirmReturnAsync(id, actor, request, ct)).ToActionResult(this) : Unauthorized();

    [HttpPost("{id:guid}/confirm-donation")]
    [Authorize(Policy = PermissionCatalog.ExpiryDonate)]
    public async Task<IActionResult> ConfirmDonation(Guid id, ConfirmDonationRequest request, CancellationToken ct) =>
        Actor() is Guid actor ? (await processing.ConfirmDonationAsync(id, actor, request, ct)).ToActionResult(this) : Unauthorized();

    [HttpPost("{id:guid}/mark-unclaimed")]
    [Authorize(Roles = "Admin,Manager")]
    [Authorize(Policy = PermissionCatalog.ExpiryMarkUnclaimed)]
    public async Task<IActionResult> MarkUnclaimed(Guid id, MarkUnclaimedRequest request, CancellationToken ct) =>
        Actor() is Guid actor ? (await processing.MarkUnclaimedAsync(id, actor, request, ct)).ToActionResult(this) : Unauthorized();

    private Guid? Actor() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
