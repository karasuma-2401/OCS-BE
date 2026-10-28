using Microsoft.EntityFrameworkCore;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Repositories;

namespace OCS.Api.Repositories;

public sealed class AuthRepository(AppDbContext dbContext) : IAuthRepository
{
    public Task<Account?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default) =>
        dbContext.Accounts
            .Include(a => a.AccountRoles)
                .ThenInclude(ar => ar.Role)
            .Include(a => a.EmployeeProfile)
            .Include(a => a.CustomerProfile)
            .SingleOrDefaultAsync(a => a.Username == username, cancellationToken);

    public Task<Account?> GetByPhoneAsync(string phone, CancellationToken cancellationToken = default) =>
        dbContext.Accounts
            .Include(a => a.AccountRoles)
                .ThenInclude(ar => ar.Role)
            .Include(a => a.EmployeeProfile)
            .Include(a => a.CustomerProfile)
            .SingleOrDefaultAsync(a => a.PhoneNumber == phone, cancellationToken);

    public Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Accounts
            .Include(a => a.AccountRoles)
                .ThenInclude(ar => ar.Role)
            .Include(a => a.EmployeeProfile)
            .Include(a => a.CustomerProfile)
            .SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<bool> ExistsUsernameAsync(string username, CancellationToken cancellationToken = default) =>
        dbContext.Accounts.AnyAsync(a => a.Username == username, cancellationToken);

    public Task<bool> ExistsPhoneAsync(string phone, CancellationToken cancellationToken = default) =>
        dbContext.Accounts.AnyAsync(a => a.PhoneNumber == phone, cancellationToken);

    public void AddAccount(Account account)
    {
        ArgumentNullException.ThrowIfNull(account);
        dbContext.Accounts.Add(account);
    }
}
