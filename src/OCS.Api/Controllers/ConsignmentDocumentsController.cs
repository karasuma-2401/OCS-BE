using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OCS.Api.Authorization;
using OCS.Api.Context;
using OCS.Api.Entities;

namespace OCS.Api.Controllers;

[ApiController]
[Route("api/consignments/{contractId:guid}/document")]
[Authorize(Roles = "Admin,Manager,Staff")]
[Authorize(Policy = PermissionCatalog.AppointmentsStaffReception)]
public sealed class ConsignmentDocumentsController(AppDbContext db) : ControllerBase
{
    private const int MaxBytes = 10 * 1024 * 1024;

    /// <summary>Tai len hoac thay the tep hop dong da ky (PDF, JPEG, PNG, WebP).</summary>
    [HttpPut]
    [RequestSizeLimit(MaxBytes + 65536)]
    public async Task<IActionResult> Put(Guid contractId, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length <= 0 || file.Length > MaxBytes)
            return BadRequest(new ProblemDetails { Status = 400, Title = "File must be between 1 byte and 10 MiB." });
        if (!await db.ConsignmentContracts.AnyAsync(c => c.Id == contractId, ct)) return NotFound();
        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, ct);
        if (stream.Length != file.Length || stream.Length > MaxBytes) return BadRequest();
        var bytes = stream.ToArray();
        var format = Detect(bytes);
        if (format is null || !string.Equals(file.ContentType, format.Value.ContentType, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new ProblemDetails { Status = 400, Title = "Unsupported file or content does not match its type." });

        var doc = await db.ConsignmentDocuments.SingleOrDefaultAsync(d => d.ContractId == contractId, ct);
        if (doc is null)
        {
            doc = new ConsignmentDocument { ContractId = contractId };
            db.ConsignmentDocuments.Add(doc);
        }
        // Ten do server tao, khong su dung duong dan hoac ten file tu client.
        doc.FileName = $"hop-dong-{contractId:N}.{format.Value.Extension}";
        doc.ContentType = format.Value.ContentType;
        doc.UploadedAt = DateTimeOffset.UtcNow;
        doc.Content = bytes;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static (string ContentType, string Extension)? Detect(byte[] bytes)
    {
        var b = bytes.AsSpan();
        if (b.StartsWith("%PDF-"u8)) return ("application/pdf", "pdf");
        if (b.StartsWith(new byte[] { 0xff, 0xd8, 0xff })) return ("image/jpeg", "jpg");
        if (b.StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a })) return ("image/png", "png");
        if (b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8)) return ("image/webp", "webp");
        return null;
    }
}
