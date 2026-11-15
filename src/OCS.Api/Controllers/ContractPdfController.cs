using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OCS.Api.Authorization;
using OCS.Api.Interfaces.Services;
using OCS.Api.Context;
using Microsoft.EntityFrameworkCore;
using OCS.Api.Consts;

namespace OCS.Api.Controllers;

/// <summary>Xem tài liệu hợp đồng của quản lý hoặc nhân viên tiếp nhận.</summary>
[ApiController]
[Route("api/contracts")]
[Authorize(Roles = "Admin,Manager,Staff")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ContractPdfController(IContractService contracts, IAuthorizationService authorization, AppDbContext db) : ControllerBase
{
    /// <summary>Dữ liệu để hiển thị hợp đồng trên web.</summary>
    [HttpGet("{id:guid}/web")]
    public async Task<IActionResult> Web(Guid id, CancellationToken cancellationToken)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("Manager") && !User.IsInRole("Staff") &&
            !(await authorization.AuthorizeAsync(User, PermissionCatalog.CustomersRead)).Succeeded) return Forbid();
        var contract = await db.ConsignmentContracts.AsNoTracking()
            .Include(x => x.Customer).ThenInclude(x => x!.Account)
            .Include(x => x.Products).ThenInclude(x => x.Images)
            .Include(x => x.Products).ThenInclude(x => x.Category)
            .Include(x => x.Products).ThenInclude(x => x.Brand)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (contract is null) return NotFound();
        var document = await db.ContractDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.ContractId == id, cancellationToken);
        System.Text.Json.JsonElement? template = null;
        if (document?.Content is { Length: > 0 } content && content[0] == (byte)'{')
        {
            try { template = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(content); }
            catch (System.Text.Json.JsonException) { /* Tài liệu lịch sử có thể không phải ảnh chụp mẫu web. */ }
        }
        string? title = null;
        string? organizationName = null;
        string? headerText = null;
        string? footerText = null;
        if (template is { } t)
        {
            if (t.TryGetProperty("title", out var p1) || t.TryGetProperty("Title", out p1)) title = p1.GetString();
            if (t.TryGetProperty("organizationName", out var p2) || t.TryGetProperty("OrganizationName", out p2)) organizationName = p2.GetString();
            if (t.TryGetProperty("headerText", out var p3) || t.TryGetProperty("HeaderText", out p3)) headerText = p3.GetString();
            if (t.TryGetProperty("footerText", out var p4) || t.TryGetProperty("FooterText", out p4)) footerText = p4.GetString();
        }
        return Ok(new {
            contract.Id, contract.ContractNumber, contract.SignedAt, contract.EndsAt,
            ExpiryHandling = contract.ExpiryHandling.ToString(),
            CustomerName = contract.CustomerNameSnapshot ?? contract.Customer?.Account?.FullName ?? "",
            CustomerPhone = contract.CustomerPhoneSnapshot ?? contract.Customer?.Account?.PhoneNumber ?? "",
            CustomerEmail = contract.CustomerEmailSnapshot, CustomerIdentity = contract.CustomerIdentitySnapshot,
            CustomerAddress = contract.CustomerAddressSnapshot, contract.BankCodeSnapshot,
            contract.BankAccountNumberSnapshot, contract.BankAccountHolderSnapshot,
            Terms = document?.Terms,
            Title = title,
            OrganizationName = organizationName,
            HeaderText = headerText,
            FooterText = footerText,
            Note = template is { } noteSnapshot && (noteSnapshot.TryGetProperty("note", out var note) || noteSnapshot.TryGetProperty("Note", out note)) ? note.GetString() : null,
            Template = template is not null ? new {
                Title = title,
                OrganizationName = organizationName,
                HeaderText = headerText,
                FooterText = footerText
            } : null,
            Signature = document?.SignatureContent is null || document.SignatureContentType is null ? null : new { ContentType = document.SignatureContentType, Data = Convert.ToBase64String(document.SignatureContent) },
            Products = template is { } snapshot && snapshot.TryGetProperty("products", out var snapshotProducts) && snapshotProducts.ValueKind == System.Text.Json.JsonValueKind.Array ? (object)snapshotProducts : contract.Products.Where(x => x.ApprovalStatus != ProductApprovalStatus.Rejected).OrderBy(x => x.CreatedAt).Select(x => new {
                x.Id, x.Name, x.Barcode, x.Size, Brand = x.Brand?.Name, Category = x.Category?.Name, x.Material, x.Description, x.ConditionPercentage,
                ListedPrice = x.AgreedListedPrice ?? x.ListedPrice, CommissionRate = x.AgreedCommissionRate ?? 0,
                Images = x.Images.OrderBy(i => i.SortOrder).Select(i => i.Content != null && i.ContentType != null
                    ? $"data:{i.ContentType};base64,{Convert.ToBase64String(i.Content)}" : i.Url).ToArray()
            }).ToArray()
        });
    }
    /// <summary>Lấy PDF đã lưu.</summary>
    [HttpGet("{id:guid}/pdf")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "application/pdf")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken cancellationToken)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("Manager") && !User.IsInRole("Staff") &&
            !(await authorization.AuthorizeAsync(User, PermissionCatalog.CustomersRead)).Succeeded) return Forbid();
        var content = await contracts.GetPdfAsync(id, cancellationToken);
        return content is null || !content.AsSpan().StartsWith("%PDF"u8) ? NotFound() : File(content, "application/pdf");
    }

    /// <summary>Lấy tài liệu Word mới hoặc PDF lịch sử của hợp đồng.</summary>
    [HttpGet("{id:guid}/document")]
    public async Task<IActionResult> Document(Guid id, CancellationToken cancellationToken)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("Manager") && !User.IsInRole("Staff") &&
            !(await authorization.AuthorizeAsync(User, PermissionCatalog.CustomersRead)).Succeeded) return Forbid();
        var content = await contracts.GetPdfAsync(id, cancellationToken);
        if (content is null) return NotFound();
        if (content.AsSpan().StartsWith("%PDF"u8)) return File(content, "application/pdf");
        if (content.AsSpan().StartsWith("PK"u8)) return File(content, "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        return NotFound();
    }

    /// <summary>Lấy sản phẩm của hợp đồng để xem trước và in nhãn.</summary>
    [HttpGet("{id:guid}/barcode-products")]
    public async Task<IActionResult> BarcodeProducts(Guid id, CancellationToken cancellationToken)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("Manager") && !User.IsInRole("Staff") &&
            !(await authorization.AuthorizeAsync(User, PermissionCatalog.CustomersRead)).Succeeded) return Forbid();
        var products = await contracts.GetBarcodeProductsAsync(id, cancellationToken);
        return products is null ? NotFound() : Ok(products);
    }
}
