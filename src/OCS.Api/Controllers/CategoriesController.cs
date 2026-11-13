using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OCS.Api.Context;
using OCS.Api.Contracts.Categories;

namespace OCS.Api.Controllers;

[ApiController]
[Route("api/categories")]
[AllowAnonymous]
public sealed class CategoriesController(AppDbContext db) : ControllerBase
{
    /// <summary>Lay danh muc san pham theo trang thai hoat dong.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CategoryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromQuery] bool activeOnly = true, CancellationToken cancellationToken = default)
    {
        var query = db.Categories.AsNoTracking();
        if (activeOnly) query = query.Where(c => c.IsActive);
        var categories = await query.OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Select(c => new CategoryResponse(c.Id, c.Name, c.IsActive)).ToListAsync(cancellationToken);
        return Ok(categories);
    }
}
