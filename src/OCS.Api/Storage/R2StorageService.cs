using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OCS.Api.Storage;

public sealed class R2StorageService(
    IServiceProvider serviceProvider,
    IOptions<R2Options> options,
    IWebHostEnvironment environment,
    ILogger<R2StorageService> logger) : IStorageService
{
    private const string ProductsFolder = "products";
    private const string InspectionsFolder = "inspections";

    private readonly R2Options _options = options.Value;

    public async Task<string> UploadAsync(
        Stream stream,
        string fileName,
        string contentType,
        string folder = "products",
        CancellationToken cancellationToken = default)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(ext))
        {
            ext = ".jpg";
        }

        var targetFolder = string.Equals(folder, InspectionsFolder, StringComparison.OrdinalIgnoreCase)
            ? InspectionsFolder
            : ProductsFolder;

        var dateFolder = DateTime.UtcNow.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        var key = $"{targetFolder}/{dateFolder}/{Guid.NewGuid():N}{ext}";

        var s3Client = serviceProvider.GetService<IAmazonS3>();
        var isR2Configured = s3Client is not null && !string.IsNullOrWhiteSpace(_options.BucketName);

        if (isR2Configured)
        {
            try
            {
                if (stream.CanSeek)
                {
                    stream.Position = 0;
                }

                var putRequest = new PutObjectRequest
                {
                    BucketName = _options.BucketName,
                    Key = key,
                    InputStream = stream,
                    ContentType = contentType,
                    DisablePayloadSigning = true
                };

                await s3Client!.PutObjectAsync(putRequest, cancellationToken);

                if (!string.IsNullOrWhiteSpace(_options.PublicBaseUrl) && targetFolder == ProductsFolder)
                {
                    return $"{_options.PublicBaseUrl.TrimEnd('/')}/{key}";
                }

                return $"/api/storage/{key}";
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Lỗi khi tải tệp {Key} lên Cloudflare R2 trên môi trường {Env}.", key, environment.EnvironmentName);
                throw new InvalidOperationException($"Lỗi tải tệp lên Cloudflare R2: {ex.Message}", ex);
            }
        }
        else
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException("Cloudflare R2 chưa được cấu hình cho môi trường Production. Không thể lưu trữ tệp lên đĩa cục bộ tạm thời.");
            }
        }

        try
        {
            var localPath = GetSafeLocalPath(key);
            if (localPath is null)
            {
                throw new InvalidOperationException("Đường dẫn lưu tệp không an toàn nằm ngoài thư mục uploads.");
            }

            var dir = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            await using var fileStream = File.Create(localPath);
            await stream.CopyToAsync(fileStream, cancellationToken);

            return $"/api/storage/{key}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write file {Key} to local storage fallback.", key);
            throw;
        }
    }

    public async Task<(Stream ContentStream, string ContentType)?> GetFileAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var safeKey = NormalizeKey(key);
        if (string.IsNullOrWhiteSpace(safeKey))
        {
            return null;
        }

        if (!IsAllowedFolder(safeKey))
        {
            return null;
        }

        var s3Client = serviceProvider.GetService<IAmazonS3>();
        var isR2Configured = s3Client is not null && !string.IsNullOrWhiteSpace(_options.BucketName);

        if (isR2Configured)
        {
            try
            {
                var response = await s3Client!.GetObjectAsync(_options.BucketName, safeKey, cancellationToken);
                var contentType = ResolveMimeType(safeKey);
                return (response.ResponseStream, contentType);
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Lỗi kết nối khi lấy tệp {Key} từ Cloudflare R2.", safeKey);
                throw new InvalidOperationException($"Không thể kết nối đến Cloudflare R2: {ex.Message}", ex);
            }
        }

        var localPath = GetSafeLocalPath(key);
        if (localPath is not null && File.Exists(localPath))
        {
            var fileStream = File.OpenRead(localPath);
            var mime = ResolveMimeType(localPath);
            return (fileStream, mime);
        }

        return null;
    }

    public async Task<bool> DeleteAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var safeKey = NormalizeKey(key);
        if (string.IsNullOrWhiteSpace(safeKey))
        {
            return false;
        }

        if (!IsAllowedFolder(safeKey))
        {
            return false;
        }

        var s3Client = serviceProvider.GetService<IAmazonS3>();
        var isR2Configured = s3Client is not null && !string.IsNullOrWhiteSpace(_options.BucketName);

        if (isR2Configured)
        {
            try
            {
                await s3Client!.DeleteObjectAsync(_options.BucketName, safeKey, cancellationToken);
                return true;
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return false;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Lỗi kết nối khi xóa tệp {Key} trên Cloudflare R2.", safeKey);
                throw new InvalidOperationException($"Không thể kết nối đến Cloudflare R2 để xóa tệp: {ex.Message}", ex);
            }
        }

        var localPath = GetSafeLocalPath(key);
        if (localPath is not null && File.Exists(localPath))
        {
            try
            {
                File.Delete(localPath);
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to delete local fallback file {Path}.", localPath);
                throw;
            }
        }

        return false;
    }

    private string? GetSafeLocalPath(string key)
    {
        var safeKey = NormalizeKey(key);
        if (string.IsNullOrWhiteSpace(safeKey))
        {
            return null;
        }

        var webRoot = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
        var uploadsRoot = Path.GetFullPath(Path.Combine(webRoot, "uploads"));

        var combined = Path.Combine(uploadsRoot, safeKey.Replace('/', Path.DirectorySeparatorChar));
        var canonicalPath = Path.GetFullPath(combined);

        var uploadsPrefix = uploadsRoot.EndsWith(Path.DirectorySeparatorChar)
            ? uploadsRoot
            : uploadsRoot + Path.DirectorySeparatorChar;

        if (!canonicalPath.StartsWith(uploadsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Phát hiện nguy cơ Path Traversal với key '{Key}' trỏ đến '{CanonicalPath}' nằm ngoài '{UploadsRoot}'.", key, canonicalPath, uploadsRoot);
            return null;
        }

        return canonicalPath;
    }

    private string? NormalizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var clean = key.Trim();

        if (clean.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            clean.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(clean, UriKind.Absolute, out var uri))
            {
                clean = uri.AbsolutePath;
            }
        }

        clean = Uri.UnescapeDataString(clean);

        if (clean.StartsWith("/api/storage/", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean["/api/storage/".Length..];
        }
        else if (clean.StartsWith("api/storage/", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean["api/storage/".Length..];
        }

        if (!string.IsNullOrWhiteSpace(_options.PublicBaseUrl) &&
            Uri.TryCreate(_options.PublicBaseUrl, UriKind.Absolute, out var publicUri))
        {
            var publicPath = publicUri.AbsolutePath.Trim('/');
            if (!string.IsNullOrEmpty(publicPath) && clean.StartsWith(publicPath + "/", StringComparison.OrdinalIgnoreCase))
            {
                clean = clean[(publicPath.Length + 1)..];
            }
        }

        clean = clean.Replace('\\', '/').Trim('/');

        if (clean.Contains("..", StringComparison.Ordinal) ||
            clean.Contains(':', StringComparison.Ordinal) ||
            clean.Contains('\0', StringComparison.Ordinal) ||
            clean.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return null;
        }

        return clean;
    }

    private static bool IsAllowedFolder(string key) =>
        key.StartsWith(ProductsFolder + "/", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith(InspectionsFolder + "/", StringComparison.OrdinalIgnoreCase);

    private static string ResolveMimeType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
    }
}
