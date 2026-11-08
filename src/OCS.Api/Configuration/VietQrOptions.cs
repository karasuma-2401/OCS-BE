namespace OCS.Api.Configuration;

public sealed class VietQrOptions
{
    public const string SectionName = "VietQr";

    public string Provider { get; set; } = "VietQrQuickLink";
    public string BankCode { get; set; } = "MB";
    public string AccountNumber { get; set; } = "0900000001";
    public string AccountHolderName { get; set; } = "CUA HANG KY GUI OCS";
    public string Template { get; set; } = "compact2";
    public int ExpiryMinutes { get; set; } = 10;
    public string ReferencePrefix { get; set; } = "OCS";
    public string QuickLinkBaseUrl { get; set; } = "https://img.vietqr.io/image";
    public string WebhookSecret { get; set; } = string.Empty;
    public bool EnableWebhookSimulation { get; set; }
}
