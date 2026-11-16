namespace OCS.Api.Entities;

/// <summary>Yêu cầu xem xét điều khoản riêng cho một lần lập hợp đồng.</summary>
public sealed class ContractTermsRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DraftKey { get; set; }
    public string CustomerPhone { get; set; } = string.Empty;
    public Guid RequestedBy { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public string? DecisionNote { get; set; }
    public byte[]? ApprovedTemplate { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DecidedAt { get; set; }
    public Guid? DecidedBy { get; set; }
}
