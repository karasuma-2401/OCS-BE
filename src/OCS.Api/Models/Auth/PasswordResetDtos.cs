using System.ComponentModel.DataAnnotations;

namespace OCS.Api.Models.Auth;

public sealed record PasswordResetRequestDto([Required, EmailAddress, MaxLength(254)] string Email);
public sealed record PasswordResetVerifyDto(
    [Required, EmailAddress, MaxLength(254)] string Email,
    [Required, RegularExpression(@"^[0-9]{6}$")] string Code);
public sealed record PasswordResetCompleteDto(
    [Required, EmailAddress, MaxLength(254)] string Email,
    [Required, MaxLength(128)] string ResetToken,
    [Required, BcryptPassword(ErrorMessage = "Mật khẩu không được vượt quá 72 byte UTF-8."),
     RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*[\d\W]).{8,70}$", ErrorMessage = "Mật khẩu phải từ 8 đến 70 ký tự và chứa chữ hoa, chữ thường và chữ số hoặc ký tự đặc biệt.")]
    string Password);
public sealed record PasswordResetTokenDto(string ResetToken);
