using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using OCS.Api.Common;
using OCS.Api.Contracts.Products;
using OCS.Api.Authorization;
using OCS.Api.Extensions;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Products;

namespace OCS.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController(IProductService productService) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PagedResponse<PublicProductResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> GetAll(
        [FromQuery] GetPublicProductsRequest request,
        CancellationToken cancellationToken)
    {
        var products = await productService.GetPublicProductsAsync(request, cancellationToken);
        return Ok(products);
    }

    [HttpGet("{id:guid}/public")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PublicProductDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> GetPublicDetail(Guid id, CancellationToken cancellationToken)
    {
        var result = await productService.GetPublicProductDetailAsync(id, cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,Manager,Staff")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await productService.GetByIdAsync(id, cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("barcode/{barcode}")]
    [Authorize(Roles = "Admin,Manager,Staff")]
    public async Task<IActionResult> GetByBarcode(string barcode, CancellationToken cancellationToken)
    {
        var result = await productService.GetByBarcodeAsync(barcode, cancellationToken);
        return result.ToActionResult();
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Manager,Staff")]
    [Authorize(Policy = PermissionCatalog.ProductsCreate)]
    public async Task<IActionResult> Create([FromBody] CreateProductDto dto, CancellationToken cancellationToken)
    {
        var result = await productService.CreateAsync(dto, cancellationToken);
        return result.ToActionResult();
    }
}

