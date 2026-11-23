namespace OCS.Api.Entities;

public sealed class ExpiryProcessingItem
{
    public Guid ExpiryProcessingId { get; set; }

    public Guid ProductId { get; set; }

    public Guid? HandledByEmployeeId { get; set; }
    public DateTimeOffset? HandledAt { get; set; }
    public string? RecipientOrganization { get; set; }
    public string? Evidence { get; set; }
    public string? Note { get; set; }

    public ExpiryProcessing ExpiryProcessing { get; set; } = null!;

    public Product Product { get; set; } = null!;
}
