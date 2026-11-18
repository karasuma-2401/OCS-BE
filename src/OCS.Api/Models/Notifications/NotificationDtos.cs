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

    public FcmDispatchResult? PushDelivery { get; set; }

    // Keep the dev API contract compatible with clients using either response name.
    public FcmDispatchResult? PushResult { get => PushDelivery; set => PushDelivery = value; }
}

public sealed class CreateNotificationDto
{
    public Guid RecipientAccountId { get; set; }
    public Guid? SenderAccountId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public NotificationType Type { get; set; } = NotificationType.System;
    public string? Link { get; set; }
    public IReadOnlyDictionary<string, string>? ExtraData { get; set; }
}
