using OCS.Api.Entities;

namespace OCS.Api.Interfaces.Repositories;

public interface IAuthRepository
{
    Task<Account?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<bool> ExistsEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<Account?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);
    Task<Account?> GetByPhoneAsync(string phone, CancellationToken cancellationToken = default);
    Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsUsernameAsync(string username, CancellationToken cancellationToken = default);
    Task<bool> ExistsPhoneAsync(string phone, CancellationToken cancellationToken = default);
    void AddAccount(Account account);
}
