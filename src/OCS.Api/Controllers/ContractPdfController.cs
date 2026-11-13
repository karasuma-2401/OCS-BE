using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OCS.Api.Authorization;
using OCS.Api.Interfaces.Services;

namespace OCS.Api.Controllers;

/// <summary>Xem tài liệu hợp đồng của quản lý hoặc nhân viên tiếp nhận.</summary>
[ApiController]
[Route("api/contracts")]
[Authorize(Roles = "Admin,Manager,Staff")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ContractPdfController(IContractService contracts, IAuthorizationService authorization) : ControllerBase
{
    /// <summary>Lấy PDF đã lưu.</summary>
    [HttpGet("{id:guid}/pdf")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "application/pdf")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken cancellationToken)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("Manager") &&
            (!(await authorization.AuthorizeAsync(User, PermissionCatalog.AppointmentsStaffReception)).Succeeded ||
             !(await authorization.AuthorizeAsync(User, PermissionCatalog.CustomersRead)).Succeeded)) return Forbid();
        var content = await contracts.GetPdfAsync(id, cancellationToken);
        return content is null || !content.AsSpan().StartsWith("%PDF"u8) ? NotFound() : File(content, "application/pdf");
    }

    /// <summary>Lấy tài liệu Word mới hoặc PDF lịch sử của hợp đồng.</summary>
    [HttpGet("{id:guid}/document")]
    public async Task<IActionResult> Document(Guid id, CancellationToken cancellationToken)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("Manager") &&
            (!(await authorization.AuthorizeAsync(User, PermissionCatalog.AppointmentsStaffReception)).Succeeded ||
             !(await authorization.AuthorizeAsync(User, PermissionCatalog.CustomersRead)).Succeeded)) return Forbid();
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
        if (!User.IsInRole("Admin") && !User.IsInRole("Manager") &&
            (!(await authorization.AuthorizeAsync(User, PermissionCatalog.AppointmentsStaffReception)).Succeeded ||
             !(await authorization.AuthorizeAsync(User, PermissionCatalog.CustomersRead)).Succeeded)) return Forbid();
        var products = await contracts.GetBarcodeProductsAsync(id, cancellationToken);
        return products is null ? NotFound() : Ok(products);
    }
}
