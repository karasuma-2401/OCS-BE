using OCS.Api.Entities;
using OCS.Api.Consts;

namespace OCS.Api.Entities;

public sealed class ExpiryProcessing : BaseEntity
{
    public Guid? ProcessedByEmployeeId { get; set; }

    public DateTimeOffset? CollectionDeadline { get; set; }

    public ExpiryProcessingType Type { get; set; }

    public ExpiryProcessingStatus Status { get; set; } = ExpiryProcessingStatus.Pending;

    public DateTimeOffset? ConfirmedAt { get; set; }

    public string? RecipientOrganization { get; set; }
    public string? Evidence { get; set; }
    public string? Note { get; set; }
    public Guid? UnclaimedApprovedByEmployeeId { get; set; }
    public DateTimeOffset? UnclaimedApprovedAt { get; set; }
    public string? UnclaimedBasis { get; set; }

    public EmployeeProfile? ProcessedByEmployee { get; set; }

    public ICollection<ExpiryProcessingItem> Items { get; set; } = new List<ExpiryProcessingItem>();
}
