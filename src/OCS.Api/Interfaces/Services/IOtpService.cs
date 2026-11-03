using OCS.Api.Common;
using OCS.Api.Entities;
using OCS.Api.Models.Auth;

namespace OCS.Api.Interfaces.Services;

public interface IOtpService
{
    Task<Result<OtpChallengeResultDto>> IssueRegistrationOtpAsync(
        Account account,
        string? clientIp,
        CancellationToken cancellationToken = default);

    Task<Result<OtpChallengeResultDto>> ResendOtpAsync(
        Guid challengeId,
        string? clientIp,
        CancellationToken cancellationToken = default);

    Task<Result<OtpVerificationResultDto>> VerifyOtpAsync(
        Guid challengeId,
        string code,
        string purpose,
        CancellationToken cancellationToken = default);
}

