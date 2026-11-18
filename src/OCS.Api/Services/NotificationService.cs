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

        var response = MapToDto(notification);
        if (fcmService is not null)
        {
            try
            {
                response.PushDelivery = await fcmService.SendPushToUserAsync(
                    dto.RecipientAccountId,
                    dto.Title,
                    dto.Content,
                    new Dictionary<string, string>
                    {
                        ["notificationId"] = notification.Id.ToString(),
                        ["type"] = dto.Type.ToString(),
                        ["recipientAccountId"] = dto.RecipientAccountId.ToString()
                    },
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger?.LogWarning(ex, "Push dispatch failed for notification {NotificationId}", notification.Id);
            }
        }

        return Result.Success(response);
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
