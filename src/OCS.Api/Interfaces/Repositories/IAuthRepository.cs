using OCS.Api.Common;
using OCS.Api.Consts;
using OCS.Api.Entities;

namespace OCS.Api.Interfaces.Repositories;

public interface IAuthRepository
{
    Task<PagedResponse<Account>> GetPagedAccountsAsync(
        PagedRequest paging,
        string? search,
        AccountType? accountType,
        AccountStatus? status,
        bool staffOnly,
        CancellationToken cancellationToken = default);

    Task<Account?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<bool> ExistsEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<Account?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);
    Task<Account?> GetByPhoneAsync(string phone, CancellationToken cancellationToken = default);
    Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsUsernameAsync(string username, CancellationToken cancellationToken = default);
    Task<bool> ExistsPhoneAsync(string phone, CancellationToken cancellationToken = default);
    void AddAccount(Account account);
    void ReplaceAccountRoles(Account account, IReadOnlyCollection<Guid> roleIds);

    Task<OtpChallenge?> GetOtpChallengeByIdAsync(Guid challengeId, CancellationToken cancellationToken = default);
    Task<int> CountOtpChallengesInWindowAsync(string normalizedEmail, DateTimeOffset windowStart, CancellationToken cancellationToken = default);
    Task<OtpChallenge?> GetLatestOtpChallengeAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task<List<OtpChallenge>> GetActiveOtpChallengesAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task LockAccountAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task LockRoleAsync(Guid roleId, CancellationToken cancellationToken = default);
    Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken = default);
    Task<int> RevokeSessionsAsync(Guid accountId, DateTimeOffset revokedAt, CancellationToken cancellationToken = default);
    Task ReloadChallengeAsync(OtpChallenge challenge, CancellationToken cancellationToken = default);
    void AddOtpChallenge(OtpChallenge challenge);

    Task<AuthSession?> GetSessionByIdAsync(Guid sessionId, CancellationToken cancellationToken = default);
    void AddSession(AuthSession session);
}
