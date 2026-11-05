using System.ComponentModel.DataAnnotations;

namespace OCS.Api.Models.Accounts;

public sealed record UpdateAccountInfoRequest(
    [Required, MaxLength(200)] string FullName,
    [Required, EmailAddress, MaxLength(254)] string Email,
    [Required, Phone, MaxLength(20)] string PhoneNumber,
    [MaxLength(100)] string? Username);
