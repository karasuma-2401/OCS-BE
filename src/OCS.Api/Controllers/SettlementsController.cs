using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OCS.Api.Authorization;
using OCS.Api.Extensions;
using OCS.Api.Models.Settlements;
using OCS.Api.Services;

namespace OCS.Api.Controllers;

[ApiController]
[Route("api/settlements")]
[Authorize(Roles = "Admin,Manager")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SettlementsController(SettlementService settlements) : ControllerBase
{
    [HttpGet("contracts")]
    [Authorize(Policy = PermissionCatalog.SettlementsRead)]
    public async Task<IActionResult> Contracts(CancellationToken ct) => (await settlements.ContractsAsync(ct)).ToActionResult(this);

    [HttpGet("contracts/{contractId:guid}/preview")]
    [Authorize(Policy = PermissionCatalog.SettlementsRead)]
    public async Task<IActionResult> Preview(Guid contractId, CancellationToken ct) =>
        (await settlements.PreviewAsync(contractId, ct)).ToActionResult(this);

    [HttpPost("contracts/{contractId:guid}/payouts")]
    [Authorize(Policy = PermissionCatalog.SettlementsManage)]
    public async Task<IActionResult> CreatePayout(Guid contractId, CreatePayoutRequest request, CancellationToken ct) =>
        Actor() is Guid actor ? (await settlements.CreatePayoutAsync(contractId, actor, request, ct)).ToActionResult(this) : Unauthorized();

    [HttpGet("payouts")]
    [Authorize(Policy = PermissionCatalog.SettlementsRead)]
    public async Task<IActionResult> Payouts(CancellationToken ct) => (await settlements.ListPayoutsAsync(ct)).ToActionResult(this);

    [HttpGet("payouts/{id:guid}")]
    [Authorize(Policy = PermissionCatalog.SettlementsRead)]
    public async Task<IActionResult> Payout(Guid id, CancellationToken ct) => (await settlements.PayoutDetailAsync(id, ct)).ToActionResult(this);

    [HttpGet("payouts/{id:guid}/history")]
    [Authorize(Policy = PermissionCatalog.SettlementsRead)]
    public async Task<IActionResult> History(Guid id, CancellationToken ct) => (await settlements.HistoryAsync(id, ct)).ToActionResult(this);

    [HttpGet("payouts/{id:guid}/proof")]
    [Authorize(Policy = PermissionCatalog.SettlementsRead)]
    public async Task<IActionResult> PayoutProof(Guid id, CancellationToken ct)
    {
        var result = await settlements.PayoutProofAsync(id, ct);
        return result.IsSuccess ? File(result.Data!.Content, result.Data.ContentType) : result.ToActionResult(this);
    }

    [HttpPost("payouts/{id:guid}/approve")]
    [Authorize(Policy = PermissionCatalog.SettlementsManage)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct) =>
        Actor() is Guid actor ? (await settlements.ApproveAsync(id, actor, ct)).ToActionResult(this) : Unauthorized();

    [HttpPost("payouts/{id:guid}/reject")]
    [Authorize(Policy = PermissionCatalog.SettlementsManage)]
    public async Task<IActionResult> Reject(Guid id, RejectPayoutRequest request, CancellationToken ct) =>
        Actor() is Guid actor ? (await settlements.RejectAsync(id, actor, request, ct)).ToActionResult(this) : Unauthorized();

    [HttpPut("payouts/{id:guid}/payment")]
    [Authorize(Policy = PermissionCatalog.SettlementsManage)]
    public async Task<IActionResult> UpdatePayment(Guid id, UpdatePayoutPaymentRequest request, CancellationToken ct) =>
        Actor() is Guid actor ? (await settlements.UpdatePaymentAsync(id, actor, request, ct)).ToActionResult(this) : Unauthorized();

    [HttpPut("payouts/{id:guid}/service-fee")]
    [Authorize(Policy = PermissionCatalog.SettlementsManage)]
    public async Task<IActionResult> UpdateServiceFee(Guid id, UpdatePayoutServiceFeeRequest request, CancellationToken ct) =>
        Actor() is Guid actor ? (await settlements.UpdateServiceFeeAsync(id, actor, request, ct)).ToActionResult(this) : Unauthorized();

    [HttpPost("payouts/{id:guid}/transfer")]
    [Authorize(Policy = PermissionCatalog.SettlementsManage)]
    public async Task<IActionResult> Transfer(Guid id, RecordPayoutTransferRequest request, CancellationToken ct) =>
        Actor() is Guid actor ? (await settlements.RecordTransferAsync(id, actor, request, ct)).ToActionResult(this) : Unauthorized();

    [HttpPost("payouts/{id:guid}/resolve-dispute")]
    [Authorize(Policy = PermissionCatalog.SettlementsManage)]
    public async Task<IActionResult> ResolveDispute(Guid id, [FromBody] ResolvePayoutDisputeRequest request, CancellationToken ct) =>
        Actor() is Guid actor ? (await settlements.ResolveDisputeAsync(id, actor, request, ct)).ToActionResult(this) : Unauthorized();

    private Guid? Actor() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id != Guid.Empty ? id : null;
}
