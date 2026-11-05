using OCS.Api.Common;
using OCS.Api.Consts;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Repositories;
using OCS.Api.Services;
using OCS.Api.Models.Accounts;

namespace OCS.Api.IntegrationTests;

public sealed class AccountManagementServiceTests
{
    [Fact]
    public async Task AdminCanUpdateAccountIdentityAndEmailRequiresActivation()
    {
        var admin = CreateAccount(Role.AdminRoleId, "Admin");
        var staff = CreateAccount(Role.StaffRoleId, "Staff");
        staff.Email = "old@example.com";
        staff.Username = "old_staff";
        using var unitOfWork = new TestUnitOfWork(admin, staff);
        var service = new AccountManagementService(unitOfWork, new PasswordHasher());

        var result = await service.UpdateAccountInfoAsync(admin.Id, staff.Id,
            new UpdateAccountInfoRequest("New Name", "new@example.com", "0900000000", "new_staff"));

        Assert.True(result.IsSuccess);
        Assert.Equal("New Name", staff.FullName);
        Assert.Equal("new_staff", result.Data!.Username);
        Assert.Equal(AccountStatus.PendingActivation, staff.Status);
        Assert.Null(staff.EmailVerifiedAt);
        Assert.Equal(staff.Id, unitOfWork.Auth.SessionsRevokedFor);
    }

