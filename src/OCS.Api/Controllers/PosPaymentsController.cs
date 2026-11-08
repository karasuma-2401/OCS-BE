using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OCS.Api.Authorization;
using OCS.Api.Extensions;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Pos;

namespace OCS.Api.Controllers;

[ApiController]
[Route("api/pos/payments/vietqr")]
[Authorize(Roles = "Admin,Manager,Staff")]
[Authorize(Policy = PermissionCatalog.ScreenPos)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PosPaymentsController(IVietQrPaymentService vietQrPaymentService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(VietQrPaymentSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Initiate(
        [FromBody] InitiateVietQrPaymentRequest request,
        CancellationToken cancellationToken)
    {
        if (Actor() is not Guid actor)
        {
            return Unauthorized();
        }

        var result = await vietQrPaymentService.InitiateAsync(actor, request, cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpGet("{transactionId:guid}")]
    [ProducesResponseType(typeof(VietQrPaymentSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> GetStatus(
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        if (Actor() is not Guid)
        {
            return Unauthorized();
        }

        var result = await vietQrPaymentService.GetStatusAsync(transactionId, cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpGet("pending-invoices")]
    [ProducesResponseType(typeof(IReadOnlyList<PosPendingInvoiceSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPendingInvoices(CancellationToken cancellationToken)
    {
        if (Actor() is not Guid)
        {
            return Unauthorized();
        }

        var result = await vietQrPaymentService.GetPendingInvoicesAsync(cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpPost("{transactionId:guid}/simulate-webhook")]
    [ProducesResponseType(typeof(VietQrPaymentSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> SimulateWebhook(
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        if (Actor() is not Guid)
        {
            return Unauthorized();
        }

        var result = await vietQrPaymentService.SimulateWebhookAsync(transactionId, cancellationToken);
        return result.ToActionResult(this);
    }

    private Guid? Actor() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id != Guid.Empty
            ? id
            : null;
}
