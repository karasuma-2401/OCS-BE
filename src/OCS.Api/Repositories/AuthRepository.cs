using Microsoft.EntityFrameworkCore;
using OCS.Api.Common;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Repositories;

namespace OCS.Api.Repositories;

public sealed class AuthRepository(AppDbContext dbContext) : IAuthRepository
{
    public async Task<PagedResponse<Account>> GetPagedAccountsAsync(
        PagedRequest paging,
        string? search,
        AccountType? accountType,
        AccountStatus? status,
        bool staffOnly,
        Guid? roleId,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Account> query = dbContext.Accounts.AsNoTracking();

        if (accountType.HasValue)
            query = query.Where(a => a.AccountType == accountType.Value);

        if (status.HasValue)
            query = query.Where(a => a.Status == status.Value);

        if (staffOnly)
            query = query.Where(a => a.AccountType == AccountType.Internal
                && a.AccountRoles.Any(ar => ar.RoleId == Role.StaffRoleId)
                && !a.AccountRoles.Any(ar => ar.RoleId == Role.AdminRoleId || ar.RoleId == Role.ManagerRoleId));

        if (roleId.HasValue)
            query = query.Where(a => a.AccountRoles.Any(ar => ar.RoleId == roleId.Value));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(a =>
                EF.Functions.ILike(a.FullName, pattern) ||
                (a.Email != null && EF.Functions.ILike(a.Email, pattern)) ||
                EF.Functions.ILike(a.PhoneNumber, pattern) ||
                (a.Username != null && EF.Functions.ILike(a.Username, pattern)));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .ThenBy(a => a.Id)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(a => new Account
            {
                Id = a.Id,
                FullName = a.FullName,
                Email = a.Email,
                PhoneNumber = a.PhoneNumber,
                AccountType = a.AccountType,
                Status = a.Status,
                Username = a.Username,
                CreatedAt = a.CreatedAt,
                AccountRoles = a.AccountRoles.Select(ar => new AccountRole
                {
                    RoleId = ar.RoleId,
                    Role = new Role { Code = ar.Role.Code }
                }).ToList()
            })
            .ToListAsync(cancellationToken);

        return new PagedResponse<Account>(items, paging.Page, paging.PageSize, totalCount);
    }

    public async Task<PagedResponse<AuditLog>> GetPagedAccountAuditLogsAsync(
        PagedRequest paging, Guid? accountId, CancellationToken cancellationToken = default)
    {
        IQueryable<AuditLog> query = dbContext.AuditLogs.AsNoTracking()
            .Where(log => log.EntityType == nameof(Account)
                || log.EntityType == nameof(AccountRole)
                || log.EntityType == nameof(EmployeeProfile));

        if (accountId.HasValue)
        {
            var id = accountId.Value.ToString("D");
            var roleEntityIdPart = $"\"accountId\":\"{id}\"";
            var roleEntityIdPartPascal = $"\"AccountId\":\"{id}\"";
            query = query.Where(log =>
                log.EntityType == nameof(Account) && log.EntityId == id
                || log.EntityType == nameof(EmployeeProfile) && log.EntityId == id
                || log.EntityType == nameof(AccountRole)
                    && (log.EntityId.Contains(roleEntityIdPart)
                        || log.EntityId.Contains(roleEntityIdPartPascal)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(log => log.OccurredAt)
            .ThenByDescending(log => log.Id)
            .Skip(paging.Skip).Take(paging.PageSize)
            .ToListAsync(cancellationToken);
        return new PagedResponse<AuditLog>(items, paging.Page, paging.PageSize, totalCount);
    }

    public void AddAuditLog(AuditLog auditLog) => dbContext.AuditLogs.Add(auditLog);

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

    public Task<Account?> GetListRequesterAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Accounts.AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new Account
            {
                Id = a.Id,
                Status = a.Status,
                EmailVerifiedAt = a.EmailVerifiedAt,
                AccountRoles = a.AccountRoles.Select(ar => new AccountRole { RoleId = ar.RoleId }).ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);

    public Task<bool> ExistsUsernameAsync(string username, CancellationToken cancellationToken = default) =>
        dbContext.Accounts.AnyAsync(a => a.Username == username, cancellationToken);

    public Task<bool> ExistsPhoneAsync(string phone, CancellationToken cancellationToken = default) =>
        dbContext.Accounts.AnyAsync(a => a.PhoneNumber == phone, cancellationToken);

    public void AddAccount(Account account)
    {
        ArgumentNullException.ThrowIfNull(account);
        dbContext.Accounts.Add(account);
    }

    public void ReplaceAccountRoles(Account account, IReadOnlyCollection<Guid> roleIds)
    {
        var removed = account.AccountRoles.Where(ar => !roleIds.Contains(ar.RoleId)).ToList();
        dbContext.AccountRoles.RemoveRange(removed);
        foreach (var role in removed)
            account.AccountRoles.Remove(role);

        foreach (var roleId in roleIds.Where(id => account.AccountRoles.All(ar => ar.RoleId != id)))
            account.AccountRoles.Add(new AccountRole { AccountId = account.Id, RoleId = roleId, Account = account });
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
        dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Accounts\" WHERE \"Id\" = {accountId} FOR UPDATE", cancellationToken);

    public Task LockRoleAsync(Guid roleId, CancellationToken cancellationToken = default) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Roles\" WHERE \"Id\" = {roleId} FOR UPDATE", cancellationToken);

    public Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken = default) =>
        dbContext.Accounts.CountAsync(a => a.Status == AccountStatus.Active && a.EmailVerifiedAt != null
            && a.AccountRoles.Any(ar => ar.RoleId == Role.AdminRoleId), cancellationToken);

    public Task<int> RevokeSessionsAsync(Guid accountId, DateTimeOffset revokedAt, CancellationToken cancellationToken = default) =>
        dbContext.AuthSessions.Where(s => s.AccountId == accountId && s.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.RevokedAt, revokedAt), cancellationToken);

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

    public Task<List<string>> GetEffectivePermissionsAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        dbContext.AccountRoles
            .Where(accountRole => accountRole.AccountId == accountId)
            .SelectMany(accountRole => accountRole.Role.RolePermissions.Select(rolePermission => rolePermission.Permission.Code))
            .Distinct()
            .OrderBy(code => code)
            .ToListAsync(cancellationToken);

    public void AddSession(AuthSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        dbContext.AuthSessions.Add(session);
    }
}
