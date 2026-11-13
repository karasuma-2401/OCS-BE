using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OCS.Api.Consts;
using OCS.Api.Extensions;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Notifications;

namespace OCS.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class NotificationsController(
    INotificationService notificationService,
    IFcmService fcmService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetMyNotifications([FromQuery] int limit = 50, CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        var items = await notificationService.GetForUserAsync(userId, limit, cancellationToken);
        var unreadCount = await notificationService.GetUnreadCountAsync(userId, cancellationToken);

        return Ok(new
        {
            data = items,
            unreadCount
        });
    }

    [HttpPatch("{id:guid}/read")]
    public async Task<IActionResult> MarkAsRead(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        var result = await notificationService.MarkAsReadAsync(id, userId, cancellationToken);
        return result.ToActionResult();
    }

    [HttpPatch("read-all")]
    public async Task<IActionResult> MarkAllAsRead(CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        var result = await notificationService.MarkAllAsReadAsync(userId, cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("fcm/status")]
    public async Task<IActionResult> GetFcmStatus(CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var tokens = await fcmService.GetTokensForAccountAsync(userId, cancellationToken);
        return Ok(new
        {
            configured = fcmService.IsConfigured,
            registeredDeviceCount = tokens.Count,
            devices = tokens.Select(t => new { t.DeviceType, t.LastUsedAt })
        });
    }

    [HttpPost("fcm/register-token")]
    public async Task<IActionResult> RegisterFcmToken(
        [FromBody] RegisterFcmTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        await fcmService.RegisterTokenAsync(userId, request.Token, request.DeviceType, cancellationToken);
        var tokens = await fcmService.GetTokensForAccountAsync(userId, cancellationToken);
        return Ok(new
        {
            registered = true,
            configured = fcmService.IsConfigured,
            registeredDeviceCount = tokens.Count
        });
    }

    [HttpDelete("fcm/unregister-token")]
    public async Task<IActionResult> UnregisterFcmToken(
        [FromBody] UnregisterFcmTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        await fcmService.UnregisterTokenAsync(userId, request.Token, cancellationToken);
        return Ok(new { unregistered = true });
    }

    [HttpPost("fcm/test")]
    public async Task<IActionResult> SendTestPushNotification(CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var result = await notificationService.CreateAsync(new CreateNotificationDto
        {
            RecipientAccountId = userId,
            SenderAccountId = userId,
            Title = "Kiểm tra thông báo OCS",
            Content = "Hệ thống thông báo thời gian thực và FCM đã sẵn sàng.",
            Type = NotificationType.System
        }, cancellationToken);

        return result.ToActionResult(this);
    }

    private Guid GetCurrentUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}
