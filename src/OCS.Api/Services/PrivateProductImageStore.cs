using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using OCS.Api.Storage;

namespace OCS.Api.Services;

public sealed class PrivateProductImageStore(IServiceProvider services, IOptions<R2Options> options)
{
    private const int MaxImageBytes = 10 * 1024 * 1024;

    // Chi doc object trong bucket R2 da cau hinh; khong fetch URL tuy y.
    public string? GetKey(string url)
    {
        if (services.GetService<IAmazonS3>() is null || string.IsNullOrWhiteSpace(options.Value.BucketName) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo)) return null;
        var pathStart = url.IndexOf('/', url.IndexOf("://", StringComparison.Ordinal) + 3);
        if (pathStart < 0 || Uri.UnescapeDataString(url[pathStart..].Split('?', '#')[0]).Split('/').Any(part => part is "." or "..")) return null;
        string key;
        if (uri.Scheme == "r2" && string.Equals(uri.Host, options.Value.BucketName, StringComparison.OrdinalIgnoreCase))
        {
            key = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        }
        else
        {
            if (string.IsNullOrWhiteSpace(options.Value.PublicBaseUrl) ||
                !Uri.TryCreate(options.Value.PublicBaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var root) ||
                root.Scheme != Uri.UriSchemeHttps || uri.Scheme != root.Scheme || uri.Host != root.Host || uri.Port != root.Port ||
                !uri.AbsolutePath.StartsWith(root.AbsolutePath, StringComparison.Ordinal)) return null;
            key = Uri.UnescapeDataString(uri.AbsolutePath[root.AbsolutePath.Length..]);
        }
        if (string.IsNullOrEmpty(key) || key.Contains('\\') || key.Split('/').Any(part => part is "" or "." or "..")) return null;
        return key;
    }

    public async Task<(byte[] Content, string ContentType)?> ReadAsync(string key, CancellationToken ct)
    {
        var client = services.GetService<IAmazonS3>();
        if (client is null) return null;
        try
        {
            using var response = await client.GetObjectAsync(new GetObjectRequest { BucketName = options.Value.BucketName, Key = key }, ct);
            if (response.ContentLength <= 0 || response.ContentLength > MaxImageBytes) return null;
            var type = response.Headers.ContentType?.Split(';')[0].Trim().ToLowerInvariant();
            if (type is not ("image/jpeg" or "image/png" or "image/webp")) return null;
            using var memory = new MemoryStream();
            var buffer = new byte[81920];
            int count;
            while ((count = await response.ResponseStream.ReadAsync(buffer, ct)) > 0)
            {
                if (memory.Length + count > MaxImageBytes) return null;
                await memory.WriteAsync(buffer.AsMemory(0, count), ct);
            }
            var data = memory.ToArray();
            var valid = type switch
            {
                "image/jpeg" => data.AsSpan().StartsWith(new byte[] { 0xff, 0xd8, 0xff }),
                "image/png" => data.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }),
                "image/webp" => data.Length >= 12 && data.AsSpan(0, 4).SequenceEqual("RIFF"u8) && data.AsSpan(8, 4).SequenceEqual("WEBP"u8),
                _ => false
            };
            return valid ? (data, type) : null;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { return null; }
    }
}
