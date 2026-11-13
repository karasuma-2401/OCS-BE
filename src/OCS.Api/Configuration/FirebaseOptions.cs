namespace OCS.Api.Configuration;

public sealed class FirebaseOptions
{
    public const string SectionName = "Firebase";

    public string ProjectId { get; set; } = string.Empty;

    public string ClientEmail { get; set; } = string.Empty;

    public string PrivateKey { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ProjectId) &&
        !string.IsNullOrWhiteSpace(ClientEmail) &&
        !string.IsNullOrWhiteSpace(PrivateKey);
}

