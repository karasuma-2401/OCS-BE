using System.ComponentModel.DataAnnotations;
using OCS.Api.Models.Auth;

namespace OCS.Api.Models.Accounts;

public sealed record ResetAccountPasswordRequest(
    [BcryptPassword, RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*[\d\W]).{8,70}$")]
    string? Password,
    bool UseDefaultPassword = false);
