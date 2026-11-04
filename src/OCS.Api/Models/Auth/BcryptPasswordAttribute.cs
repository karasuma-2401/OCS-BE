using System.ComponentModel.DataAnnotations;
using System.Text;

namespace OCS.Api.Models.Auth;

/// <summary>BCrypt ignores input after 72 UTF-8 bytes; reject it before hashing or verification.</summary>
public sealed class BcryptPasswordAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => value is not string password || Encoding.UTF8.GetByteCount(password) <= 72;
}
