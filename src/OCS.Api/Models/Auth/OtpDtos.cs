namespace OCS.Api.Models.Auth;

public sealed record OtpChallengeResultDto(
    Guid ChallengeId,
    string Purpose,
    string MaskedDestination,
    DateTimeOffset ExpiresAt,
    DateTimeOffset ResendAvailableAt,
    string ContinuationToken);

public sealed record OtpVerificationResultDto(
    Guid AccountId,
    string NormalizedEmail,
    DateTimeOffset VerifiedAt);

