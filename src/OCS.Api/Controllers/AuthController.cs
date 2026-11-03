using System.Security.Claims;
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
        var clientIp = HttpContext.GetClientIp();
        var result = await authService.RegisterAsync(request, clientIp, cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpPost("otp/verify")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpRequestDto request, CancellationToken cancellationToken)
    {
        var result = await authService.VerifyRegistrationOtpAsync(request, cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpPost("otp/resend")]
    [AllowAnonymous]
    public async Task<IActionResult> ResendOtp([FromBody] ResendOtpRequestDto request, CancellationToken cancellationToken)
    {
        var clientIp = HttpContext.GetClientIp();
        var result = await authService.ResendRegistrationOtpAsync(request, clientIp, cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var username = User.Identity?.Name;
        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

        return Ok(new
        {
            Id = userId,
            Username = username,
            Roles = roles
        });
    }
}
