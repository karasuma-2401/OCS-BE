using System.ComponentModel.DataAnnotations;

namespace OCS.Api.Models.Accounts;

public sealed record UpdateEmployeeProfileRequest(
    [Required, MaxLength(200)] string FullName,
    [Required, Phone, MaxLength(20)] string PhoneNumber,
    [Required, MaxLength(100)] string JobTitle,
    [Required] DateOnly? HiredOn);
