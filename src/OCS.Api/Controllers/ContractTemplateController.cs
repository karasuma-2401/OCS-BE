using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OCS.Api.Extensions;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Contracts;
using OCS.Api.Services;

namespace OCS.Api.Controllers;

/// <summary>Quản lý mẫu PDF hợp đồng của cửa hàng.</summary>
[ApiController]
[Route("api/contract-template")]
[Authorize(Roles = "Admin,Manager")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ContractTemplateController(IContractTemplateService templates) : ControllerBase
{
    /// <summary>Tải mẫu nền tham khảo để tự chỉnh sửa.</summary>
    [HttpGet("sample-background")]
    public IActionResult SampleBackground() => File(ContractPdfRenderer.CreateSampleBackground(), "application/pdf", "mau-nen-hop-dong.pdf");

    /// <summary>Lấy mẫu và điều khoản mặc định để quản lý.</summary>
    [HttpGet]
    public async Task<ActionResult<ContractTemplateResponse>> Get(CancellationToken cancellationToken) => Ok(await templates.GetAsync(cancellationToken));

    /// <summary>Lưu mẫu mới; phiên bản cũ không thay đổi các PDF đã tạo.</summary>
    [HttpPut]
    public async Task<IActionResult> Update(UpdateContractTemplateRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId)) return Unauthorized();
        return (await templates.UpdateAsync(request, actorId, cancellationToken)).ToActionResult(this);
    }

    /// <summary>Xem PDF minh họa bằng mẫu đang chỉnh mà không lưu.</summary>
    [HttpPost("preview")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "application/pdf")]
    public IActionResult Preview(UpdateContractTemplateRequest request)
    {
        var result = templates.Preview(request);
        return result.IsSuccess ? File(result.Data!, "application/pdf") : result.ToActionResult(this);
    }
}
