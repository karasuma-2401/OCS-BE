using System.ComponentModel.DataAnnotations;
using OCS.Api.Models.Auth;

namespace OCS.Api.Models.Accounts;

public sealed record CreateInternalAccountRequest(
    [Required, MaxLength(200)] string FullName,
    [Required, EmailAddress, MaxLength(254)] string Email,
    [Required, Phone, MaxLength(20)] string PhoneNumber,
    [Required, MaxLength(100)] string Username,
    [Required, BcryptPassword, RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*[\d\W]).{8,70}$")]
    string Password,
    [Required, MaxLength(100)] string JobTitle,
    [Required] DateOnly? HiredOn,
    [Required] string Role);