    [Fact]
    public async Task ChangingOnlyContactInfoDoesNotRevokeSessions()
    {
        var admin = CreateAccount(Role.AdminRoleId, "Admin");
        var staff = CreateAccount(Role.StaffRoleId, "Staff");
        staff.Email = "staff@example.com";
        staff.Username = "staff";
        using var unitOfWork = new TestUnitOfWork(admin, staff);
        var service = new AccountManagementService(unitOfWork, new PasswordHasher());

        var result = await service.UpdateAccountInfoAsync(admin.Id, staff.Id,
            new UpdateAccountInfoRequest("New Name", "STAFF@example.com", "0900000000", "staff"));

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountStatus.Active, staff.Status);
        Assert.NotNull(staff.EmailVerifiedAt);
        Assert.Null(unitOfWork.Auth.SessionsRevokedFor);
    }

    [Fact]
    public async Task ManagerCannotUpdateAccountIdentity()
    {
        var manager = CreateAccount(Role.ManagerRoleId, "Manager");
        var staff = CreateAccount(Role.StaffRoleId, "Staff");
        staff.Username = "staff";
        using var unitOfWork = new TestUnitOfWork(manager, staff);
        var service = new AccountManagementService(unitOfWork, new PasswordHasher());

        var result = await service.UpdateAccountInfoAsync(manager.Id, staff.Id,
            new UpdateAccountInfoRequest("New Name", "new@example.com", "0900000000", "new_staff"));

        Assert.Equal("ACCOUNT.FORBIDDEN", result.Error?.Code);
    }

    [Fact]
    public async Task RootAdminIdentityCannotBeChanged()
    {
        var admin = CreateAccount(Role.AdminRoleId, "Admin");
        var root = CreateAccount(Role.AdminRoleId, "Admin");
        root.Username = "ocs_admin";
        root.Email = "root@example.com";
        using var unitOfWork = new TestUnitOfWork(admin, root);
        var service = new AccountManagementService(unitOfWork, new PasswordHasher());

        var result = await service.UpdateAccountInfoAsync(admin.Id, root.Id,
            new UpdateAccountInfoRequest("Root", "other@example.com", "0900000000", "ocs_admin"));

        Assert.Equal("ACCOUNT.PROTECTED_ADMIN", result.Error?.Code);
        Assert.Equal("root@example.com", root.Email);
    }

    [Fact]
    public async Task LastActiveAdminCannotChangeEmail()
    {
        var admin = CreateAccount(Role.AdminRoleId, "Admin");
        admin.Email = "admin@example.com";
        admin.Username = "admin";
        using var unitOfWork = new TestUnitOfWork(admin);
        var service = new AccountManagementService(unitOfWork, new PasswordHasher());

        var result = await service.UpdateAccountInfoAsync(admin.Id, admin.Id,
            new UpdateAccountInfoRequest("Admin", "other@example.com", "0900000000", "admin"));

        Assert.Equal("ACCOUNT.LAST_ADMIN", result.Error?.Code);
        Assert.Equal(AccountStatus.Active, admin.Status);
    }

    [Fact]
    public async Task RootAdminCannotBeLockedDeactivatedOrDemoted()
    {
        var admin = CreateAccount(Role.AdminRoleId, "Admin");
        var root = CreateAccount(Role.AdminRoleId, "Admin");
        root.Username = "ocs_admin";
        using var unitOfWork = new TestUnitOfWork(admin, root);
        var service = new AccountManagementService(unitOfWork, new PasswordHasher());

        var lockResult = await service.ChangeLockStatusAsync(admin.Id, root.Id,
            new ChangeAccountLockRequest(true));
        var statusResult = await service.ChangeStatusAsync(admin.Id, root.Id,
            new ChangeAccountStatusRequest("Deactivated"));
        var roleResult = await service.UpdateRolesAsync(admin.Id, root.Id,
            new UpdateAccountRolesRequest(["Staff"]));

        Assert.Equal("ACCOUNT.PROTECTED_ADMIN", lockResult.Error?.Code);
        Assert.Equal("ACCOUNT.PROTECTED_ADMIN", statusResult.Error?.Code);
        Assert.Equal("ACCOUNT.PROTECTED_ADMIN", roleResult.Error?.Code);
        Assert.Equal(AccountStatus.Active, root.Status);
        Assert.Equal(Role.AdminRoleId, root.AccountRoles.Single().RoleId);
        Assert.Null(unitOfWork.Auth.SessionsRevokedFor);
    }

    [Fact]
    public async Task AdminCanCreateManagerPendingActivation()
    {
        var admin = CreateAccount(Role.AdminRoleId, "Admin");
        using var unitOfWork = new TestUnitOfWork(admin);
        var service = new AccountManagementService(unitOfWork, new PasswordHasher());
        var request = new CreateInternalAccountRequest("Manager A", "manager@example.com",
            "0900000000", "manager_a", "Example123!", "Quản lý",
            new DateOnly(2026, 11, 5), "Manager");

        var result = await service.CreateInternalAccountAsync(admin.Id, request);

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountStatus.PendingActivation, unitOfWork.Auth.CreatedAccount!.Status);
        Assert.Equal(Role.ManagerRoleId, unitOfWork.Auth.CreatedAccount.AccountRoles.Single().RoleId);
        Assert.Equal("Manager", result.Data!.Roles.Single());
    }

    [Fact]
    public async Task ManagerCannotCreateAdmin()
    {
        var manager = CreateAccount(Role.ManagerRoleId, "Manager");
        using var unitOfWork = new TestUnitOfWork(manager);
        var service = new AccountManagementService(unitOfWork, new PasswordHasher());
        var request = new CreateInternalAccountRequest("Admin A", "admin@example.com",
            "0900000000", "admin_a", "Example123!", "Admin",
            new DateOnly(2026, 11, 5), "Admin");

        var result = await service.CreateInternalAccountAsync(manager.Id, request);

        Assert.Equal("ACCOUNT.FORBIDDEN", result.Error?.Code);
        Assert.Null(unitOfWork.Auth.CreatedAccount);
    }

    [Fact]
    public async Task DeactivatingStaffRevokesSessionsAndReactivationRestoresActive()
    {
        var admin = CreateAccount(Role.AdminRoleId, "Admin");
        var staff = CreateAccount(Role.StaffRoleId, "Staff");
        using var unitOfWork = new TestUnitOfWork(admin, staff);
        var service = new AccountManagementService(unitOfWork, new PasswordHasher());

        var disabled = await service.ChangeStatusAsync(admin.Id, staff.Id,
            new ChangeAccountStatusRequest("Deactivated"));
        Assert.True(disabled.IsSuccess);
        Assert.Equal(AccountStatus.Deactivated, staff.Status);
        Assert.Equal(staff.Id, unitOfWork.Auth.SessionsRevokedFor);

        var enabled = await service.ChangeStatusAsync(admin.Id, staff.Id,
            new ChangeAccountStatusRequest("Active"));
        Assert.True(enabled.IsSuccess);
        Assert.Equal(AccountStatus.Active, staff.Status);
    }

    [Fact]
    public async Task ManagerCannotDeactivateManager()
    {
        var manager = CreateAccount(Role.ManagerRoleId, "Manager");
        var target = CreateAccount(Role.ManagerRoleId, "Manager");
        using var unitOfWork = new TestUnitOfWork(manager, target);
        var service = new AccountManagementService(unitOfWork, new PasswordHasher());

        var result = await service.ChangeStatusAsync(manager.Id, target.Id,
            new ChangeAccountStatusRequest("Deactivated"));

        Assert.Equal("ACCOUNT.NOT_FOUND", result.Error?.Code);
        Assert.Equal(AccountStatus.Active, target.Status);
    }

    [Fact]
    public async Task ManagerCanUpdateStaffProfile()
    {
        var manager = CreateAccount(Role.ManagerRoleId, "Manager");
        var staff = CreateAccount(Role.StaffRoleId, "Staff");
        staff.EmployeeProfile = new EmployeeProfile { Id = staff.Id, JobTitle = "Cũ", HiredOn = new DateOnly(2026, 1, 1) };
        using var unitOfWork = new TestUnitOfWork(manager, staff);
        var service = new AccountManagementService(unitOfWork, new PasswordHasher());

        var result = await service.UpdateEmployeeProfileAsync(manager.Id, staff.Id,
            new UpdateEmployeeProfileRequest("Tên mới", "0900000000", "Bán hàng", new DateOnly(2026, 2, 1)));

        Assert.True(result.IsSuccess);
        Assert.Equal("Tên mới", staff.FullName);
        Assert.Equal("Bán hàng", result.Data!.JobTitle);
    }

    [Fact]
    public async Task AdminCanReplaceStaffRoleAndRevokeSessions()
    {
        var admin = CreateAccount(Role.AdminRoleId, "Admin");
        var staff = CreateAccount(Role.StaffRoleId, "Staff");
        using var unitOfWork = new TestUnitOfWork(admin, staff);
        var service = new AccountManagementService(unitOfWork, new PasswordHasher());

        var result = await service.UpdateRolesAsync(admin.Id, staff.Id,
            new UpdateAccountRolesRequest(["Manager"]));

        Assert.True(result.IsSuccess);
        Assert.Equal(["Manager"], result.Data!.Roles);
        Assert.Equal([Role.ManagerRoleId], staff.AccountRoles.Select(ar => ar.RoleId));
        Assert.Equal(staff.Id, unitOfWork.Auth.SessionsRevokedFor);
        Assert.True(unitOfWork.Committed);
    }

    [Fact]
    public async Task ManagerCannotAssignRoles()
    {
        var manager = CreateAccount(Role.ManagerRoleId, "Manager");
        var staff = CreateAccount(Role.StaffRoleId, "Staff");
        using var unitOfWork = new TestUnitOfWork(manager, staff);
        var service = new AccountManagementService(unitOfWork, new PasswordHasher());

        var result = await service.UpdateRolesAsync(manager.Id, staff.Id,
            new UpdateAccountRolesRequest(["Admin"]));

        Assert.Equal("ACCOUNT.FORBIDDEN", result.Error?.Code);
        Assert.Equal(Role.StaffRoleId, staff.AccountRoles.Single().RoleId);
    }

    [Fact]
    public async Task AdminCannotRemoveLastActiveAdminRole()
    {
        var admin = CreateAccount(Role.AdminRoleId, "Admin");
        using var unitOfWork = new TestUnitOfWork(admin);
        var service = new AccountManagementService(unitOfWork, new PasswordHasher());

        var result = await service.UpdateRolesAsync(admin.Id, admin.Id,
            new UpdateAccountRolesRequest(["Staff"]));

        Assert.Equal("ACCOUNT.LAST_ADMIN", result.Error?.Code);
        Assert.Equal(Role.AdminRoleId, admin.AccountRoles.Single().RoleId);
    }

    [Fact]
    public async Task AdminCannotAssignInternalRoleToCustomer()
    {
        var admin = CreateAccount(Role.AdminRoleId, "Admin");
        var customer = CreateAccount(Role.CustomerRoleId, "Customer");
        customer.AccountType = AccountType.Customer;
        using var unitOfWork = new TestUnitOfWork(admin, customer);
        var service = new AccountManagementService(unitOfWork, new PasswordHasher());

        var result = await service.UpdateRolesAsync(admin.Id, customer.Id,
            new UpdateAccountRolesRequest(["Staff"]));

        Assert.Equal("ACCOUNT.ROLE_TYPE_MISMATCH", result.Error?.Code);
        Assert.Equal(Role.CustomerRoleId, customer.AccountRoles.Single().RoleId);
    }

    [Fact]
    public async Task AdminCanDemoteSelfWhenAnotherActiveAdminExists()
    {
        var admin = CreateAccount(Role.AdminRoleId, "Admin");
        var secondAdmin = CreateAccount(Role.AdminRoleId, "Admin");
        using var unitOfWork = new TestUnitOfWork(admin, secondAdmin);
        var service = new AccountManagementService(unitOfWork, new PasswordHasher());

        var result = await service.UpdateRolesAsync(admin.Id, admin.Id,
            new UpdateAccountRolesRequest(["Staff"]));

        Assert.True(result.IsSuccess);
        Assert.Equal(Role.StaffRoleId, admin.AccountRoles.Single().RoleId);
        Assert.Equal(admin.Id, unitOfWork.Auth.SessionsRevokedFor);
    }

    [Fact]
    public async Task LockingStaffRevokesSessions()
    {
        var admin = CreateAccount(Role.AdminRoleId, "Admin");
        var staff = CreateAccount(Role.StaffRoleId, "Staff");
        using var unitOfWork = new TestUnitOfWork(admin, staff);
        var service = new AccountManagementService(unitOfWork, new PasswordHasher());

        var result = await service.ChangeLockStatusAsync(admin.Id, staff.Id, new ChangeAccountLockRequest(true));

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountStatus.Locked, staff.Status);
        Assert.Equal(staff.Id, unitOfWork.Auth.SessionsRevokedFor);
        Assert.True(unitOfWork.Committed);
    }

    [Fact]
    public async Task ManagerCannotLockAnotherManager()
    {
        var manager = CreateAccount(Role.ManagerRoleId, "Manager");
        var target = CreateAccount(Role.ManagerRoleId, "Manager");
        using var unitOfWork = new TestUnitOfWork(manager, target);
        var service = new AccountManagementService(unitOfWork, new PasswordHasher());

        var result = await service.ChangeLockStatusAsync(manager.Id, target.Id, new ChangeAccountLockRequest(true));

        Assert.False(result.IsSuccess);
        Assert.Equal("ACCOUNT.NOT_FOUND", result.Error?.Code);
        Assert.Equal(AccountStatus.Active, target.Status);
        Assert.Null(unitOfWork.Auth.SessionsRevokedFor);
    }

    [Fact]
    public async Task AdminCannotLockOwnAccount()
    {
        var admin = CreateAccount(Role.AdminRoleId, "Admin");
        using var unitOfWork = new TestUnitOfWork(admin);
        var service = new AccountManagementService(unitOfWork, new PasswordHasher());

        var result = await service.ChangeLockStatusAsync(admin.Id, admin.Id, new ChangeAccountLockRequest(true));

        Assert.False(result.IsSuccess);
        Assert.Equal("ACCOUNT.SELF_LOCK", result.Error?.Code);
        Assert.Equal(AccountStatus.Active, admin.Status);
    }

    private static Account CreateAccount(Guid roleId, string roleCode)
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            AccountType = AccountType.Internal,
            Status = AccountStatus.Active,
            EmailVerifiedAt = DateTimeOffset.UtcNow
        };
        account.AccountRoles.Add(new AccountRole
        {
            AccountId = account.Id,
            RoleId = roleId,
            Role = new Role { Id = roleId, Code = roleCode }
        });
        return account;
    }

    private sealed class TestUnitOfWork(params Account[] accounts) : IUnitOfWork
    {
        public TestAuthRepository Auth { get; } = new(accounts);
        public IAuthRepository AuthRepository => Auth;
        public IProductRepository ProductRepository => throw new NotSupportedException();
        public IAppointmentRepository AppointmentRepository => throw new NotSupportedException();
        public INotificationRepository NotificationRepository => throw new NotSupportedException();
        public bool Committed { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
        {
            Committed = true;
            return Task.CompletedTask;
        }
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ReloadAccountAsync(Account account, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public void Dispose() { }
    }

    private sealed class TestAuthRepository(params Account[] accounts) : IAuthRepository
    {
        public Guid? SessionsRevokedFor { get; private set; }
        public Account? CreatedAccount { get; private set; }
        public Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(accounts.FirstOrDefault(a => a.Id == id));
        public Task LockAccountAsync(Guid accountId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LockRoleAsync(Guid roleId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(accounts.Count(a => a.Status == AccountStatus.Active
                && a.EmailVerifiedAt != null && a.AccountRoles.Any(ar => ar.RoleId == Role.AdminRoleId)));
        public Task<int> RevokeSessionsAsync(Guid accountId, DateTimeOffset revokedAt, CancellationToken cancellationToken = default)
        {
            SessionsRevokedFor = accountId;
            return Task.FromResult(1);
        }

        public Task<PagedResponse<Account>> GetPagedAccountsAsync(PagedRequest paging, string? search,
            AccountType? accountType, AccountStatus? status, bool staffOnly,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Account?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsEmailAsync(string email, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<Account?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Account?> GetByPhoneAsync(string phone, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsUsernameAsync(string username, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> ExistsPhoneAsync(string phone, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void AddAccount(Account account) => CreatedAccount = account;
        public void ReplaceAccountRoles(Account account, IReadOnlyCollection<Guid> roleIds)
        {
            foreach (var role in account.AccountRoles.Where(ar => !roleIds.Contains(ar.RoleId)).ToList())
                account.AccountRoles.Remove(role);
            foreach (var roleId in roleIds.Where(id => account.AccountRoles.All(ar => ar.RoleId != id)))
                account.AccountRoles.Add(new AccountRole { AccountId = account.Id, RoleId = roleId });
        }
        public Task<OtpChallenge?> GetOtpChallengeByIdAsync(Guid challengeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountOtpChallengesInWindowAsync(string normalizedEmail, DateTimeOffset windowStart,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OtpChallenge?> GetLatestOtpChallengeAsync(Guid accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<OtpChallenge>> GetActiveOtpChallengesAsync(Guid accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReloadChallengeAsync(OtpChallenge challenge, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void AddOtpChallenge(OtpChallenge challenge) => throw new NotSupportedException();
        public Task<AuthSession?> GetSessionByIdAsync(Guid sessionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void AddSession(AuthSession session) => throw new NotSupportedException();
    }
}
