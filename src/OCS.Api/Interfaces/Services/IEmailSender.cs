using OCS.Api.Common;

namespace OCS.Api.Interfaces.Services;

public sealed record EmailMessage(
    string To,
    string Subject,
    string Body,
    bool IsHtml = true);

public interface IEmailSender
{
    Task<Result<bool>> SendEmailAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
