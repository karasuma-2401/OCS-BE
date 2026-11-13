using System.ComponentModel.DataAnnotations;

namespace OCS.Api.Models.Notifications;

public sealed class RegisterFcmTokenRequest
{
    [Required(ErrorMessage = "FCM token là bắt buộc.")]
    [MaxLength(512)]
    public string Token { get; set; } = string.Empty;

    [MaxLength(50)]
    public string DeviceType { get; set; } = "Web";
}

public sealed class UnregisterFcmTokenRequest
{
    [Required(ErrorMessage = "FCM token là bắt buộc.")]
    [MaxLength(512)]
    public string Token { get; set; } = string.Empty;
}

public sealed class FcmTokenRecord
{
    public string Token { get; set; } = string.Empty;
    public string DeviceType { get; set; } = "Web";
    public DateTimeOffset RegisteredAt { get; set; }
    public DateTimeOffset LastUsedAt { get; set; }
}

public sealed record FcmDispatchResult(
    int TargetedDeviceCount,
    int SuccessCount,
    int FailureCount,
    bool FirebaseConfigured);

