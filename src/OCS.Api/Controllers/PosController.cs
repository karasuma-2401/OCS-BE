using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OCS.Api.Authorization;
using OCS.Api.Extensions;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Pos;

namespace OCS.Api.Controllers;

[ApiController]
[Route("api/pos")]
[Authorize(Roles = "Admin,Manager,Staff")]
[Authorize(Policy = PermissionCatalog.ScreenPos)]
public class PosController(
    IPosService posService,
    IVietQrPaymentService vietQrPaymentService) : ControllerBase
{
    [HttpGet("lookup/{identifier}")]
    [ProducesResponseType(typeof(PosProductLookupDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> LookupForPos(string identifier, CancellationToken cancellationToken)
    {
        var result = await posService.LookupPosProductAsync(identifier, cancellationToken);
        return result.ToActionResult();
    }

    [HttpPost("checkout/cash")]
    [ProducesResponseType(typeof(PosCashPaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> CheckoutCash(
        [FromBody] PosCashPaymentRequest request,
        CancellationToken cancellationToken)
    {
        if (Actor() is not Guid actor)
        {
            return Unauthorized();
        }

        var result = await posService.CheckoutCashAsync(actor, request, cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpPost("checkout/vietqr")]
    [ProducesResponseType(typeof(VietQrPaymentSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> CheckoutVietQr(
        [FromBody] PosVietQrCheckoutRequest request,
        CancellationToken cancellationToken)
    {
        if (Actor() is not Guid actor)
        {
            return Unauthorized();
        }

        var invoiceResult = await posService.PrepareVietQrInvoiceAsync(actor, request, cancellationToken);
        if (!invoiceResult.IsSuccess)
        {
            return invoiceResult.ToActionResult(this);
        }

        var sessionResult = await vietQrPaymentService.InitiateAsync(
            actor,
            new InitiateVietQrPaymentRequest(invoiceResult.Data, request.ForceNew),
            cancellationToken);
        return sessionResult.ToActionResult(this);
    }

    [HttpGet("invoices/{invoiceIdOrNumber}")]
    [ProducesResponseType(typeof(PosInvoiceDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> GetInvoiceDetail(
        string invoiceIdOrNumber,
        CancellationToken cancellationToken)
    {
        var result = await posService.GetInvoiceDetailAsync(invoiceIdOrNumber, cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpGet("invoices/{invoiceIdOrNumber}/pdf")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> ExportInvoicePdf(
        string invoiceIdOrNumber,
        CancellationToken cancellationToken)
    {
        var result = await posService.ExportInvoicePdfAsync(invoiceIdOrNumber, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.ToActionResult(this);
        }

        return File(result.Data!, "application/pdf", $"hoa-don-{invoiceIdOrNumber}.pdf");
    }

    private Guid? Actor() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id != Guid.Empty
            ? id
            : null;
}