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
    public async Task<IActionResult> GetMyNotifications(
        [FromQuery] int limit = 50,
        [FromQuery] bool? unreadOnly = null,
        [FromQuery] string? category = null,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        var clampedLimit = Math.Clamp(limit, 1, 200);
        var items = await notificationService.GetForUserAsync(userId, clampedLimit, cancellationToken);
        var unreadCount = await notificationService.GetUnreadCountAsync(userId, cancellationToken);

        IEnumerable<NotificationDto> filtered = items;
        if (unreadOnly == true)
        {
            filtered = filtered.Where(n => !n.IsRead);
        }

        if (!string.IsNullOrWhiteSpace(category) && !string.Equals(category, "all", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(n => string.Equals(n.Category, category.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        return Ok(new
        {
            data = filtered.ToList(),
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
    public async Task<IActionResult> SendTestPushNotification(
        [FromQuery] string? category = null,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var (title, content, type, resolvedCategory) = (category?.Trim()) switch
        {
            "Appointment" => (
                "Nhắc nhở: Lịch hẹn ký gửi sắp diễn ra",
                "Bạn có lịch hẹn ký gửi mã #APT-2026 tại cửa hàng OCS hôm nay lúc 14:00 - 15:00.",
                NotificationType.System,
                "Appointment"),
            "Consignment" => (
                "Tiếp nhận ký gửi thành công",
                "Hợp đồng ký gửi #CN-2026 đã tiếp nhận 5 sản phẩm đạt chuẩn niêm yết tại cửa hàng.",
                NotificationType.System,
                "Consignment"),
            "Buyout" => (
                "Hoàn tất thu mua sản phẩm",
                "Phiếu thu mua đứt #BO-2026 đã được phê duyệt với tổng giá trị thanh toán 1.250.000đ.",
                NotificationType.System,
                "Buyout"),
            "Payment" => (
                "Giải ngân tiền ký gửi thành công",
                "Khoản thanh toán 850.000đ cho hợp đồng #CN-2026 đã được chuyển khoản qua VietQR.",
                NotificationType.System,
                "Payment"),
            "Promotion" => (
                "Ưu đãi ký gửi cuối tuần tại OCS",
                "Giảm 5% phí hoa hồng ký gửi cho các sản phẩm Local Brand & Luxury trong tuần này.",
                NotificationType.Promotion,
                "Promotion"),
            _ => (
                "Kiểm tra thông báo OCS",
                "Thông báo thử nghiệm từ hệ thống Notification Design System của OCS.",
                NotificationType.System,
                "System")
        };

        var result = await notificationService.CreateAsync(new CreateNotificationDto
        {
            RecipientAccountId = userId,
            SenderAccountId = userId,
            Title = title,
            Content = content,
            Type = type,
            Category = resolvedCategory
        }, cancellationToken);

        return result.ToActionResult(this);
    }

    private Guid GetCurrentUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}
