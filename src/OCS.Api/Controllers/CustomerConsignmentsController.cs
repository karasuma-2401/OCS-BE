using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OCS.Api.Models.Customers;
using OCS.Api.Services;

namespace OCS.Api.Controllers;

[ApiController]
[Route("api/customers/me/consignments")]
[Authorize(Roles = "Customer")]
public sealed class CustomerConsignmentsController(CustomerConsignmentService consignments) : ControllerBase
{
    /// <summary>Lay thong tin theo doi cac dot ky gui cua khach hang dang dang nhap.</summary>
    /// <remarks>
    /// GET /api/customers/me/consignments. Chi tai khoan Customer co phien hop le va ho so
    /// dang hoat dong duoc xem hop dong cua chinh minh; khong nhan ID khach hang tu request.
    /// Moi hop dong gom thoi han, trang thai, san pham, tien du kien va tien da quyet toan.
    /// Tien du kien gia dinh toan bo san pham ban dung gia niem yet, khong phai cong no.
    /// Khong co hop dong se tra ve mang rong.
    /// </remarks>
    /// <response code="200">Danh sach hop dong cua khach hang, co the rong.</response>
    /// <response code="401">Phien dang nhap hoac thong tin dinh danh khong hop le.</response>
    /// <response code="403">Khong co vai tro Customer hoac ho so khach hang khong hoat dong.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ConsignmentTrackingResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var customerId) || customerId == Guid.Empty)
            return Unauthorized();

        var response = await consignments.GetAsync(customerId, cancellationToken);
        if (response is null) return Forbid();
        Response.Headers.CacheControl = "no-store";
        return Ok(response);
    }
    /// <summary>Lay tep hop dong da ky cua chinh khach hang.</summary>
    [HttpGet("{contractId:guid}/document")]
    public async Task<IActionResult> GetDocument(Guid contractId, CancellationToken ct)
    {
        if (!TryGetCustomerId(out var customerId)) return Unauthorized();
        if (!await consignments.IsActiveCustomerAsync(customerId, ct)) return Forbid();
        var doc = await consignments.GetDocumentAsync(customerId, contractId, ct);
        if (doc is null) return NotFound();
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = "sandbox";
        Response.Headers.ContentDisposition = $"inline; filename=\"{doc.FileName}\"";
        return File(doc.Content, doc.ContentType);
    }

    /// <summary>Lay chi tiet san pham thuoc hop dong cua chinh khach hang.</summary>
    [HttpGet("{contractId:guid}/products/{productId:guid}")]
    [ProducesResponseType(typeof(ConsignmentProductDetailResponse), 200)]
    public async Task<IActionResult> GetProduct(Guid contractId, Guid productId, CancellationToken ct)
    {
        if (!TryGetCustomerId(out var customerId)) return Unauthorized();
        if (!await consignments.IsActiveCustomerAsync(customerId, ct)) return Forbid();
        var product = await consignments.GetProductAsync(customerId, contractId, productId, ct);
        if (product is null) return NotFound();
        Response.Headers.CacheControl = "private, no-store";
        return Ok(product);
    }

    /// <summary>Lay anh san pham sau khi kiem tra quyen so huu hop dong.</summary>
    [HttpGet("{contractId:guid}/products/{productId:guid}/images/{imageId:guid}")]
    public async Task<IActionResult> GetProductImage(Guid contractId, Guid productId, Guid imageId, CancellationToken ct)
    {
        if (!TryGetCustomerId(out var customerId)) return Unauthorized();
        if (!await consignments.IsActiveCustomerAsync(customerId, ct)) return Forbid();
        var image = await consignments.GetImageAsync(customerId, contractId, productId, imageId, ct);
        if (image is null) return NotFound();
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(image.Value.Content, image.Value.ContentType);
    }

    private bool TryGetCustomerId(out Guid id) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out id) && id != Guid.Empty;
}