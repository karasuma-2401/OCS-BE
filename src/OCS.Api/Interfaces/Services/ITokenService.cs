using OCS.Api.Entities;

namespace OCS.Api.Interfaces.Services;

public interface ITokenService
{
    string GenerateAccessToken(Account account, IEnumerable<string> roles);
}
