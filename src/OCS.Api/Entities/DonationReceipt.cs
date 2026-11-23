namespace OCS.Api.Entities;

// Immutable snapshot of one confirmed physical handover, including its exact products.
public sealed class DonationReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExpiryProcessingId { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public string ContractNumber { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string RecipientOrganization { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
    public string? Note { get; set; }
    public Guid HandledByEmployeeId { get; set; }
    public DateTimeOffset HandledAt { get; set; }
    public string ProductsJson { get; set; } = string.Empty;
    public ExpiryProcessing ExpiryProcessing { get; set; } = null!;
}
