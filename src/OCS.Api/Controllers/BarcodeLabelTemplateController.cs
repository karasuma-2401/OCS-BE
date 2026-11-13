using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OCS.Api.Extensions;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Products;

namespace OCS.Api.Controllers;

/// <summary>Cấu hình mẫu in barcode dùng chung.</summary>
[ApiController]
[Route("api/barcode-label-template")]
[Authorize(Roles = "Admin,Manager,Staff")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BarcodeLabelTemplateController(IBarcodeLabelTemplateService templates) : ControllerBase
{
    /// <summary>Lấy mẫu đang dùng để in nhãn.</summary>
    [HttpGet]
    public async Task<ActionResult<BarcodeLabelTemplateDto>> Get(CancellationToken cancellationToken) =>
        Ok(await templates.GetAsync(cancellationToken));

    /// <summary>Thay đổi mẫu in, chỉ quản lý và quản trị viên.</summary>
    [HttpPut]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Update(BarcodeLabelTemplateDto request, CancellationToken cancellationToken)
        => (await templates.UpdateAsync(request, cancellationToken)).ToActionResult(this);
}
