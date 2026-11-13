using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Services;
using System.Security.Claims;
using DocumentFormat.OpenXml.Packaging;

namespace OCS.Api.Controllers;

/// <summary>Tải và kiểm tra các ô thông tin trong mẫu Word.</summary>
[ApiController]
[Route("api/word-template-samples")]
[Authorize(Roles = "Admin,Manager")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WordTemplateSamplesController(AppDbContext db) : ControllerBase
{
    /// <summary>Tải mẫu hợp đồng hoặc mẫu phiếu sản phẩm có sẵn các ô thông tin.</summary>
    [HttpGet("{kind}")]
    public async Task<IActionResult> Download(string kind, CancellationToken cancellationToken)
    {
        if (kind is not (WordTemplateSamples.Contract or WordTemplateSamples.Product)) return NotFound();
        var name = kind == WordTemplateSamples.Contract ? "mau-hop-dong.docx" : "mau-thong-tin-san-pham.docx";
        var existingTerms = kind == WordTemplateSamples.Contract
            ? await db.ContractTemplates.AsNoTracking().Where(x => x.Id == 1).Select(x => x.DefaultTerms).SingleOrDefaultAsync(cancellationToken)
            : null;
        return File(WordTemplateSamples.Create(kind, existingTerms), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", name);
    }

    /// <summary>Danh sách tham số có thể chép vào mẫu Word.</summary>
    [HttpGet("{kind}/fields")]
    public IActionResult Fields(string kind)
    {
        var fields = kind switch { WordTemplateSamples.Contract => WordTemplateSamples.ContractFields, WordTemplateSamples.Product => WordTemplateSamples.ProductFields, _ => null };
        return fields is null ? NotFound() : Ok(fields.Select(field => new { label = field.Value, token = "[" + field.Value + "]" }));
    }

    /// <summary>Kiểm tra các ô trong mẫu Word đã chỉnh sửa mà không lưu.</summary>
    [HttpPost("{kind}/validate")]
    [RequestSizeLimit(2_200_000)]
    public async Task<IActionResult> Validate(string kind, IFormFile file, CancellationToken cancellationToken)
    {
        if (kind is not (WordTemplateSamples.Contract or WordTemplateSamples.Product)) return NotFound();
        if (file.Length is 0 or > WordTemplateValidator.MaximumFileBytes || !file.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new ProblemDetails { Title = "Chọn tệp .docx không quá 2 MB.", Status = StatusCodes.Status400BadRequest });
        await using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        var errors = WordTemplateValidator.Validate(buffer.ToArray(), kind);
        return Ok(new WordTemplateValidationResponse(errors.Count == 0, errors));
    }

    /// <summary>Lấy thông tin phiên bản bản nháp Word đã lưu.</summary>
    [HttpGet("{kind}/saved")]
    public async Task<IActionResult> Saved(string kind, CancellationToken cancellationToken)
    {
        if (kind is not (WordTemplateSamples.Contract or WordTemplateSamples.Product)) return NotFound();
        var template = await db.WordDocumentTemplates.AsNoTracking().Where(item => item.Kind == kind)
            .Select(item => new { item.Version, item.UpdatedAt }).SingleOrDefaultAsync(cancellationToken);
        return Ok(new { exists = template is not null, version = template?.Version ?? Guid.Empty, updatedAt = template?.UpdatedAt });
    }

    /// <summary>Tải lại bản nháp Word đã lưu.</summary>
    [HttpGet("{kind}/saved/download")]
    public async Task<IActionResult> DownloadSaved(string kind, CancellationToken cancellationToken)
    {
        if (kind is not (WordTemplateSamples.Contract or WordTemplateSamples.Product)) return NotFound();
        var bytes = await db.WordDocumentTemplates.AsNoTracking().Where(item => item.Kind == kind)
            .Select(item => item.Content).SingleOrDefaultAsync(cancellationToken);
        return bytes is null ? NotFound() : File(bytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", $"mau-{kind}.docx");
    }

    /// <summary>Lưu bản nháp sau khi kiểm tra, không kích hoạt thay thế mẫu đang dùng.</summary>
    [HttpPut("{kind}/saved")]
    [RequestSizeLimit(2_200_000)]
    public async Task<IActionResult> Save(string kind, IFormFile file, [FromForm] string version, CancellationToken cancellationToken)
    {
        if (kind is not (WordTemplateSamples.Contract or WordTemplateSamples.Product)) return NotFound();
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId)) return Unauthorized();
        if (!Guid.TryParse(version, out var expectedVersion)) return BadRequest(new ProblemDetails { Title = "Phiên bản mẫu không hợp lệ." });
        if (file.Length is 0 or > WordTemplateValidator.MaximumFileBytes || !file.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new ProblemDetails { Title = "Chọn tệp .docx không quá 2 MB." });
        await using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        var errors = WordTemplateValidator.Validate(bytes, kind);
        if (errors.Count != 0) return BadRequest(new WordTemplateValidationResponse(false, errors));
        if (kind == WordTemplateSamples.Contract)
        {
            using var word = WordprocessingDocument.Open(new MemoryStream(bytes), false);
            if (word.MainDocumentPart!.Document.Body!.InnerText.Contains(WordTemplateSamples.TermsInstruction, StringComparison.Ordinal))
                return BadRequest(new ProblemDetails { Title = "Hãy thay hướng dẫn trong mẫu bằng điều khoản của cửa hàng trước khi lưu." });
        }
        var template = await db.WordDocumentTemplates.SingleOrDefaultAsync(item => item.Kind == kind, cancellationToken);
        if ((template?.Version ?? Guid.Empty) != expectedVersion)
            return Conflict(new ProblemDetails { Title = "Mẫu đã được thay đổi. Hãy tải lại trước khi lưu." });
        if (template is null)
        {
            template = new WordDocumentTemplate { Kind = kind };
            db.WordDocumentTemplates.Add(template);
        }
        template.Content = bytes;
        template.Version = Guid.NewGuid();
        template.UpdatedAt = DateTimeOffset.UtcNow;
        template.UpdatedBy = actorId;
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return Conflict(new ProblemDetails { Title = "Mẫu đã được thay đổi. Hãy tải lại trước khi lưu." }); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { return Conflict(new ProblemDetails { Title = "Mẫu đã được thay đổi. Hãy tải lại trước khi lưu." }); }
        return Ok(new { exists = true, version = template.Version, updatedAt = template.UpdatedAt });
    }
}

/// <summary>Kết quả kiểm tra các ô trong mẫu Word.</summary>
public sealed record WordTemplateValidationResponse(bool Valid, IReadOnlyList<string> Errors);
