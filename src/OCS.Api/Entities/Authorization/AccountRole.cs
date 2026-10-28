namespace OCS.Api.Entities;

public sealed class AccountRole
{
    public Guid AccountId { get; set; }

    public Guid RoleId { get; set; }

    public Account Account { get; set; } = null!;

    public Role Role { get; set; } = null!;
}