using OCS.Api.Common;
using OCS.Api.Models.Notifications;

namespace OCS.Api.Interfaces.Services;

public interface INotificationService
{
    Task<Result<NotificationDto>> CreateAsync(CreateNotificationDto dto, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationDto>> GetForUserAsync(Guid accountId, int limit = 50, CancellationToken cancellationToken = default);

    Task<int> GetUnreadCountAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task<Result<bool>> MarkAsReadAsync(Guid notificationId, Guid accountId, CancellationToken cancellationToken = default);

    Task<Result<bool>> MarkAllAsReadAsync(Guid accountId, CancellationToken cancellationToken = default);
}

