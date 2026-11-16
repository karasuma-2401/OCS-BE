using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OCS.Api.Authorization;
using OCS.Api.Interfaces.Services;

namespace OCS.Api.Controllers;

/// <summary>Đọc ảnh sản phẩm được lưu trong database.</summary>
[ApiController]
[Route("api/product-images")]
public sealed class ProductImagesController(IContractService contracts, IAuthorizationService authorization) : ControllerBase
{
    /// <summary>Ảnh công khai khi sản phẩm đang bán; ảnh khác chỉ cho nhân viên có quyền xem kho.</summary>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var image = await contracts.GetImageAsync(id, cancellationToken);
        if (image is null) return NotFound();
        if (!image.IsPublic && (!(User.IsInRole("Admin") || User.IsInRole("Manager") || User.IsInRole("Staff")) ||
            !(await authorization.AuthorizeAsync(User, PermissionCatalog.ProductsRead)).Succeeded)) return NotFound();
        Response.Headers.CacheControl = "no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(image.Content, image.ContentType);
    }
}
