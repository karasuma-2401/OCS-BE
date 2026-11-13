using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OCS.Api.Authorization;
using OCS.Api.Extensions;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Consignments;

namespace OCS.Api.Controllers;

[ApiController]
[Route("api/consignments")]
[Authorize(Roles = "Admin,Manager,Staff")]
public sealed class ConsignmentsController(IConsignmentService consignmentService) : ControllerBase
{
    /// <summary>
    /// Tạo phiếu tiếp nhận ký gửi hoặc thu mua đứt từ lịch hẹn hoặc khách vãng lai.
    /// </summary>
    [HttpPost("reception")]
    [Authorize(Policy = PermissionCatalog.AppointmentsStaffReception)]
    [ProducesResponseType(typeof(ReceptionContractResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> CreateReception(
        [FromBody] CreateReceptionRequest request,
        CancellationToken cancellationToken)
    {
        var staffId = GetCurrentUserId();
        if (staffId == Guid.Empty)
        {
            return Unauthorized();
        }

        var result = await consignmentService.CreateReceptionAsync(staffId, request, cancellationToken);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Lấy danh sách hợp đồng tiếp nhận ký gửi gần đây.
    /// </summary>
    [HttpGet("contracts")]
    [Authorize(Policy = PermissionCatalog.ProductsRead)]
    [ProducesResponseType(typeof(IReadOnlyList<ConsignmentContractSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetContracts(CancellationToken cancellationToken)
    {
        var result = await consignmentService.GetContractsAsync(cancellationToken);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Đếm số lượng sản phẩm từng ký gửi của khách hàng (theo customerId hoặc số điện thoại).
    /// </summary>
    [HttpGet("customer-history-count")]
    [Authorize(Policy = PermissionCatalog.CustomersRead)]
    public async Task<IActionResult> GetCustomerItemHistoryCount(
        [FromQuery] Guid? customerId,
        [FromQuery] string? phoneNumber,
        CancellationToken cancellationToken)
    {
        var result = await consignmentService.GetCustomerItemHistoryCountAsync(customerId, phoneNumber, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.ToActionResult(this);
        }

        return Ok(new { count = result.Data });
    }

    /// <summary>
    /// Lưu nháp đợt tiếp nhận (ràng buộc quyền tiếp nhận của nhân viên).
    /// </summary>
    [HttpPost("draft")]
    [Authorize(Policy = PermissionCatalog.AppointmentsStaffReception)]
    public async Task<IActionResult> SaveDraft(
        [FromBody] SaveReceptionDraftRequest request,
        CancellationToken cancellationToken)
    {
        var staffId = GetCurrentUserId();
        if (staffId == Guid.Empty)
        {
            return Unauthorized();
        }

        var result = await consignmentService.SaveDraftAsync(staffId, request, cancellationToken);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Lấy bản nháp tiếp nhận theo DraftId.
    /// </summary>
    [HttpGet("draft/{id:guid}")]
    [Authorize(Policy = PermissionCatalog.AppointmentsStaffReception)]
    public async Task<IActionResult> GetDraft(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await consignmentService.GetDraftAsync(id, cancellationToken);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Lấy bản nháp tiếp nhận theo AppointmentId (nếu có).
    /// </summary>
    [HttpGet("draft/by-appointment/{appointmentId:guid}")]
    [Authorize(Policy = PermissionCatalog.AppointmentsStaffReception)]
    public async Task<IActionResult> GetDraftByAppointment(
        Guid appointmentId,
        CancellationToken cancellationToken)
    {
        var result = await consignmentService.GetDraftByAppointmentAsync(appointmentId, cancellationToken);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Lấy bản nháp tiếp nhận khách vãng lai gần nhất của nhân viên hiện tại.
    /// </summary>
    [HttpGet("draft/walkin")]
    [Authorize(Policy = PermissionCatalog.AppointmentsStaffReception)]
    public async Task<IActionResult> GetLatestWalkInDraft(CancellationToken cancellationToken)
    {
        var staffId = GetCurrentUserId();
        if (staffId == Guid.Empty)
        {
            return Unauthorized();
        }

        var result = await consignmentService.GetLatestWalkInDraftAsync(staffId, cancellationToken);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Xóa bản nháp tiếp nhận theo DraftId.
    /// </summary>
    [HttpDelete("draft/{id:guid}")]
    [Authorize(Policy = PermissionCatalog.AppointmentsStaffReception)]
    public async Task<IActionResult> DeleteDraft(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await consignmentService.DeleteDraftAsync(id, cancellationToken);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Xóa bản nháp tiếp nhận theo AppointmentId.
    /// </summary>
    [HttpDelete("draft/by-appointment/{appointmentId:guid}")]
    [Authorize(Policy = PermissionCatalog.AppointmentsStaffReception)]
    public async Task<IActionResult> DeleteDraftByAppointment(
        Guid appointmentId,
        CancellationToken cancellationToken)
    {
        var result = await consignmentService.DeleteDraftByAppointmentAsync(appointmentId, cancellationToken);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Xóa bản nháp tiếp nhận khách vãng lai của nhân viên hiện tại.
    /// </summary>
    [HttpDelete("draft/walkin")]
    [Authorize(Policy = PermissionCatalog.AppointmentsStaffReception)]
    public async Task<IActionResult> DeleteWalkInDraft(CancellationToken cancellationToken)
    {
        var staffId = GetCurrentUserId();
        if (staffId == Guid.Empty)
        {
            return Unauthorized();
        }

        var result = await consignmentService.DeleteWalkInDraftAsync(staffId, cancellationToken);
        return result.ToActionResult(this);
    }

    private Guid GetCurrentUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}

