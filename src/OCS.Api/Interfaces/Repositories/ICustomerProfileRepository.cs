using OCS.Api.Entities;

namespace OCS.Api.Interfaces.Repositories;

public interface ICustomerProfileRepository
{
    /// <summary>
    /// Lấy hồ sơ người ký gửi dạng read-only (AsNoTracking) theo accountId của chính khách.
    /// </summary>
    Task<Account?> GetSelfProfileAsync(Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy hồ sơ người ký gửi có tracked entity để chuẩn bị cập nhật.
    /// </summary>
    Task<Account?> GetSelfProfileForUpdateAsync(Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kiểm tra xem số điện thoại đã tồn tại ở một tài khoản khách hàng khác hay chưa.
    /// </summary>
    Task<bool> IsPhoneNumberTakenAsync(string phoneNumber, Guid excludeAccountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kiểm tra xem khách hàng có lệnh giải ngân nào đang ở trạng thái Pending hay không.
    /// </summary>
    Task<bool> HasPendingPayoutAsync(Guid customerId, CancellationToken cancellationToken = default);
}
