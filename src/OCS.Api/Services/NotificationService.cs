using OCS.Api.Common;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Repositories;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Notifications;

namespace OCS.Api.Services;

public sealed class NotificationService(IUnitOfWork unitOfWork) : INotificationService
{
    public async Task<Result<NotificationDto>> CreateAsync(CreateNotificationDto dto, CancellationToken cancellationToken = default)
    {
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            RecipientAccountId = dto.RecipientAccountId,
            SenderAccountId = dto.SenderAccountId,
            Title = dto.Title,
            Content = dto.Content,
            Type = dto.Type,
            SentAt = DateTimeOffset.UtcNow,
            IsRead = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

        unitOfWork.NotificationRepository.Add(notification);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(MapToDto(notification));
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
