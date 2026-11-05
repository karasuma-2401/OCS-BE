using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OCS.Api.Common;
using OCS.Api.Context;
using OCS.Api.Contracts.Brands;

namespace OCS.Api.Controllers;

[ApiController]
[Route("api/v1/brands")]
[AllowAnonymous]
public sealed class BrandsController(AppDbContext dbContext) : ControllerBase
{
    /// <summary>Gets active and inactive brands in a stable, paginated order.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<BrandResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<ActionResult<PagedResponse<BrandResponse>>> GetPage(
        [FromQuery] PagedRequest request,
        CancellationToken cancellationToken)
    {
        var brands = dbContext.Brands
            .AsNoTracking()
            .OrderBy(brand => brand.Name)
            .ThenBy(brand => brand.Id);

        var totalCount = await brands.CountAsync(cancellationToken);
        var brandRows = await brands
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(brand => new
            {
                brand.Id,
                brand.Name,
                brand.Tier,
                brand.DefaultCommissionRate,
                brand.IsActive
            })
            .ToListAsync(cancellationToken);

        var items = brandRows
            .Select(brand => new BrandResponse(
                brand.Id,
                brand.Name,
                brand.Tier.ToString(),
                brand.DefaultCommissionRate.ToString("0.00", CultureInfo.InvariantCulture),
                brand.IsActive))
            .ToList();

        var response = new PagedResponse<BrandResponse>(
            items,
            request.Page,
            request.PageSize,
            totalCount);

        return Ok(response);
    }
}
