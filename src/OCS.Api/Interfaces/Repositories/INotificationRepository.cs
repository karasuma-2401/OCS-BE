using OCS.Api.Entities;

namespace OCS.Api.Interfaces.Repositories;

public interface INotificationRepository
{
    Task<IReadOnlyList<Notification>> GetByRecipientAccountIdAsync(Guid accountId, int limit = 50, CancellationToken cancellationToken = default);

    Task<int> GetUnreadCountAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task<Notification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(Notification notification);

    void Update(Notification notification);
}

