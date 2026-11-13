namespace OCS.Api.Entities;

public sealed class ConsignmentDocument
{
    public Guid ContractId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public DateTimeOffset UploadedAt { get; set; }
    public byte[] Content { get; set; } = [];
    public ConsignmentContract Contract { get; set; } = null!;
}
