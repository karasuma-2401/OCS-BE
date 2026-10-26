using OCS.Domain.Common;
using OCS.Domain.Enums;

namespace OCS.Domain.Entities;

public sealed class Account : BaseEntity
{
    // AUTH TODO: Extend this aggregate only with authentication identity state:
    // normalized phone/email/username lookup values and EmailVerifiedAt. Keep password hashing,
    // OTP delivery, JWT, session lookup and HTTP concerns outside Domain. Email proof must never
    // set PhoneVerifiedAt. Preserve Id and business-history relations for every legacy account.
    public AccountType AccountType { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public string? Username { get; set; }

    public string? Email { get; set; }

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
