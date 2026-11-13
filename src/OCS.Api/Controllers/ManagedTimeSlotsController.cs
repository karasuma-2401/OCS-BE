using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OCS.Api.Extensions;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Appointments;

namespace OCS.Api.Controllers;

/// <summary>Quản lý khung giờ tiếp nhận cho toàn cửa hàng.</summary>
[ApiController]
[Route("api/appointment-slots")]
[Authorize(Roles = "Admin,Manager")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ManagedTimeSlotsController(IManagedTimeSlotService slots) : ControllerBase
{
    /// <summary>Liệt kê mọi khung giờ, kể cả khung giờ đã ngừng áp dụng.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ManagedTimeSlotResponse>>> List(CancellationToken cancellationToken) => Ok(await slots.ListAsync(cancellationToken));

    /// <summary>Lấy một khung giờ theo mã.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var slot = await slots.GetAsync(id, cancellationToken);
        return slot is null ? NotFound() : Ok(slot);
    }

    /// <summary>Tạo khung giờ mới.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(SaveManagedTimeSlotRequest request, CancellationToken cancellationToken)
    {
        var result = await slots.CreateAsync(request, cancellationToken);
        return result.IsSuccess ? CreatedAtAction(nameof(Get), new { id = result.Data!.Id }, result.Data) : result.ToActionResult(this);
    }

    /// <summary>Chỉnh sức chứa hoặc trạng thái; lịch đã tạo giữ nguyên giờ.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, SaveManagedTimeSlotRequest request, CancellationToken cancellationToken) =>
        (await slots.UpdateAsync(id, request, cancellationToken)).ToActionResult(this);
}
