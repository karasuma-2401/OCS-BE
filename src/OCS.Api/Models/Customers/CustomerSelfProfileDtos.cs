using System.ComponentModel.DataAnnotations;

namespace OCS.Api.Models.Customers;

/// <summary>
/// Thông tin tài khoản ngân hàng nhận tiền chi trả ký gửi.
/// </summary>
public sealed record BankAccountResponse(
    string BankCode,
    string AccountNumber,
    string AccountHolderName);

/// <summary>
/// Thông tin hồ sơ tự phục vụ của người ký gửi.
/// </summary>
public sealed record CustomerSelfProfileResponse(
    Guid Id,
    string FullName,
    string PhoneNumber,
    BankAccountResponse? BankAccount);

/// <summary>
/// Yêu cầu cập nhật thông tin tài khoản ngân hàng của người ký gửi.
/// </summary>
public sealed record UpdateBankAccountRequest : IValidatableObject
{
    /// <summary>
    /// Mã ngân hàng (ví dụ: VCB, MB, TCB).
    /// </summary>
    [Required(ErrorMessage = "Mã ngân hàng là bắt buộc.")]
    [MaxLength(50, ErrorMessage = "Mã ngân hàng không được vượt quá 50 ký tự.")]
    public string BankCode { get; init; } = string.Empty;

    /// <summary>
    /// Số tài khoản ngân hàng của người thụ hưởng.
    /// </summary>
    [Required(ErrorMessage = "Số tài khoản ngân hàng là bắt buộc.")]
    [MaxLength(50, ErrorMessage = "Số tài khoản ngân hàng không được vượt quá 50 ký tự.")]
    public string AccountNumber { get; init; } = string.Empty;

    /// <summary>
    /// Tên chủ tài khoản ngân hàng.
    /// </summary>
    [Required(ErrorMessage = "Tên chủ tài khoản là bắt buộc.")]
    [MaxLength(200, ErrorMessage = "Tên chủ tài khoản không được vượt quá 200 ký tự.")]
    public string AccountHolderName { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(BankCode))
        {
            yield return new ValidationResult("Mã ngân hàng không được để trống.", [nameof(BankCode)]);
        }

        if (string.IsNullOrWhiteSpace(AccountNumber))
        {
            yield return new ValidationResult("Số tài khoản ngân hàng không được để trống.", [nameof(AccountNumber)]);
        }

        if (string.IsNullOrWhiteSpace(AccountHolderName))
        {
            yield return new ValidationResult("Tên chủ tài khoản không được để trống.", [nameof(AccountHolderName)]);
        }
    }
}
/// <summary>
/// Yêu cầu cập nhật hồ sơ cá nhân của người ký gửi.
/// </summary>
public sealed record UpdateCustomerSelfProfileRequest : IValidatableObject
{
    /// <summary>
    /// Họ và tên của người ký gửi.
    /// </summary>
    [Required(ErrorMessage = "Họ và tên là bắt buộc.")]
    [MaxLength(150, ErrorMessage = "Họ và tên không được vượt quá 150 ký tự.")]
    public string FullName { get; init; } = string.Empty;

    /// <summary>
    /// Số điện thoại liên hệ (10 chữ số bắt đầu bằng số 0).
    /// </summary>
    [Required(ErrorMessage = "Số điện thoại là bắt buộc.")]
    [RegularExpression(@"^0[0-9]{9}$", ErrorMessage = "Số điện thoại phải gồm đúng 10 chữ số bắt đầu bằng số 0.")]
    public string PhoneNumber { get; init; } = string.Empty;

    /// <summary>
    /// Cấu hình tài khoản ngân hàng nhận tiền chi trả. Đặt null nếu muốn xóa thông tin ngân hàng.
    /// </summary>
    public UpdateBankAccountRequest? BankAccount { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(FullName))
        {
            yield return new ValidationResult("Họ và tên không được để trống.", [nameof(FullName)]);
        }

        if (string.IsNullOrWhiteSpace(PhoneNumber))
        {
            yield return new ValidationResult("Số điện thoại không được để trống.", [nameof(PhoneNumber)]);
        }
    }
}
