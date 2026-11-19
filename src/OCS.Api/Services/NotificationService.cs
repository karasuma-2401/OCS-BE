using Microsoft.Extensions.Logging;
using OCS.Api.Common;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Repositories;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Notifications;

namespace OCS.Api.Services;

public sealed class NotificationService(
    IUnitOfWork unitOfWork,
    IFcmService? fcmService = null,
    ILogger<NotificationService>? logger = null) : INotificationService
{
    public async Task<Result<NotificationDto>> CreateAsync(CreateNotificationDto dto, CancellationToken cancellationToken = default)
    {
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            RecipientAccountId = dto.RecipientAccountId,
            SenderAccountId = dto.SenderAccountId.HasValue && dto.SenderAccountId.Value != Guid.Empty
                ? dto.SenderAccountId
                : null,
            Title = dto.Title,
            Content = dto.Content,
            Type = dto.Type,
            SentAt = DateTimeOffset.UtcNow,
            IsRead = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

        unitOfWork.NotificationRepository.Add(notification);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var (resolvedCategory, resolvedLink) = ResolveCategoryAndLink(notification, dto.Category, dto.Link);

        FcmDispatchResult? pushResult = null;
        if (fcmService is not null)
        {
            try
            {
                var pushData = new Dictionary<string, string>
                {
                    ["notificationId"] = notification.Id.ToString(),
                    ["type"] = dto.Type.ToString(),
                    ["category"] = resolvedCategory,
                    ["title"] = dto.Title,
                    ["body"] = dto.Content,
                };

                if (!string.IsNullOrWhiteSpace(dto.Link))
                {
                    pushData["link"] = dto.Link;
                    pushData["url"] = dto.Link;
                }

                if (dto.ExtraData is not null)
                {
                    foreach (var (k, v) in dto.ExtraData)
                    {
                        if (!string.IsNullOrWhiteSpace(k) && v is not null)
                        {
                            pushData[k] = v;
                        }
                    }
                }

                // Custom data must not change the notification identity or recipient.
                pushData["notificationId"] = notification.Id.ToString();
                pushData["type"] = dto.Type.ToString();
                pushData["recipientAccountId"] = dto.RecipientAccountId.ToString();

                pushResult = await fcmService.SendPushToUserAsync(
                    dto.RecipientAccountId,
                    dto.Title,
                    dto.Content,
                    pushData,
                    cancellationToken);

                if (logger is not null)
                {
                    if (!pushResult.FirebaseConfigured)
                    {
                        if (logger.IsEnabled(LogLevel.Information))
                        {
                            logger.LogInformation(
                                "FCM push not dispatched for account {AccountId}: Firebase credentials not configured on server.",
                                dto.RecipientAccountId);
                        }
                    }
                    else if (pushResult.TargetedDeviceCount == 0)
                    {
                        if (logger.IsEnabled(LogLevel.Information))
                        {
                            logger.LogInformation(
                                "FCM push skipped for account {AccountId}: No registered device tokens found. Saved to DB notifications.",
                                dto.RecipientAccountId);
                        }
                    }
                    else if (pushResult.FailureCount > 0)
                    {
                        if (logger.IsEnabled(LogLevel.Warning))
                        {
                            logger.LogWarning(
                                "FCM push dispatched for account {AccountId} with partial/total failures: {SuccessCount}/{TotalCount} succeeded, {FailureCount} failed.",
                                dto.RecipientAccountId,
                                pushResult.SuccessCount,
                                pushResult.TargetedDeviceCount,
                                pushResult.FailureCount);
                        }
                    }
                    else if (logger.IsEnabled(LogLevel.Information))
                    {
                        logger.LogInformation(
                            "FCM push dispatched successfully to {SuccessCount} device(s) for account {AccountId}.",
                            pushResult.SuccessCount,
                            dto.RecipientAccountId);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger?.LogError(ex, "Exception occurred while dispatching FCM push notification to account {AccountId}", dto.RecipientAccountId);
                pushResult = new FcmDispatchResult(0, 0, 1, fcmService.IsConfigured);
            }
        }

        var dtoResult = MapToDto(notification, resolvedCategory, resolvedLink);
        dtoResult.PushResult = pushResult;
        return Result.Success(dtoResult);
    }

    public async Task<IReadOnlyList<NotificationDto>> GetForUserAsync(
        Guid accountId,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var list = await unitOfWork.NotificationRepository.GetByRecipientAccountIdAsync(accountId, limit, cancellationToken);
        return list.Select(n => MapToDto(n)).ToList();
    }

    public Task<int> GetUnreadCountAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        unitOfWork.NotificationRepository.GetUnreadCountAsync(accountId, cancellationToken);

    public async Task<Result<bool>> MarkAsReadAsync(Guid notificationId, Guid accountId, CancellationToken cancellationToken = default)
    {
        var notification = await unitOfWork.NotificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null || notification.RecipientAccountId != accountId)
        {
            return Result.Failure<bool>(ApplicationError.NotFound("NOTIFICATION_NOT_FOUND", "Không tìm thấy thông báo."));
        }

        notification.IsRead = true;
        notification.UpdatedAt = DateTimeOffset.UtcNow;
        unitOfWork.NotificationRepository.Update(notification);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(true);
    }

    public async Task<Result<bool>> MarkAllAsReadAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var list = await unitOfWork.NotificationRepository.GetUnreadByRecipientAccountIdAsync(accountId, cancellationToken);
        foreach (var noti in list)
        {
            noti.IsRead = true;
            noti.UpdatedAt = DateTimeOffset.UtcNow;
            unitOfWork.NotificationRepository.Update(noti);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(true);
    }

    private static NotificationDto MapToDto(
        Notification n,
        string? explicitCategory = null,
        string? explicitLink = null)
    {
        var (category, link) = ResolveCategoryAndLink(n, explicitCategory, explicitLink);
        return new()
        {
            Id = n.Id,
            Title = n.Title,
            Content = n.Content,
            Type = n.Type,
            Category = category,
            Link = link,
            SentAt = n.SentAt,
            IsRead = n.IsRead
        };
    }

    private static (string Category, string? Link) ResolveCategoryAndLink(
        Notification n,
        string? explicitCategory = null,
        string? explicitLink = null)
    {
        var titleLower = (n.Title ?? string.Empty).ToLowerInvariant();
        var contentLower = (n.Content ?? string.Empty).ToLowerInvariant();
        var combined = $"{titleLower} {contentLower}";

        string category;
        if (!string.IsNullOrWhiteSpace(explicitCategory))
        {
            category = explicitCategory.Trim();
        }
        else if (n.Type == Consts.NotificationType.Promotion ||
                 combined.Contains("ưu đãi", StringComparison.Ordinal) ||
                 combined.Contains("khuyến mãi", StringComparison.Ordinal) ||
                 combined.Contains("voucher", StringComparison.Ordinal))
        {
            category = "Promotion";
        }
        else if (combined.Contains("thu mua", StringComparison.Ordinal) ||
                 combined.Contains("buyout", StringComparison.Ordinal))
        {
            category = "Buyout";
        }
        else if (combined.Contains("thanh toán", StringComparison.Ordinal) ||
                 combined.Contains("giải ngân", StringComparison.Ordinal) ||
                 combined.Contains("quyết toán", StringComparison.Ordinal) ||
                 combined.Contains("hóa đơn", StringComparison.Ordinal) ||
                 combined.Contains("vietqr", StringComparison.Ordinal))
        {
            category = "Payment";
        }
        else if (combined.Contains("lịch hẹn", StringComparison.Ordinal) ||
                 combined.Contains("đặt lịch", StringComparison.Ordinal) ||
                 combined.Contains("khung giờ", StringComparison.Ordinal) ||
                 combined.Contains("có mặt tại quầy", StringComparison.Ordinal))
        {
            category = "Appointment";
        }
        else if (combined.Contains("ký gửi", StringComparison.Ordinal) ||
                 combined.Contains("hợp đồng", StringComparison.Ordinal) ||
                 combined.Contains("tiếp nhận", StringComparison.Ordinal))
        {
            category = "Consignment";
        }
        else
        {
            category = "System";
        }

        string? link = !string.IsNullOrWhiteSpace(explicitLink) ? explicitLink.Trim() : null;
        if (string.IsNullOrWhiteSpace(link))
        {
            var isStaffTargeted = titleLower.StartsWith("khách ", StringComparison.Ordinal) ||
                                  contentLower.Contains("của khách ", StringComparison.Ordinal) ||
                                  combined.Contains("cần duyệt", StringComparison.Ordinal);

            link = category switch
            {
                "Appointment" => isStaffTargeted ? "/shop/consignment" : "/appointments",
                "Consignment" => isStaffTargeted ? "/shop/consignment" : "/my-consignments",
                "Buyout" => isStaffTargeted ? "/shop/buyout" : "/my-consignments",
                "Payment" => isStaffTargeted ? "/shop/settlement" : "/my-consignments",
                "Promotion" => "/catalog",
                _ => null
            };
        }

        return (category, link);
    }
}
