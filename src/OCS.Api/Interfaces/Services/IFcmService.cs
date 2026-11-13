using OCS.Api.Models.Notifications;

namespace OCS.Api.Interfaces.Services;

public interface IFcmService
{
    bool IsConfigured { get; }

    Task RegisterTokenAsync(
        Guid accountId,
        string token,
        string deviceType = "Web",
        CancellationToken cancellationToken = default);

    Task UnregisterTokenAsync(
        Guid accountId,
        string token,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FcmTokenRecord>> GetTokensForAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    Task<FcmDispatchResult> SendPushToUserAsync(
        Guid recipientAccountId,
        string title,
        string body,
        IReadOnlyDictionary<string, string>? data = null,
        CancellationToken cancellationToken = default);
}

