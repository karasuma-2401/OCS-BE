namespace OCS.Api.Entities;

public sealed class Role
{
    public static readonly Guid AdminRoleId = new("20000000-0000-0000-0000-000000000003");
    public static readonly Guid ManagerRoleId = new("20000000-0000-0000-0000-000000000001");
    public static readonly Guid StaffRoleId = new("20000000-0000-0000-0000-000000000002");
    public static readonly Guid CustomerRoleId = new("20000000-0000-0000-0000-000000000004");

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public ICollection<AccountRole> AccountRoles { get; set; }
        = new List<AccountRole>();

    public ICollection<RolePermission> RolePermissions { get; set; }
        = new List<RolePermission>();
}
