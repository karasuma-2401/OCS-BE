using OCS.Api.Consts;

namespace OCS.Api.Models.Notifications;

public sealed class NotificationDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public NotificationType Type { get; set; }
    public DateTimeOffset SentAt { get; set; }
    public bool IsRead { get; set; }
}

public sealed class CreateNotificationDto
{
    public Guid RecipientAccountId { get; set; }
    public Guid? SenderAccountId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public NotificationType Type { get; set; } = NotificationType.System;
}

