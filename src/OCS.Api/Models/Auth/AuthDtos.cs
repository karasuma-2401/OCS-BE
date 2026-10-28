using System.ComponentModel.DataAnnotations;

namespace OCS.Api.Models.Auth;

public record LoginRequestDto(
    [Required] string Username,
    [Required] string Password);

public record RegisterRequestDto(
    [Required] string FullName,
    [Required] string Username,
    [Required, EmailAddress] string Email,
    [Required, Phone] string PhoneNumber,
    [Required, MinLength(6)] string Password);

public record UserDto(
    Guid Id,
    string? Username,
    string? FullName,
    string? Email,
    string? PhoneNumber,
    List<string> Roles);

public record AuthResponseDto(
    string AccessToken,
    int ExpiresIn,
    UserDto User);

