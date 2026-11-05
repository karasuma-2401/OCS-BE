using OCS.Api.Entities;

namespace OCS.Api.Interfaces.Repositories;

public interface IUnitOfWork : IDisposable
{
    IProductRepository ProductRepository { get; }
    IAuthRepository AuthRepository { get; }
    IAppointmentRepository AppointmentRepository { get; }
    INotificationRepository NotificationRepository { get; }
    ICustomerProfileRepository CustomerProfileRepository { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
    Task<bool> ReloadAccountAsync(Account account, CancellationToken cancellationToken = default);
}
