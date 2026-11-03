using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OCS.Api.Configuration;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Services;

namespace OCS.Api.Services;

public sealed class TokenService(
    IConfiguration configuration,
    TimeProvider? timeProvider = null,
    IOptions<AuthOptions>? authOptions = null) : ITokenService
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly int _sessionExpirySeconds = authOptions?.Value.SessionExpirySeconds ?? 28800;

    public string GenerateAccessToken(Account account, IEnumerable<string> roles, Guid? sessionId = null)
    {
        var issuer = configuration["Jwt:Issuer"] ?? "OCS";
        var audience = configuration["Jwt:Audience"] ?? "OCS";
        var key = configuration["Jwt:Key"] ?? "ocs-super-secret-key-that-is-at-least-32-bytes-long";

        var sid = (sessionId ?? Guid.NewGuid()).ToString();

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, account.Id.ToString()),
            new(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new(ClaimTypes.Name, account.FullName ?? account.Username ?? account.Email ?? account.Id.ToString()),
            new(JwtRegisteredClaimNames.Sid, sid),
            new("sid", sid)
        };

        if (!string.IsNullOrWhiteSpace(account.Email))
        {
            claims.Add(new Claim(ClaimTypes.Email, account.Email));
        }

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

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: now.AddSeconds(_sessionExpirySeconds),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
