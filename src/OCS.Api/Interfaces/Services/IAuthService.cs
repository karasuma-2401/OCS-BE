using OCS.Api.Common;
using OCS.Api.Models.Auth;

namespace OCS.Api.Interfaces.Services;

public interface IAuthService
{
    Task<Result<AuthResponseDto>> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default);
    Task<Result<UserDto>> RegisterAsync(RegisterRequestDto request, CancellationToken cancellationToken = default);
}

