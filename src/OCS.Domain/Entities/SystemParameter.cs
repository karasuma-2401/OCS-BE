namespace OCS.Domain.Entities;

public sealed class SystemParameter
{
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public string? Description { get; set; }
}
