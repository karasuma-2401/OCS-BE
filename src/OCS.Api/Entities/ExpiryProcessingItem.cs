namespace OCS.Api.Entities;

public sealed class ExpiryProcessingItem
{
    public Guid ExpiryProcessingId { get; set; }

    public Guid ProductId { get; set; }

    public ExpiryProcessing ExpiryProcessing { get; set; } = null!;

    public Product Product { get; set; } = null!;
}
