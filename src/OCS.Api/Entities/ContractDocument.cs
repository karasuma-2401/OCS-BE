namespace OCS.Api.Entities;

public sealed class ContractDocument
{
    public Guid ContractId { get; set; }
    public ConsignmentContract Contract { get; set; } = null!;
    public byte[] Content { get; set; } = [];
    public byte[]? SignatureContent { get; set; }
    public string? SignatureContentType { get; set; }
    public string? Terms { get; set; }
    public string TemplateVersion { get; set; } = "draft-v1";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
