using OCS.Api.Entities;

namespace OCS.Api.Entities;

public sealed class StorageLocation : BaseEntity
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}