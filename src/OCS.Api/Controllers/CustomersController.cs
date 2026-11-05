using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OCS.Api.Common;
using OCS.Api.Extensions;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Customers;

namespace OCS.Api.Controllers;

[ApiController]
[Route("api/v1/customers")]
[Authorize(Roles = "Customer")]
public sealed class CustomersController(ICustomerProfileService customerProfileService) : ControllerBase
{
    /// <summary>
    /// Lấy thông tin hồ sơ tự phục vụ của chính người ký gửi đang đăng nhập.
    /// </summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(CustomerSelfProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSelfProfile(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentCustomerId(out var customerId))
        {
            return Result.Failure<CustomerSelfProfileResponse>(
                ApplicationError.Unauthorized(ApiErrorCodes.Unauthorized, "Phiên đăng nhập không hợp lệ hoặc thiếu thông tin định danh."))
                .ToActionResult(this);
        }

        var result = await customerProfileService.GetSelfProfileAsync(customerId, cancellationToken);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Cập nhật thông tin hồ sơ tự phục vụ của chính người ký gửi đang đăng nhập.
    /// </summary>
    [HttpPut("me")]
    [ProducesResponseType(typeof(CustomerSelfProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateSelfProfile(
        [FromBody] UpdateCustomerSelfProfileRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentCustomerId(out var customerId))
        {
            return Result.Failure<CustomerSelfProfileResponse>(
                ApplicationError.Unauthorized(ApiErrorCodes.Unauthorized, "Phiên đăng nhập không hợp lệ hoặc thiếu thông tin định danh."))
                .ToActionResult(this);
        }

        var result = await customerProfileService.UpdateSelfProfileAsync(customerId, request, cancellationToken);
        return result.ToActionResult(this);
    }

    private bool TryGetCurrentCustomerId(out Guid customerId)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out customerId) && customerId != Guid.Empty;
    }
}
