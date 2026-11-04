using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OCS.Api.Extensions;
using OCS.Api.Interfaces.Services;

namespace OCS.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class NotificationsController(INotificationService notificationService) : ControllerBase
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

    private Guid GetCurrentUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}

