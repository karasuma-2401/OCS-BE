using OCS.Domain.Common;

namespace OCS.Domain.Entities;

public sealed class Category : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}