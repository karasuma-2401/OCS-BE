using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OCS.Api.Common;
using OCS.Api.Configuration;
using OCS.Api.Extensions;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Pos;

namespace OCS.Api.Controllers;

[ApiController]
[Route("api/webhooks/vietqr")]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class VietQrWebhooksController(
    IVietQrPaymentService vietQrPaymentService,
    IOptions<VietQrOptions> options,
    IHostEnvironment environment) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(VietQrWebhookProcessResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> ReceiveWebhook(
        [FromBody] VietQrWebhookPayloadDto payload,
        CancellationToken cancellationToken)
    {
        if (!IsWebhookAuthorized())
        {
            return Result
                .Failure<VietQrWebhookProcessResultDto>(
                    ApplicationError.Unauthorized("POS.WEBHOOK_UNAUTHORIZED", "Chữ ký hoặc mã bí mật Webhook VietQR không hợp lệ."))
                .ToActionResult(this);
        }

        var result = await vietQrPaymentService.ProcessWebhookAsync(payload, cancellationToken);
        return result.ToActionResult(this);
    }

    private bool IsWebhookAuthorized()
    {
        var expectedSecret = options.Value.WebhookSecret?.Trim();
        if (string.IsNullOrEmpty(expectedSecret))
        {
            return !environment.IsProduction();
        }

        var candidateToken = ExtractTokenFromHeaders();
        if (string.IsNullOrEmpty(candidateToken))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expectedSecret);
        var actualBytes = Encoding.UTF8.GetBytes(candidateToken);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private string? ExtractTokenFromHeaders()
    {
        if (Request.Headers.TryGetValue("X-Webhook-Secret", out var customSecret)
            && !string.IsNullOrWhiteSpace(customSecret))
        {
            return customSecret.ToString().Trim();
        }

        if (Request.Headers.TryGetValue("Secure-Token", out var cassoToken)
            && !string.IsNullOrWhiteSpace(cassoToken))
        {
            return cassoToken.ToString().Trim();
        }

        if (Request.Headers.TryGetValue("Authorization", out var authHeader)
            && !string.IsNullOrWhiteSpace(authHeader))
        {
            var raw = authHeader.ToString().Trim();
            const string apikeyPrefix = "Apikey ";
            const string bearerPrefix = "Bearer ";

            if (raw.StartsWith(apikeyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return raw[apikeyPrefix.Length..].Trim();
            }

            if (raw.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return raw[bearerPrefix.Length..].Trim();
            }

            return raw;
        }

        return null;
    }
}
