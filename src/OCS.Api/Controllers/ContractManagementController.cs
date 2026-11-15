using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OCS.Api.Interfaces.Services;

namespace OCS.Api.Controllers;

/// <summary>Danh sách hợp đồng dành cho quản lý.</summary>
[ApiController]
[Route("api/contracts")]
[Authorize(Roles = "Admin,Manager")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ContractManagementController(IContractService contracts) : ControllerBase
{
    /// <summary>Tra cứu hợp đồng theo trang và bộ lọc.</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] string? query = null,
        [FromQuery] string? status = null,
        [FromQuery] DateTimeOffset? fromDate = null,
        [FromQuery] DateTimeOffset? toDate = null,
        CancellationToken cancellationToken = default) =>
        Ok(await contracts.ListAsync(page, query, status, fromDate, toDate, cancellationToken));
}
