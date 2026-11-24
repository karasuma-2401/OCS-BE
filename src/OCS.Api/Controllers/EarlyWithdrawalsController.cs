using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OCS.Api.Authorization;
using OCS.Api.Extensions;
using OCS.Api.Models.Products;
using OCS.Api.Services;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Pos;

namespace OCS.Api.Controllers;

[ApiController, Route("api/early-withdrawals"), Authorize(Roles = "Admin,Manager,Staff")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class EarlyWithdrawalsController(EarlyWithdrawalService service, IVietQrPaymentService vietQr) : ControllerBase
{
    [HttpPost("{id:guid}/payment/vietqr"), Authorize(Policy = PermissionCatalog.WithdrawalPayment)]
    public async Task<IActionResult> InitiateVietQr(Guid id, InitiateWithdrawalVietQrRequest request, CancellationToken ct) => Actor() is Guid actor
        ? (await vietQr.InitiateWithdrawalAsync(id, actor, request.ForceNew, ct)).ToActionResult(this) : Unauthorized();
    [HttpGet("{id:guid}/payment/vietqr/{transactionId:guid}"), Authorize(Policy = PermissionCatalog.WithdrawalPayment)]
    public async Task<IActionResult> VietQrStatus(Guid id, Guid transactionId, CancellationToken ct) => Actor() is Guid actor
        ? (await vietQr.GetWithdrawalStatusAsync(id, transactionId, actor, ct)).ToActionResult(this) : Unauthorized();
    [HttpGet("summary"), Authorize(Policy = PermissionCatalog.WithdrawalRead)]
    public async Task<IActionResult> Summary(CancellationToken ct) => (await service.SummaryAsync(null, ct)).ToActionResult(this);
    [HttpGet("search"), Authorize(Policy = PermissionCatalog.WithdrawalRead)]
    public async Task<IActionResult> Search([FromQuery] WithdrawalQuery query, CancellationToken ct) => (await service.SearchAsync(null, query, ct)).ToActionResult(this);
    [HttpGet, Authorize(Policy = PermissionCatalog.WithdrawalRead)]
    public async Task<IActionResult> List(CancellationToken ct, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        (await service.ListAsync(null, page, pageSize, ct)).ToActionResult(this);
    [HttpGet("{id:guid}"), Authorize(Policy = PermissionCatalog.WithdrawalRead)]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct) => (await service.DetailAsync(id, null, ct)).ToActionResult(this);
    [HttpPost("{id:guid}/approve"), Authorize(Policy = PermissionCatalog.WithdrawalApprove)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct) => Actor() is Guid actor
        ? (await service.ApproveAsync(id, actor, ct)).ToActionResult(this) : Unauthorized();
    [HttpPost("{id:guid}/reject"), Authorize(Policy = PermissionCatalog.WithdrawalApprove)]
    public async Task<IActionResult> Reject(Guid id, WithdrawalDecisionRequest request, CancellationToken ct) => Actor() is Guid actor
        ? (await service.RejectAsync(id, actor, request, ct)).ToActionResult(this) : Unauthorized();
    [HttpPost("{id:guid}/payment"), Authorize(Policy = PermissionCatalog.WithdrawalPayment)]
    public async Task<IActionResult> Payment(Guid id, WithdrawalPaymentRequest request, CancellationToken ct) => Actor() is Guid actor
        ? (await service.PayAsync(id, actor, request, ct)).ToActionResult(this) : Unauthorized();
    [HttpPost("{id:guid}/handover"), Authorize(Policy = PermissionCatalog.WithdrawalHandover)]
    public async Task<IActionResult> Handover(Guid id, WithdrawalHandoverRequest request, CancellationToken ct) => Actor() is Guid actor
        ? (await service.HandoverAsync(id, actor, request, ct)).ToActionResult(this) : Unauthorized();
    private Guid? Actor() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id != Guid.Empty ? id : null;
}

[ApiController, Route("api/customers/me/early-withdrawals"), Authorize(Roles = "Customer")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CustomerEarlyWithdrawalsController(EarlyWithdrawalService service) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct) => Actor() is Guid actor
        ? (await service.SummaryAsync(actor, ct)).ToActionResult(this) : Unauthorized();
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] WithdrawalQuery query, CancellationToken ct) => Actor() is Guid actor
        ? (await service.SearchAsync(actor, query, ct)).ToActionResult(this) : Unauthorized();
    [HttpGet("eligible-products")]
    public async Task<IActionResult> Eligible(CancellationToken ct) => Actor() is Guid actor
        ? (await service.EligibleAsync(actor, ct)).ToActionResult(this) : Unauthorized();
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) => Actor() is Guid actor
        ? (await service.ListAsync(actor, page, pageSize, ct)).ToActionResult(this) : Unauthorized();
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct) => Actor() is Guid actor
        ? (await service.DetailAsync(id, actor, ct)).ToActionResult(this) : Unauthorized();
    [HttpPost]
    public async Task<IActionResult> Create(CreateEarlyWithdrawalRequest request, CancellationToken ct) => Actor() is Guid actor
        ? (await service.CreateAsync(actor, request, ct)).ToActionResult(this) : Unauthorized();
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) => Actor() is Guid actor
        ? (await service.CancelAsync(id, actor, ct)).ToActionResult(this) : Unauthorized();
    private Guid? Actor() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id != Guid.Empty ? id : null;
}
