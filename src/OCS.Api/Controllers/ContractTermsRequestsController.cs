using System.Security.Claims;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OCS.Api.Authorization;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Services;

namespace OCS.Api.Controllers;

/// <summary>Yêu cầu quản lý duyệt điều khoản riêng trước khi lập hợp đồng.</summary>
[ApiController]
[Route("api/contract-terms-requests")]
[Authorize(Roles = "Admin,Manager,Staff")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ContractTermsRequestsController(AppDbContext db) : ControllerBase
{
    private Guid? Actor => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    /// <summary>Gửi yêu cầu cho một lần lập hợp đồng.</summary>
    [HttpPost]
    [Authorize(Policy = PermissionCatalog.AppointmentsStaffReception)]
    public async Task<IActionResult> Create(CreateTermsRequest request, CancellationToken cancellationToken)
    {
        if (Actor is not Guid actor) return Unauthorized();
        var reason = request.Reason?.Trim();
        var customerPhone = request.CustomerPhone?.Trim();
        if (customerPhone is null || customerPhone.Length != 10 || customerPhone[0] != '0' || customerPhone.Any(c => c < '0' || c > '9'))
            return BadRequest(new ProblemDetails { Title = "Vui lòng chọn khách hàng có số điện thoại hợp lệ trước khi gửi yêu cầu." });
        if (request.DraftKey == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Length > 2000)
            return BadRequest(new ProblemDetails { Title = "Nhập yêu cầu điều chỉnh, tối đa 2.000 ký tự." });
        await using var transaction = await ContractDraftLock.AcquireAsync(db, request.DraftKey, cancellationToken);
        var contractNumber = $"HD-{request.DraftKey:N}";
        if (await db.ConsignmentContracts.AnyAsync(x => x.AppointmentId == request.DraftKey || x.ContractNumber == contractNumber, cancellationToken))
            return Conflict(new ProblemDetails { Title = "Hợp đồng này đã được tạo." });
        if (await db.ContractTermsRequests.AnyAsync(x => x.DraftKey == request.DraftKey && (x.Status == "Pending" || x.Status == "Approved"), cancellationToken))
            return Conflict(new ProblemDetails { Title = "Đã có yêu cầu đang chờ hoặc đã được duyệt cho hợp đồng này." });
        var item = new ContractTermsRequest { DraftKey = request.DraftKey, RequestedBy = actor, Reason = reason, CustomerPhone = customerPhone };
        db.ContractTermsRequests.Add(item);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { return Conflict(new ProblemDetails { Title = "Đã có yêu cầu đang xử lý cho hợp đồng này." }); }
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return Ok(Map(item));
    }

    /// <summary>Xem yêu cầu mới nhất của chính nhân viên cho bản đang lập.</summary>
    [HttpGet("mine/{draftKey:guid}")]
    [Authorize(Policy = PermissionCatalog.AppointmentsStaffReception)]
    public async Task<IActionResult> Mine(Guid draftKey, CancellationToken cancellationToken)
    {
        if (Actor is not Guid actor) return Unauthorized();
        var item = await db.ContractTermsRequests.AsNoTracking().Where(x => x.RequestedBy == actor && x.DraftKey == draftKey)
            .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        return item is null ? NotFound() : Ok(Map(item));
    }

    /// <summary>Nhân viên hủy yêu cầu của mình trước khi tạo hợp đồng.</summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = PermissionCatalog.AppointmentsStaffReception)]
    public async Task<IActionResult> Cancel(Guid id, CancelTermsRequest request, CancellationToken cancellationToken)
    {
        if (Actor is not Guid actor) return Unauthorized();
        var item = await db.ContractTermsRequests.SingleOrDefaultAsync(x => x.Id == id && x.RequestedBy == actor, cancellationToken);
        if (item is null) return NotFound();
        if (item.Status is not ("Pending" or "Approved") || item.Version != request.Version)
            return Conflict(new ProblemDetails { Title = "Yêu cầu đã thay đổi. Hãy tải lại." });
        item.Status = "Cancelled"; item.Version = Guid.NewGuid();
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return Conflict(new ProblemDetails { Title = "Yêu cầu đã thay đổi. Hãy tải lại." }); }
        return Ok(Map(item));
    }

    /// <summary>Danh sách yêu cầu chờ duyệt.</summary>
    [HttpGet]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Pending(CancellationToken cancellationToken)
    {
        var items = await db.ContractTermsRequests.AsNoTracking().Where(x => x.Status == "Pending")
            .OrderBy(x => x.CreatedAt).Take(100).ToListAsync(cancellationToken);
        var ids = items.Select(item => item.RequestedBy).Distinct().ToArray();
        var names = await db.Accounts.AsNoTracking().Where(account => ids.Contains(account.Id)).ToDictionaryAsync(account => account.Id, account => account.FullName, cancellationToken);
        return Ok(items.Select(item => Map(item) with { RequestedByName = names.GetValueOrDefault(item.RequestedBy) }));
    }

    /// <summary>Duyệt một tệp Word điều khoản riêng cho yêu cầu, không sửa mẫu chung.</summary>
    [HttpPut("{id:guid}/approve")]
    [Authorize(Roles = "Admin,Manager")]
    [RequestSizeLimit(2_200_000)]
    public async Task<IActionResult> Approve(Guid id, IFormFile file, [FromForm] string version, [FromForm] string? note, CancellationToken cancellationToken)
    {
        if (Actor is not Guid actor) return Unauthorized();
        if (!Guid.TryParse(version, out var expected) || note?.Length > 2000 || file.Length is 0 or > WordTemplateValidator.MaximumFileBytes || !file.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new ProblemDetails { Title = "Kiểm tra phiên bản, ghi chú và tệp Word không quá 2 MB." });
        var item = await db.ContractTermsRequests.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null) return NotFound();
        if (item.Status != "Pending" || item.Version != expected) return Conflict(new ProblemDetails { Title = "Yêu cầu đã thay đổi. Hãy tải lại." });
        await using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        var errors = WordTemplateValidator.Validate(bytes, WordTemplateSamples.Contract);
        if (errors.Count > 0) return BadRequest(new WordTemplateValidationResponse(false, errors));
        using (var word = WordprocessingDocument.Open(new MemoryStream(bytes), false))
            if (word.MainDocumentPart!.Document.Body!.InnerText.Contains(WordTemplateSamples.TermsInstruction, StringComparison.Ordinal))
                return BadRequest(new ProblemDetails { Title = "Hãy viết điều khoản trước khi duyệt." });
        item.ApprovedTemplate = bytes;
        item.Status = "Approved";
        item.DecisionNote = note?.Trim();
        item.DecidedAt = DateTimeOffset.UtcNow;
        item.DecidedBy = actor;
        item.Version = Guid.NewGuid();
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return Conflict(new ProblemDetails { Title = "Yêu cầu đã thay đổi. Hãy tải lại." }); }
        return Ok(Map(item));
    }

    /// <summary>Từ chối yêu cầu với lý do.</summary>
    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Reject(Guid id, RejectTermsRequest request, CancellationToken cancellationToken)
    {
        if (Actor is not Guid actor) return Unauthorized();
        var note = request.Note?.Trim();
        if (string.IsNullOrWhiteSpace(note) || note.Length > 2000) return BadRequest(new ProblemDetails { Title = "Nhập lý do từ chối, tối đa 2.000 ký tự." });
        var item = await db.ContractTermsRequests.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null) return NotFound();
        if (item.Status != "Pending" || item.Version != request.Version) return Conflict(new ProblemDetails { Title = "Yêu cầu đã thay đổi. Hãy tải lại." });
        item.Status = "Rejected"; item.DecisionNote = note; item.DecidedAt = DateTimeOffset.UtcNow; item.DecidedBy = actor; item.Version = Guid.NewGuid();
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return Conflict(new ProblemDetails { Title = "Yêu cầu đã thay đổi. Hãy tải lại." }); }
        return Ok(Map(item));
    }

    private static TermsRequestResponse Map(ContractTermsRequest item) => new(item.Id, item.DraftKey, item.RequestedBy, item.Reason, item.Status, item.DecisionNote, item.Version, item.CreatedAt, item.DecidedAt, null);
}

public sealed record CreateTermsRequest(Guid DraftKey, string? Reason, string? CustomerPhone);
public sealed record RejectTermsRequest(Guid Version, string? Note);
public sealed record CancelTermsRequest(Guid Version);
public sealed record TermsRequestResponse(Guid Id, Guid DraftKey, Guid RequestedBy, string Reason, string Status, string? DecisionNote, Guid Version, DateTimeOffset CreatedAt, DateTimeOffset? DecidedAt, string? RequestedByName);
