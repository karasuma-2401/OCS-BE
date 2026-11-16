using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OCS.Api.Common;
using OCS.Api.Authorization;
using OCS.Api.Consts;
using OCS.Api.Extensions;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Appointments;

namespace OCS.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AppointmentsController(IAppointmentService appointmentService, IAuthorizationService authorizationService) : ControllerBase
{
    /// <summary>
    /// Lấy danh sách các khung giờ và số chỗ còn trống theo ngày đã chọn.
    /// </summary>
    [HttpGet("available-slots")]
    [AllowAnonymous]
    public async Task<IActionResult> GetAvailableSlots(
        [FromQuery] DateOnly date,
        CancellationToken cancellationToken)
    {
        var result = await appointmentService.GetAvailableSlotsAsync(date, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>
    /// Khách hàng đặt lịch hẹn ký gửi mới.
    /// </summary>
    [HttpPost]
    [Authorize]
    [Authorize(Policy = PermissionCatalog.AppointmentsCreate)]
    public async Task<IActionResult> Create(
        [FromBody] CreateAppointmentRequest request,
        CancellationToken cancellationToken)
    {
        var customerId = GetCurrentUserId();
        if (customerId == Guid.Empty)
        {
            return Unauthorized();
        }

        var result = await appointmentService.CreateAsync(customerId, request, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>
    /// Khách hàng xem lịch sử đặt hẹn của mình.
    /// </summary>
    [HttpGet("my-history")]
    [Authorize]
    [Authorize(Policy = PermissionCatalog.AppointmentsOwnRead)]
    public async Task<IActionResult> GetMyHistory(CancellationToken cancellationToken)
    {
        var customerId = GetCurrentUserId();
        if (customerId == Guid.Empty)
        {
            return Unauthorized();
        }

        var result = await appointmentService.GetCustomerHistoryAsync(customerId, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>
    /// Xem chi tiết một lịch hẹn theo Id.
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        var isStaff = (User.IsInRole("Staff") || User.IsInRole("Manager") || User.IsInRole("Admin"))
            && (await authorizationService.AuthorizeAsync(User, PermissionCatalog.AppointmentsStaffRead)).Succeeded;
        if (!isStaff && !(await authorizationService.AuthorizeAsync(User, PermissionCatalog.AppointmentsOwnRead)).Succeeded)
            return Forbid();

        var result = await appointmentService.GetByIdAsync(id, currentUserId, isStaff, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>
    /// Khách hàng hủy lịch hẹn theo quy định.
    /// </summary>
    [HttpPut("{id:guid}/cancel")]
    [Authorize]
    [Authorize(Policy = PermissionCatalog.AppointmentsOwnCancel)]
    public async Task<IActionResult> Cancel(
        Guid id,
        [FromBody] CancelAppointmentRequest request,
        CancellationToken cancellationToken)
    {
        var customerId = GetCurrentUserId();
        if (customerId == Guid.Empty)
        {
            return Unauthorized();
        }

        var result = await appointmentService.CancelByCustomerAsync(id, customerId, request, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>
    /// Nhân viên xem danh sách lịch hẹn (lọc theo ngày, khung giờ, trạng thái, tìm kiếm thông tin khách).
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Admin,Manager,Staff")]
    [Authorize(Policy = PermissionCatalog.AppointmentsStaffRead)]
    public async Task<IActionResult> GetAppointments(
        [FromQuery] DateOnly? date,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] Guid? timeSlotId,
        [FromQuery] AppointmentStatus? status,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var result = await appointmentService.GetStaffAppointmentsAsync(date, timeSlotId, status, search, fromDate, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>
    /// Nhân viên cập nhật trạng thái lịch hẹn: xác nhận khách đến (CheckedIn), vắng mặt (NoShow) hoặc hủy (Cancelled).
    /// </summary>
    [HttpPatch("{id:guid}/status")]
    [Authorize(Roles = "Admin,Manager,Staff")]
    [Authorize(Policy = PermissionCatalog.AppointmentsStaffUpdate)]
    public async Task<IActionResult> UpdateStatus(
        Guid id,
        [FromBody] UpdateAppointmentStatusRequest request,
        CancellationToken cancellationToken)
    {
        var staffId = GetCurrentUserId();
        var result = await appointmentService.UpdateStatusByStaffAsync(id, request, staffId, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>
    /// Bắt đầu tiếp nhận từ một lịch hẹn hợp lệ (Chuyển sang quy trình tiếp nhận & kiểm định).
    /// </summary>
    [HttpPost("{id:guid}/start-reception")]
    [Authorize(Roles = "Admin,Manager,Staff")]
    [Authorize(Policy = PermissionCatalog.AppointmentsStaffReception)]
    public async Task<IActionResult> StartReception(
        Guid id,
        CancellationToken cancellationToken)
    {
        var staffId = GetCurrentUserId();
        var result = await appointmentService.StartReceptionAsync(id, staffId, cancellationToken);
        return result.ToActionResult();
    }

    private Guid GetCurrentUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}
