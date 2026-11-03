using OCS.Api.Common;
using OCS.Api.Models.Auth;

namespace OCS.Api.Interfaces.Services;

public interface IAuthService
{
    Task<Result<LoginResultDto>> LoginAsync(LoginRequestDto request, string? clientIp = null, CancellationToken cancellationToken = default);
    Task<Result<OtpChallengeResponseDto>> RegisterAsync(RegisterRequestDto request, string? clientIp = null, CancellationToken cancellationToken = default);
    Task<Result<VerifyOtpResponseDto>> VerifyRegistrationOtpAsync(VerifyOtpRequestDto request, CancellationToken cancellationToken = default);
    Task<Result<OtpChallengeResponseDto>> ResendRegistrationOtpAsync(ResendOtpRequestDto request, string? clientIp = null, CancellationToken cancellationToken = default);
    Task<Result<CurrentUserResponseDto>> GetCurrentUserAsync(Guid accountId, Guid sessionId, CancellationToken cancellationToken = default);
    Task<Result<bool>> LogoutAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
