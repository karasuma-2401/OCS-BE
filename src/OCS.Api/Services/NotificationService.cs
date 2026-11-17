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

        FcmDispatchResult? pushResult = null;
        if (fcmService is not null)
        {
            try
            {
                var pushData = new Dictionary<string, string>
                {
                    ["notificationId"] = notification.Id.ToString(),
                    ["type"] = dto.Type.ToString(),
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
            catch (Exception ex)
            {
                logger?.LogError(ex, "Exception occurred while dispatching FCM push notification to account {AccountId}", dto.RecipientAccountId);
                pushResult = new FcmDispatchResult(0, 0, 1, fcmService.IsConfigured);
            }
        }

        var dtoResult = MapToDto(notification);
        dtoResult.PushResult = pushResult;
        return Result.Success(dtoResult);
    }

    public async Task<IReadOnlyList<NotificationDto>> GetForUserAsync(
        Guid accountId,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var list = await unitOfWork.NotificationRepository.GetByRecipientAccountIdAsync(accountId, limit, cancellationToken);
        return list.Select(MapToDto).ToList();
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
        var list = await unitOfWork.NotificationRepository.GetByRecipientAccountIdAsync(accountId, 100, cancellationToken);
        foreach (var noti in list.Where(n => !n.IsRead))
        {
            noti.IsRead = true;
            noti.UpdatedAt = DateTimeOffset.UtcNow;
            unitOfWork.NotificationRepository.Update(noti);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(true);
    }

    private static NotificationDto MapToDto(Notification n) => new()
    {
        Id = n.Id,
        Title = n.Title,
        Content = n.Content,
        Type = n.Type,
        SentAt = n.SentAt,
        IsRead = n.IsRead
    };
}
