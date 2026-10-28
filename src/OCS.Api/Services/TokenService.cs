using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Services;

namespace OCS.Api.Services;

public sealed class TokenService(IConfiguration configuration) : ITokenService
{
    public string GenerateAccessToken(Account account, IEnumerable<string> roles)
    {
        var issuer = configuration["Jwt:Issuer"] ?? "OCS";
        var audience = configuration["Jwt:Audience"] ?? "OCS";
        var key = configuration["Jwt:Key"] ?? "ocs-super-secret-key-that-is-at-least-32-bytes-long";

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, account.Id.ToString()),
            new(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new(ClaimTypes.Name, account.Username ?? account.PhoneNumber ?? account.Id.ToString()),
        };

        if (!string.IsNullOrWhiteSpace(account.PhoneNumber))
        {
            claims.Add(new Claim(ClaimTypes.MobilePhone, account.PhoneNumber));
        }

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var symmetricKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(symmetricKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
