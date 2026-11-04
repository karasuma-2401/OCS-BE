using Microsoft.EntityFrameworkCore;
using OCS.Api.Common;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Repositories;

namespace OCS.Api.Repositories;

public sealed class AuthRepository(AppDbContext dbContext) : IAuthRepository
{
    public Task<Account?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalized = EmailIdentity.Normalize(email);
        return normalized is null
            ? Task.FromResult<Account?>(null)
            : dbContext.Accounts
                .Include(a => a.AccountRoles).ThenInclude(ar => ar.Role)
                .Include(a => a.EmployeeProfile)
                .Include(a => a.CustomerProfile)
                .SingleOrDefaultAsync(a => a.NormalizedEmail == normalized, cancellationToken);
    }

    public Task<bool> ExistsEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalized = EmailIdentity.Normalize(email);
        return normalized is null
            ? Task.FromResult(false)
            : dbContext.Accounts.AnyAsync(a => a.NormalizedEmail == normalized, cancellationToken);
    }

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

    public Task<OtpChallenge?> GetOtpChallengeByIdAsync(Guid challengeId, CancellationToken cancellationToken = default) =>
        dbContext.OtpChallenges
            .Include(c => c.Account)
            .SingleOrDefaultAsync(c => c.Id == challengeId, cancellationToken);

    public Task<int> CountOtpChallengesInWindowAsync(string normalizedEmail, DateTimeOffset windowStart, CancellationToken cancellationToken = default) =>
        dbContext.OtpChallenges
            .CountAsync(c => c.NormalizedEmail == normalizedEmail && c.CreatedAt >= windowStart, cancellationToken);

    public Task<OtpChallenge?> GetLatestOtpChallengeAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        dbContext.OtpChallenges.Where(c => c.AccountId == accountId && c.Purpose == "RegistrationActivation" && c.DeliveryStatus != "Failed")
            .OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync(cancellationToken);

    public Task<List<OtpChallenge>> GetActiveOtpChallengesAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        dbContext.OtpChallenges.Where(c => c.AccountId == accountId && c.Purpose == "RegistrationActivation"
            && c.ConsumedAt == null && c.InvalidatedAt == null).ToListAsync(cancellationToken);

    public Task LockAccountAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"TAIKHOAN\" WHERE \"Id\" = {accountId} FOR UPDATE", cancellationToken);

    public Task ReloadChallengeAsync(OtpChallenge challenge, CancellationToken cancellationToken = default) =>
        dbContext.Entry(challenge).ReloadAsync(cancellationToken);

    public void AddOtpChallenge(OtpChallenge challenge)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        dbContext.OtpChallenges.Add(challenge);
    }

    public Task<AuthSession?> GetSessionByIdAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        dbContext.AuthSessions
            .Include(s => s.Account)
                .ThenInclude(a => a.AccountRoles)
                    .ThenInclude(ar => ar.Role)
            .SingleOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

    public void AddSession(AuthSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        dbContext.AuthSessions.Add(session);
    }
}
