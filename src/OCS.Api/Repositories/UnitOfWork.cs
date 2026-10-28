using OCS.Api.Context;
using OCS.Api.Interfaces.Repositories;

namespace OCS.Api.Repositories;

public sealed class UnitOfWork(
    AppDbContext dbContext,
    IProductRepository productRepository,
    IAuthRepository authRepository) : IUnitOfWork
{
    public IProductRepository ProductRepository => productRepository;
    public IAuthRepository AuthRepository => authRepository;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Database.BeginTransactionAsync(cancellationToken);

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Database.CommitTransactionAsync(cancellationToken);

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Database.RollbackTransactionAsync(cancellationToken);

    public void Dispose() => dbContext.Dispose();
}

