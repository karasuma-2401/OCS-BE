namespace OCS.Api.Entities;

/// <summary>Mẫu Word do quản lý lưu, phân biệt theo loại tài liệu.</summary>
public sealed class WordDocumentTemplate
{
    public string Kind { get; set; } = string.Empty;
    public byte[] Content { get; set; } = [];
    public Guid Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid UpdatedBy { get; set; }
}
