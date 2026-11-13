using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OCS.Api.Context;

namespace OCS.Api.Controllers;

public sealed record CategoryResponse(Guid Id, string Name, bool IsActive);

[ApiController]
[Route("api/categories")]
[AllowAnonymous]
public sealed class CategoriesController(AppDbContext dbContext) : ControllerBase
{
    /// <summary>Gets categories ordered by name.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CategoryResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CategoryResponse>>> GetAll(
        [FromQuery] bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Categories.AsNoTracking();
        if (activeOnly)
        {
            query = query.Where(c => c.IsActive);
        }

        var items = await query
            .OrderBy(c => c.Name)
            .ThenBy(c => c.Id)
            .Select(c => new CategoryResponse(c.Id, c.Name, c.IsActive))
            .ToListAsync(cancellationToken);

        return Ok(items);
    }
}

