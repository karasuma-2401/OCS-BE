namespace OCS.Api.Storage;

public interface IStorageService
{
    Task<string> UploadAsync(
        Stream stream,
        string fileName,
        string contentType,
        string folder = "products",
        CancellationToken cancellationToken = default);

    Task<(Stream ContentStream, string ContentType)?> GetFileAsync(
        string key,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        string key,
        CancellationToken cancellationToken = default);
}
