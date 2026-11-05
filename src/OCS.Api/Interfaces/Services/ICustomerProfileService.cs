using OCS.Api.Common;
using OCS.Api.Models.Customers;

namespace OCS.Api.Interfaces.Services;

public interface ICustomerProfileService
{
    /// <summary>
    /// Lấy thông tin hồ sơ của chính người ký gửi.
    /// </summary>
    Task<Result<CustomerSelfProfileResponse>> GetSelfProfileAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cập nhật thông tin hồ sơ của chính người ký gửi (họ tên, số điện thoại, tài khoản ngân hàng).
    /// </summary>
    Task<Result<CustomerSelfProfileResponse>> UpdateSelfProfileAsync(
        Guid customerId,
        UpdateCustomerSelfProfileRequest request,
        CancellationToken cancellationToken = default);
}
