using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OCS.Api.Authorization;
using OCS.Api.Extensions;
using OCS.Api.Models.Reports;
using OCS.Api.Services;

namespace OCS.Api.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize(Policy = PermissionCatalog.ReportsRead)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ReportsController(ReportService reports) : ControllerBase
{
    [HttpGet("overview")]
    public async Task<IActionResult> Overview([FromQuery] ReportFilterRequest filter, CancellationToken ct) =>
        (await reports.GetOverviewAsync(filter, ct)).ToActionResult(this);

    [HttpGet("pos-revenue")]
    public async Task<IActionResult> PosRevenue([FromQuery] ReportFilterRequest filter, CancellationToken ct) =>
        (await reports.GetPosRevenueAsync(filter, ct)).ToActionResult(this);

    [HttpGet("consignment-commissions")]
    public async Task<IActionResult> ConsignmentCommissions([FromQuery] ReportFilterRequest filter, CancellationToken ct) =>
        (await reports.GetConsignmentCommissionsAsync(filter, ct)).ToActionResult(this);

    [HttpGet("buyout-profit")]
    public async Task<IActionResult> BuyoutProfit([FromQuery] ReportFilterRequest filter, CancellationToken ct) =>
        (await reports.GetBuyoutProfitAsync(filter, ct)).ToActionResult(this);

    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] string reportType,
        [FromQuery] ReportFilterRequest filter,
        CancellationToken ct)
    {
        var result = await reports.ExportCsvAsync(reportType, filter, ct);
        if (!result.IsSuccess) return result.ToActionResult(this);
        return File(result.Data.Content, "text/csv; charset=utf-8", result.Data.FileName);
    }
}
