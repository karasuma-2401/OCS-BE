using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OCS.Api.Common;
using OCS.Api.Extensions;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Auth;

namespace OCS.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(
    IAuthService authService,
    IWebHostEnvironment? environment = null) : ControllerBase
{
    public const string SessionCookieName = "ocs_session";

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var clientIp = HttpContext.GetClientIp();
        var result = await authService.LoginAsync(request, clientIp, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.ToActionResult(this);
        }

        var loginResult = result.Data!;
        var isDevelopment = environment?.IsDevelopment() ?? true;
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = !isDevelopment,
            Path = "/api",
            Expires = loginResult.ExpiresAt
        };

        Response.Cookies.Append(SessionCookieName, loginResult.AccessToken, cookieOptions);

        var response = new LoginResponseDto(
            loginResult.ExpiresIn,
            loginResult.ExpiresAt,
            loginResult.User);

        return Ok(response);
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterRequestDto request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var clientIp = HttpContext.GetClientIp();
        var result = await authService.RegisterAsync(request, clientIp, cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpPost("otp/verify")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpRequestDto request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var result = await authService.VerifyRegistrationOtpAsync(request, cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpPost("otp/resend")]
    [AllowAnonymous]
    public async Task<IActionResult> ResendOtp([FromBody] ResendOtpRequestDto request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var clientIp = HttpContext.GetClientIp();
        var result = await authService.ResendRegistrationOtpAsync(request, clientIp, cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        var accountIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var sidClaim = User.FindFirst("sid")?.Value
                       ?? User.FindFirst(JwtRegisteredClaimNames.Sid)?.Value;

        if (!Guid.TryParse(accountIdClaim, out var accountId) || !Guid.TryParse(sidClaim, out var sessionId))
        {
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Phiên đăng nhập không hợp lệ hoặc đã hết hạn."
            };
            problem.Extensions["code"] = "UNAUTHORIZED";
            problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
            return Unauthorized(problem);
        }

        var result = await authService.GetCurrentUserAsync(accountId, sessionId, cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var sidClaim = User.FindFirst("sid")?.Value
                       ?? User.FindFirst(JwtRegisteredClaimNames.Sid)?.Value;

        if (Guid.TryParse(sidClaim, out var sessionId))
        {
            await authService.LogoutAsync(sessionId, cancellationToken);
        }

        var isDevelopment = environment?.IsDevelopment() ?? true;
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = !isDevelopment,
            Path = "/api",
            Expires = DateTimeOffset.UnixEpoch
        };

        Response.Cookies.Delete(SessionCookieName, cookieOptions);
        Response.Headers.CacheControl = "no-store";

        return NoContent();
    }

    [HttpGet("csrf")]
    [AllowAnonymous]
    public IActionResult GetCsrfToken([FromServices] IAntiforgery antiforgery)
    {
        ArgumentNullException.ThrowIfNull(antiforgery);

        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        Response.Headers.CacheControl = "no-store";

        return Ok(new CsrfTokenResponseDto(tokens.RequestToken!));
    }
}
