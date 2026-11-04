using Microsoft.EntityFrameworkCore;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Repositories;

namespace OCS.Api.Repositories;

public sealed class NotificationRepository(AppDbContext dbContext) : INotificationRepository
{
    public async Task<IReadOnlyList<Notification>> GetByRecipientAccountIdAsync(
        Guid accountId,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Notifications
            .Where(n => n.RecipientAccountId == accountId)
            .OrderByDescending(n => n.SentAt)
            .Take(limit)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public Task<int> GetUnreadCountAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        dbContext.Notifications
            .CountAsync(n => n.RecipientAccountId == accountId && !n.IsRead, cancellationToken);

    public Task<Notification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Notifications
            .SingleOrDefaultAsync(n => n.Id == id, cancellationToken);

    public void Add(Notification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        dbContext.Notifications.Add(notification);
    }

    public void Update(Notification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        dbContext.Notifications.Update(notification);
    }
}

