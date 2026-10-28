namespace OCS.Api.Interfaces.Repositories;

public interface IUnitOfWork : IDisposable
{
    IProductRepository ProductRepository { get; }
    IAuthRepository AuthRepository { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
}

