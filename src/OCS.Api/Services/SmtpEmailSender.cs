using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using OCS.Api.Common;
using OCS.Api.Configuration;
using OCS.Api.Interfaces.Services;

namespace OCS.Api.Services;

public sealed partial class SmtpEmailSender(
    IOptions<EmailOptions> emailOptions,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task<Result<bool>> SendEmailAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var options = emailOptions.Value;
        if (string.IsNullOrWhiteSpace(options.SmtpHost) || string.IsNullOrWhiteSpace(options.FromAddress))
        {
            LogSmtpNotConfigured(logger);
            return Result.Failure<bool>(ApplicationError.ServiceUnavailable(
                "AUTH.DELIVERY_UNAVAILABLE",
                "Dịch vụ gửi email chưa được cấu hình đầy đủ."));
        }

        try
        {
            using var mailMessage = new MailMessage
            {
                From = new MailAddress(options.FromAddress, options.FromName),
                Subject = message.Subject,
                Body = message.Body,
                IsBodyHtml = message.IsHtml
            };
            mailMessage.To.Add(message.To);

            using var smtpClient = new SmtpClient(options.SmtpHost, options.SmtpPort)
            {
                EnableSsl = !string.Equals(options.Security, "None", StringComparison.OrdinalIgnoreCase),
                Timeout = (int)TimeSpan.FromSeconds(options.TimeoutSeconds > 0 ? options.TimeoutSeconds : 15).TotalMilliseconds
            };

            if (!string.IsNullOrWhiteSpace(options.Username) && !string.IsNullOrWhiteSpace(options.AppPassword))
            {
                smtpClient.Credentials = new NetworkCredential(options.Username, options.AppPassword);
            }

            await smtpClient.SendMailAsync(mailMessage, cancellationToken);
            LogEmailSent(logger);
            return Result.Success(true);
        }
        catch (Exception ex)
        {
            // Never log secret passwords or credentials
            LogSmtpSendFailed(logger, ex, options.SmtpHost, options.SmtpPort);
            return Result.Failure<bool>(ApplicationError.ServiceUnavailable(
                "AUTH.DELIVERY_UNAVAILABLE",
                "Dịch vụ gửi email tạm thời gặp sự cố. Vui lòng thử lại sau."));
        }
    }

    [LoggerMessage(LogLevel.Error, "SMTP host or sender address is not configured.")]
    private static partial void LogSmtpNotConfigured(ILogger logger);

    [LoggerMessage(LogLevel.Information, "Email notification dispatched successfully.")]
    private static partial void LogEmailSent(ILogger logger);

    [LoggerMessage(LogLevel.Error, "Failed to send email via SMTP host {Host}:{Port}.")]
    private static partial void LogSmtpSendFailed(ILogger logger, Exception ex, string host, int port);
}
