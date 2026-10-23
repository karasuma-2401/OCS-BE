using OCS.Domain.Common;
using OCS.Domain.Enums;

namespace OCS.Domain.Entities;

public sealed class ExpiryProcessing : BaseEntity
{
    public Guid ProcessedByEmployeeId { get; set; }

    public DateTimeOffset CollectionDeadline { get; set; }

    public ExpiryProcessingType Type { get; set; }

    public ExpiryProcessingStatus Status { get; set; } = ExpiryProcessingStatus.Pending;

    public DateTimeOffset? ConfirmedAt { get; set; }

    public EmployeeProfile ProcessedByEmployee { get; set; } = null!;

    public ICollection<ExpiryProcessingItem> Items { get; set; } = new List<ExpiryProcessingItem>();
}
