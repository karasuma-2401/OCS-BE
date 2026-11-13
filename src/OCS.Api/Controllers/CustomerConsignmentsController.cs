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
    /// <summary>Returns the signed-in customer's consignment contracts and settlement progress.</summary>
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
}
