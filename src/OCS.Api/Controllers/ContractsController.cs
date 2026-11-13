using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OCS.Api.Authorization;
using OCS.Api.Extensions;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Contracts;
using OCS.Api.Models.Products;
using OCS.Api.Context;
using OCS.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace OCS.Api.Controllers;

[ApiController]
[Route("api/contracts")]
[Authorize(Roles = "Admin,Manager,Staff")]
[Authorize(Policy = PermissionCatalog.AppointmentsStaffReception)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ContractsController(IContractService contracts, IAuthorizationService authorization) : ControllerBase
{
    /// <summary>Tạo mã barcode cho sản phẩm đang được thêm vào hợp đồng.</summary>
    [HttpGet("barcode/new")]
    [Authorize(Policy = PermissionCatalog.ProductsCreate)]
    public async Task<ActionResult<GeneratedBarcodeResponse>> GenerateBarcode(CancellationToken cancellationToken) =>
        Ok(new GeneratedBarcodeResponse(await contracts.GenerateBarcodeAsync(cancellationToken)));
    /// <summary>Lập hợp đồng cơ bản từ lịch hẹn hoặc khách đến trực tiếp.</summary>
    [HttpPost]
    [RequestSizeLimit(12_000_000)]
    [ProducesResponseType(typeof(ContractResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CreateContractRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var employeeId)) return Unauthorized();
        if (!(await authorization.AuthorizeAsync(User, PermissionCatalog.CustomersRead)).Succeeded) return Forbid();
        if (request.Products?.Count > 0 && !(await authorization.AuthorizeAsync(User, PermissionCatalog.ProductsCreate)).Succeeded) return Forbid();
        var result = await contracts.CreateAsync(request, employeeId, cancellationToken);
        return result.IsSuccess ? CreatedAtAction(nameof(GetById), new { id = result.Data!.Id }, result.Data) : result.ToActionResult(this);
    }

    /// <summary>Xem trước hợp đồng dưới dạng PDF từ thông tin đang nhập, không tạo hợp đồng hoặc hồ sơ khách.</summary>
    [HttpPost("preview")]
    [RequestSizeLimit(12_000_000)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "application/pdf")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Preview([FromBody] CreateContractRequest request, [FromQuery] string? format = null, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var employeeId)) return Unauthorized();
        if (!(await authorization.AuthorizeAsync(User, PermissionCatalog.CustomersRead)).Succeeded) return Forbid();
        if (request.Products?.Count > 0 && !(await authorization.AuthorizeAsync(User, PermissionCatalog.ProductsCreate)).Succeeded) return Forbid();
        if (string.Equals(format, "word", StringComparison.OrdinalIgnoreCase))
        {
            var wordResult = await contracts.PreviewWordAsync(request, employeeId, cancellationToken);
            return wordResult.IsSuccess ? File(wordResult.Data!, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "hop-dong-xem-truoc.docx") : wordResult.ToActionResult(this);
        }
        var result = await contracts.PreviewAsync(request, employeeId, cancellationToken);
        return result.IsSuccess ? File(result.Data!, "application/pdf", "hop-dong-xem-truoc.pdf") : result.ToActionResult(this);
    }

    /// <summary>Xem trước hợp đồng dưới dạng tệp Word từ thông tin đang nhập.</summary>
    [HttpPost("preview-word")]
    [RequestSizeLimit(12_000_000)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    public async Task<IActionResult> PreviewWord([FromBody] CreateContractRequest request, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var employeeId)) return Unauthorized();
        if (!(await authorization.AuthorizeAsync(User, PermissionCatalog.CustomersRead)).Succeeded) return Forbid();
        if (request.Products?.Count > 0 && !(await authorization.AuthorizeAsync(User, PermissionCatalog.ProductsCreate)).Succeeded) return Forbid();
        var result = await contracts.PreviewWordAsync(request, employeeId, cancellationToken);
        return result.IsSuccess ? File(result.Data!, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "hop-dong-xem-truoc.docx") : result.ToActionResult(this);
    }

    /// <summary>Lấy mẫu và điều khoản mặc định để lập hợp đồng.</summary>
    [HttpGet("template")]
    public async Task<ActionResult<ContractTemplateResponse>> Template([FromServices] IContractTemplateService templates, [FromServices] AppDbContext db, CancellationToken cancellationToken)
    {
        var version = await db.WordDocumentTemplates.AsNoTracking().Where(x => x.Kind == WordTemplateSamples.Contract)
            .Select(x => (Guid?)x.Version).SingleOrDefaultAsync(cancellationToken);
        return Ok((await templates.GetAsync(cancellationToken)) with { Version = version ?? Guid.Empty, DefaultTerms = "", PdfBase64 = null, FieldMappings = [] });
    }

    /// <summary>Tra cứu hồ sơ khách theo số điện thoại chính xác.</summary>
    [HttpGet("customers/by-phone")]
    [Authorize(Policy = PermissionCatalog.CustomersRead)]
    [ProducesResponseType(typeof(ContractCustomerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> FindCustomerByPhone([FromQuery] string phone, CancellationToken cancellationToken) =>
        (await contracts.FindCustomerByPhoneAsync(phone, cancellationToken)).ToActionResult(this);

    /// <summary>Lấy thông tin khách và giải ngân để lập hợp đồng.</summary>
    [HttpGet("customers/{id:guid}")]
    [Authorize(Policy = PermissionCatalog.CustomersRead)]
    public async Task<IActionResult> GetCustomer(Guid id, CancellationToken cancellationToken) =>
        (await contracts.GetCustomerAsync(id, cancellationToken)).ToActionResult(this);

    /// <summary>Cập nhật các thông tin được phép của khách tại quầy.</summary>
    [HttpPut("customers/{id:guid}")]
    [Authorize(Policy = PermissionCatalog.CustomersRead)]
    [Authorize(Policy = PermissionCatalog.CustomersUpdate)]
    public async Task<IActionResult> UpdateCustomer(Guid id, UpdateContractCustomerRequest request, CancellationToken cancellationToken) =>
        (await contracts.UpdateCustomerAsync(id, request, cancellationToken)).ToActionResult(this);

    /// <summary>Lấy hồ sơ của nhân viên đang lập hợp đồng.</summary>
    [HttpGet("employee/me")]
    public async Task<IActionResult> GetCurrentEmployee(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return Unauthorized();
        return (await contracts.GetEmployeeAsync(id, cancellationToken)).ToActionResult(this);
    }

    /// <summary>Lấy danh mục, thương hiệu và vị trí kho đang hoạt động.</summary>
    [HttpGet("options")]
    [Authorize(Policy = PermissionCatalog.ProductsCreate)]
    public async Task<IActionResult> Options(CancellationToken cancellationToken) => Ok(await contracts.GetOptionsAsync(cancellationToken));

    /// <summary>Lấy thông tin cơ bản của hợp đồng nội bộ.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.CustomersRead)]
    [ProducesResponseType(typeof(ContractResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken) =>
        (await contracts.GetAsync(id, cancellationToken)).ToActionResult(this);

}
