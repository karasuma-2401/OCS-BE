using OCS.Api.Context;
using OCS.Api.Interfaces.Repositories;
using OCS.Api.Entities;
using Microsoft.EntityFrameworkCore;

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

    public async Task<bool> ReloadAccountAsync(Account account, CancellationToken cancellationToken = default)
    {
        await dbContext.Entry(account).ReloadAsync(cancellationToken);
        return dbContext.Entry(account).State != EntityState.Detached;
    }

    public void Dispose() => dbContext.Dispose();
}

