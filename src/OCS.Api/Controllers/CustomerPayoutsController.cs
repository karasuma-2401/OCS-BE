using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OCS.Api.Extensions;
using OCS.Api.Models.Settlements;
using OCS.Api.Services;

namespace OCS.Api.Controllers;

[ApiController]
[Route("api/customer/payouts")]
[Authorize(Roles = "Customer")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CustomerPayoutsController(SettlementService settlements) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Actor() is Guid customerId ? (await settlements.CustomerPayoutsAsync(customerId, ct)).ToActionResult(this) : Unauthorized();

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct) =>
        Actor() is Guid customerId ? (await settlements.CustomerPayoutDetailAsync(id, customerId, ct)).ToActionResult(this) : Unauthorized();

    [HttpGet("{id:guid}/proof")]
    public async Task<IActionResult> Proof(Guid id, CancellationToken ct)
    {
        if (Actor() is not Guid customerId) return Unauthorized();
        var result = await settlements.CustomerPayoutProofAsync(id, customerId, ct);
        return result.IsSuccess ? File(result.Data!.Content, result.Data.ContentType) : result.ToActionResult(this);
    }

    [HttpPost("{id:guid}/confirm-received")]
    public async Task<IActionResult> ConfirmReceived(Guid id, [FromBody] CustomerPayoutConfirmation? request, CancellationToken ct) =>
        Actor() is Guid customerId ? (await settlements.ConfirmReceivedAsync(id, customerId, request, ct)).ToActionResult(this) : Unauthorized();

    [HttpPost("{id:guid}/dispute")]
    [HttpPost("{id:guid}/complaint")]
    public async Task<IActionResult> Dispute(Guid id, [FromBody] SubmitPayoutDisputeRequest request, CancellationToken ct) =>
        Actor() is Guid customerId ? (await settlements.SubmitDisputeAsync(id, customerId, request, ct)).ToActionResult(this) : Unauthorized();

    private Guid? Actor() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id != Guid.Empty ? id : null;
}
