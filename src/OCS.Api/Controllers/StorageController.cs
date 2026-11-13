using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OCS.Api.Authorization;
using OCS.Api.Common;
using OCS.Api.Storage;

namespace OCS.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class StorageController(
    IStorageService storageService,
    IOptions<R2Options> r2Options) : ControllerBase
{
    private const string ProductsFolder = "products";
    private const string InspectionsFolder = "inspections";
    private const long MaxFileSizeBytes = 10 * 1024 * 1024;
    private static readonly HashSet<string> AllowedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ProductsFolder,
        InspectionsFolder
    };

    private readonly R2Options _r2Options = r2Options.Value;

    [HttpPost("upload")]
    [Authorize(Roles = "Admin,Manager,Staff")]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<IActionResult> Upload(
        IFormFile? file,
        [FromQuery] string? folder = "products",
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Tệp tin không hợp lệ",
                Detail = "Vui lòng chọn một tệp hình ảnh để tải lên."
            });
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Tệp tin quá lớn",
                Detail = "Dung lượng ảnh tối đa cho phép là 10MB."
            });
        }

        var targetFolder = string.Equals(folder, InspectionsFolder, StringComparison.OrdinalIgnoreCase)
            ? InspectionsFolder
            : ProductsFolder;

        await using var stream = file.OpenReadStream();
        if (!ImageFormatValidator.TryDetectImageFormat(stream, file.FileName, out var detectedMimeType, out var validationError))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Tệp không hợp lệ",
                Detail = validationError
            });
        }

        try
        {
            var uploadedUrl = await storageService.UploadAsync(
                stream,
                file.FileName,
                detectedMimeType,
                targetFolder,
                cancellationToken);

            var absoluteUrl = ToAbsoluteUrl(uploadedUrl);

            return Ok(new
            {
                id = Guid.NewGuid(),
                url = absoluteUrl,
                fileName = file.FileName,
                size = file.Length,
                contentType = detectedMimeType
            });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
            {
                Title = "Lỗi kết nối máy chủ Cloudflare R2",
                Detail = ex.Message
            });
        }
    }

    [HttpGet("{**key}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetFile(
        string key,
        CancellationToken cancellationToken = default)
    {
        var cleanKey = ExtractKey(key);
        if (string.IsNullOrWhiteSpace(cleanKey) ||
            cleanKey.Contains("..", StringComparison.Ordinal) ||
            cleanKey.Contains(':', StringComparison.Ordinal) ||
            cleanKey.Contains('\0', StringComparison.Ordinal) ||
            cleanKey.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return NotFound();
        }

        var firstFolder = cleanKey.Split('/', 2)[0].ToLowerInvariant();
        if (!AllowedFolders.Contains(firstFolder))
        {
            return NotFound();
        }

        if (string.Equals(firstFolder, InspectionsFolder, StringComparison.OrdinalIgnoreCase))
        {
            var user = HttpContext.User;
            if (user.Identity?.IsAuthenticated != true)
            {
                return Unauthorized(new ProblemDetails
                {
                    Title = "Yêu cầu xác thực",
                    Detail = "Ảnh kiểm định nội bộ chỉ được truy cập bởi nhân viên đã đăng nhập."
                });
            }

            var isStaffOrAdmin = user.IsInRole("Admin") || user.IsInRole("Manager") || user.IsInRole("Staff");
            if (!isStaffOrAdmin)
            {
                return Forbid();
            }

            try
            {
                var result = await storageService.GetFileAsync(cleanKey, cancellationToken);
                if (result is null)
                {
                    return NotFound();
                }

                Response.Headers["X-Content-Type-Options"] = "nosniff";
                Response.Headers.CacheControl = "private, no-cache, no-store";
                return File(result.Value.ContentStream, result.Value.ContentType);
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
                {
                    Title = "Lỗi kết nối máy chủ Cloudflare R2",
                    Detail = ex.Message
                });
            }
        }
        else
        {
            try
            {
                var result = await storageService.GetFileAsync(cleanKey, cancellationToken);
                if (result is null)
                {
                    return NotFound();
                }

                Response.Headers["X-Content-Type-Options"] = "nosniff";
                Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                return File(result.Value.ContentStream, result.Value.ContentType);
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
                {
                    Title = "Lỗi kết nối máy chủ Cloudflare R2",
                    Detail = ex.Message
                });
            }
        }
    }

    [HttpDelete("{**key}")]
    [Authorize]
    public async Task<IActionResult> DeleteFile(
        string key,
        CancellationToken cancellationToken = default)
    {
        var cleanKey = ExtractKey(key);
        if (string.IsNullOrWhiteSpace(cleanKey) ||
            cleanKey.Contains("..", StringComparison.Ordinal) ||
            cleanKey.Contains(':', StringComparison.Ordinal) ||
            cleanKey.Contains('\0', StringComparison.Ordinal) ||
            cleanKey.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Khóa tệp không hợp lệ",
                Detail = "Đường dẫn tệp chứa ký tự không an toàn."
            });
        }

        var firstFolder = cleanKey.Split('/', 2)[0].ToLowerInvariant();
        if (!AllowedFolders.Contains(firstFolder))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Thao tác không được phép",
                Detail = "Chỉ cho phép xóa ảnh trong các thư mục hợp lệ."
            });
        }

        var user = HttpContext.User;
        var isAdminOrManager = user.IsInRole("Admin") || user.IsInRole("Manager");
        var hasPermission = isAdminOrManager ||
                            user.HasClaim("permission", PermissionCatalog.AppointmentsStaffReception) ||
                            user.HasClaim("permission", PermissionCatalog.ProductsCreate) ||
                            user.HasClaim("permission", PermissionCatalog.ProductsApprove);
        if (!hasPermission)
        {
            return Forbid();
        }

        try
        {
            var deleted = await storageService.DeleteAsync(cleanKey, cancellationToken);
            if (!deleted)
            {
                return NotFound(new ProblemDetails
                {
                    Title = "Không tìm thấy tệp",
                    Detail = "Tệp tin không tồn tại hoặc đã được xóa trước đó."
                });
            }

            return Ok(new { success = true, key = cleanKey });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
            {
                Title = "Lỗi kết nối máy chủ Cloudflare R2",
                Detail = ex.Message
            });
        }
    }

    private string ExtractKey(string urlOrKey)
    {
        if (string.IsNullOrWhiteSpace(urlOrKey)) return string.Empty;
        var str = urlOrKey.Trim();
        if (str.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            str.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(str, UriKind.Absolute, out var uri))
            {
                str = uri.AbsolutePath;
            }
        }
        str = Uri.UnescapeDataString(str);
        if (str.StartsWith("/api/storage/", StringComparison.OrdinalIgnoreCase))
        {
            str = str["/api/storage/".Length..];
        }
        else if (str.StartsWith("api/storage/", StringComparison.OrdinalIgnoreCase))
        {
            str = str["api/storage/".Length..];
        }

        str = str.Replace('\\', '/').Trim('/');
        if (Uri.TryCreate(_r2Options.PublicBaseUrl, UriKind.Absolute, out var publicUri))
        {
            var publicPath = Uri.UnescapeDataString(publicUri.AbsolutePath).Trim('/');
            if (!string.IsNullOrEmpty(publicPath) &&
                str.StartsWith(publicPath + "/", StringComparison.OrdinalIgnoreCase))
            {
                str = str[(publicPath.Length + 1)..];
            }
        }

        return str;
    }

    private string ToAbsoluteUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            return url;
        }

        var request = Request;
        var baseUrl = $"{request.Scheme}://{request.Host}{request.PathBase}";
        return $"{baseUrl.TrimEnd('/')}/{url.TrimStart('/')}";
    }
}
