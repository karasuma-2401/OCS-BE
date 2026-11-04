using OCS.Api.Entities;
using OCS.Api.Consts;

namespace OCS.Api.Entities;

public sealed class Account : BaseEntity
{
    // Email verification does not prove phone ownership. Preserve legacy account IDs and history.
    public AccountType AccountType { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public string? Username { get; set; }

    public string? Email { get; set; }

    public string? NormalizedEmail { get; private set; }

    public DateTimeOffset? EmailVerifiedAt { get; set; }

    public int FailedLoginAttempts { get; set; }

    public DateTimeOffset? LockoutEndAt { get; set; }

    // Npgsql maps this concurrency token to PostgreSQL's system xmin column.
    public uint Version { get; private set; }

    public string? IdentityNumber { get; set; }

    public string? PasswordHash { get; set; }

    public AccountStatus Status { get; set; }
        = AccountStatus.PendingActivation;

    public DateTimeOffset? PhoneVerifiedAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public Guid? CreatedByAccountId { get; set; }

    public Account? CreatedByAccount { get; set; }

    public CustomerProfile? CustomerProfile { get; set; }

    public EmployeeProfile? EmployeeProfile { get; set; }

    public ICollection<AccountRole> AccountRoles { get; set; }
        = new List<AccountRole>();
}
