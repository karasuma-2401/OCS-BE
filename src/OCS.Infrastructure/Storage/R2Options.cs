namespace OCS.Infrastructure.Storage;

public sealed class R2Options
{
    public const string SectionName = "R2";

    public string ServiceUrl { get; init; } = string.Empty;

    public string AccessKeyId { get; init; } = string.Empty;

    public string SecretAccessKey { get; init; } = string.Empty;

    public string BucketName { get; init; } = "ocs";

    public string PublicBaseUrl { get; init; } = string.Empty;
}
