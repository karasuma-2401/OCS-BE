using OCS.Domain.Common;
using OCS.Domain.Enums;

namespace OCS.Domain.Entities;

public sealed class Notification : BaseEntity
{
    public Guid RecipientAccountId { get; set; }

    public Guid? SenderAccountId { get; set; }

    public NotificationType Type { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTimeOffset SentAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsRead { get; set; }

    public Account RecipientAccount { get; set; } = null!;

    public Account? SenderAccount { get; set; }
}
