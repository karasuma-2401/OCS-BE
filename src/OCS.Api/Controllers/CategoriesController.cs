using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OCS.Api.Authorization;
using OCS.Api.Context;
using OCS.Api.Contracts.Categories;

namespace OCS.Api.Controllers;

[ApiController]
[Route("api/categories")]
[Authorize(Roles = "Admin,Manager,Staff")]
[Authorize(Policy = PermissionCatalog.AppointmentsStaffReception)]
public sealed class CategoriesController(AppDbContext db) : ControllerBase
{
    /// <summary>Lay danh muc san pham cho quy trinh tiep nhan va kiem dinh.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CategoryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get([FromQuery] bool activeOnly = false, CancellationToken cancellationToken = default)
    {
        var query = db.Categories.AsNoTracking();
        if (activeOnly) query = query.Where(c => c.IsActive);
        var categories = await query.OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Select(c => new CategoryResponse(c.Id, c.Name, c.IsActive)).ToListAsync(cancellationToken);
        return Ok(categories);
    }
}
