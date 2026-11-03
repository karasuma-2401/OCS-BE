using System.ComponentModel.DataAnnotations;

namespace OCS.Api.Models.Auth;

public record LoginRequestDto(
    [Required, EmailAddress, MaxLength(254)] string Email,
    [Required] string Password);

public sealed record LoginResponseDto(
    int ExpiresIn,
    DateTimeOffset ExpiresAt,
    UserDto User);

public sealed record LoginResultDto(
    string AccessToken,
    int ExpiresIn,
    DateTimeOffset ExpiresAt,
    UserDto User);

public record RegisterRequestDto(
    [Required, MaxLength(150)] string FullName,
    [Required, EmailAddress, MaxLength(254)] string Email,
    [Required, Phone, MaxLength(20)] string PhoneNumber,
    [Required, RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*[\d\W]).{8,70}$",
        ErrorMessage = "Mật khẩu phải từ 8 đến 70 ký tự và chứa ít nhất một chữ hoa, một chữ thường và một chữ số hoặc ký tự đặc biệt.")]
    string Password);

public sealed record OtpChallengeResponseDto(
    Guid ChallengeId,
    string Purpose,
    string MaskedDestination,
    DateTimeOffset ExpiresAt,
    DateTimeOffset ResendAvailableAt);

public sealed record VerifyOtpRequestDto(
    [Required] Guid ChallengeId,
    [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "Mã OTP phải gồm 6 chữ số.")] string Code);

public sealed record VerifyOtpResponseDto(
    string Message,
    DateTimeOffset VerifiedAt);

public sealed record ResendOtpRequestDto(
    [Required] Guid ChallengeId);

public record UserDto(
    Guid Id,
    string? FullName,
    string? Email,
    string? PhoneNumber,
    List<string> Roles,
    string? Username = null);
