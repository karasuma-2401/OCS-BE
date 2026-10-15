using OCS.Domain.Common;

namespace OCS.Domain.Entities;

public sealed class Role
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = String.Empty;
    public string? Description { get; set; }

    public ICollection<AccountRole> AccountRoles { get; set; }
        = new List<AccountRole>();

    public ICollection<RolePermission> RolePermissions { get; set; }
        = new List<RolePermission>();

}

