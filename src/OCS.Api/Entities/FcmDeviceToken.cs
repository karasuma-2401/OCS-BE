namespace OCS.Api.Entities;

public sealed class FcmDeviceToken
{
    public string Token { get; set; } = string.Empty;
    public Guid AccountId { get; set; }
    public string DeviceType { get; set; } = "Web";
    public DateTimeOffset RegisteredAt { get; set; }
    public DateTimeOffset LastUsedAt { get; set; }
    public Account Account { get; set; } = null!;
}
