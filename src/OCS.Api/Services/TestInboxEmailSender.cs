using System.Collections.Concurrent;
using OCS.Api.Common;
using OCS.Api.Interfaces.Services;

namespace OCS.Api.Services;

/// <summary>
/// In-memory sink for local testing and integration tests.
/// Never used in Production.
/// </summary>
public sealed class TestInboxEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sentMessages = new();

    public IReadOnlyCollection<EmailMessage> SentMessages => _sentMessages.ToArray();

    public Task<Result<bool>> SendEmailAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        _sentMessages.Enqueue(message);
        return Task.FromResult(Result.Success(true));
    }

    public void Clear() => _sentMessages.Clear();
}
