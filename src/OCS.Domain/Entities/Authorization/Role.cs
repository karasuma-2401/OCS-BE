using OCS.Domain.Common;

namespace OCS.Domain.Entities;

public sealed class Role
{
    // AUTH TODO: Treat this existing physical model as an authorization group in application/API
    // language. Do not authorize from Code/Name (for example, "Manager"); effective permissions
    // and delegation decide access. Add only metadata required to protect system-managed groups.
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = String.Empty;
    public string? Description { get; set; }

    public ICollection<AccountRole> AccountRoles { get; set; }
        = new List<AccountRole>();

    public ICollection<RolePermission> RolePermissions { get; set; }
        = new List<RolePermission>();

}

