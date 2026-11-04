using OCS.Api.Interfaces.Services;
using System.Text;

namespace OCS.Api.Services;

public sealed class PasswordHasher : IPasswordHasher
{
    public string HashPassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        if (Encoding.UTF8.GetByteCount(password) > 72)
        {
            throw new ArgumentException("BCrypt passwords must not exceed 72 UTF-8 bytes.", nameof(password));
        }
        return BCrypt.Net.BCrypt.HashPassword(password);
    }

    public bool VerifyPassword(string password, string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(passwordHash) || Encoding.UTF8.GetByteCount(password) > 72)
        {
            return false;
        }

        try
        {
            return BCrypt.Net.BCrypt.Verify(password, passwordHash);
        }
        catch
        {
            return false;
        }
    }
}

