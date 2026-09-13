using DataVerification.Application.Common.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace DataVerification.Infrastructure.Email;

/// <summary>
/// Delivers mail through SendGrid's Web API. Like the SMTP sender, a delivery failure is logged
/// rather than thrown: the caller's work has already committed and a lost notification can be
/// re-sent, whereas failing the request would be worse. A non-2xx response is treated as a failure.
/// </summary>
public sealed class SendGridEmailSender : IEmailSender
{
    private readonly SendGridOptions _options;
    private readonly IEmailSettingsStore _settings;
    private readonly ILogger<SendGridEmailSender> _logger;

    public SendGridEmailSender(
        IOptions<SendGridOptions> options,
        IEmailSettingsStore settings,
        ILogger<SendGridEmailSender> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _settings = settings;
        _logger = logger;
    }

    public async Task<EmailDeliveryResult> SendAsync(
        EmailMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Resolved per send rather than captured at startup, so a key set on the admin settings
        // page takes effect immediately instead of at the next restart.
        var key = await _settings.GetSendGridApiKeyAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(key.Value))
        {
            _logger.LogWarning(
                "No SendGrid API key is available ({Source}), so '{Subject}' to {Recipient} was not "
                + "delivered. Set it on the admin settings page.{NewLine}{Body}",
                key.Source,
                message.Subject,
                message.To,
                Environment.NewLine,
                message.PlainTextBody);
            return EmailDeliveryResult.NoApiKey(key.Source.ToString());
        }

        try
        {
            var client = new SendGridClient(key.Value);

            // The message's own sender wins where one was configured for its kind; the options
            // remain the fallback for everything else.
            var from = new EmailAddress(
                message.FromAddress ?? _options.FromAddress,
                message.FromName ?? _options.FromName);

            var to = new EmailAddress(message.To);
            var msg = MailHelper.CreateSingleEmail(
                from,
                to,
                message.Subject,
                message.PlainTextBody,
                message.HtmlBody);

            foreach (var bcc in EmailRecipients.BlindCopies(message))
            {
                // Never as a Bcc on the recipient's own copy — SendGrid would refuse the send.
                msg.AddBcc(new EmailAddress(bcc));
            }

            foreach (var attachment in message.Attachments ?? [])
            {
                msg.AddAttachment(
                    attachment.FileName,
                    Convert.ToBase64String(attachment.Content),
                    attachment.ContentType);
            }

            var response = await client.SendEmailAsync(msg, cancellationToken);

            if ((int)response.StatusCode is >= 200 and < 300)
            {
                _logger.LogInformation(
                    "Sent '{Subject}' to {Recipient} via SendGrid.", message.Subject, message.To);
                return EmailDeliveryResult.Sent();
            }

            var body = await response.Body.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "SendGrid rejected '{Subject}' to {Recipient}: {Status} {Body}",
                message.Subject,
                message.To,
                (int)response.StatusCode,
                body);

            // The provider's own words, carried back so an administrator sees "the from address is
            // not a verified sender" rather than silence.
            return EmailDeliveryResult.Rejected($"{(int)response.StatusCode}: {Summarise(body)}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Failed to deliver '{Subject}' to {Recipient} via SendGrid.",
                message.Subject,
                message.To);

            return EmailDeliveryResult.Failed(ex.Message);
        }
    }

    /// <summary>Keeps a provider error short enough to show, and never longer than a line or two.</summary>
    private static string Summarise(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "no detail returned";
        }

        var flattened = body.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return flattened.Length <= 300 ? flattened : flattened[..300] + "…";
    }
}

/// <summary>
/// Delivers mail over SMTP. A delivery failure is logged rather than thrown: registration has
/// already committed by the time the mail is sent, and losing the order would be far worse than
/// losing the notification, which the applicant can request again.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger;
    }

    public async Task<EmailDeliveryResult> SendAsync(
        EmailMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(
            message.FromName ?? _options.FromName,
            message.FromAddress ?? _options.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));

        foreach (var bcc in EmailRecipients.BlindCopies(message))
        {
            mime.Bcc.Add(MailboxAddress.Parse(bcc));
        }
        mime.Subject = message.Subject;

        var builder = new BodyBuilder
        {
            HtmlBody = message.HtmlBody,
            TextBody = message.PlainTextBody,
        };

        foreach (var attachment in message.Attachments ?? [])
        {
            builder.Attachments.Add(
                attachment.FileName,
                attachment.Content,
                MimeKit.ContentType.Parse(attachment.ContentType));
        }

        mime.Body = builder.ToMessageBody();

        try
        {
            using var client = new SmtpClient();

            var socketOptions = _options.UseStartTls
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.Auto;

            await client.ConnectAsync(_options.Host, _options.Port, socketOptions, cancellationToken);

            if (!string.IsNullOrWhiteSpace(_options.UserName))
            {
                await client.AuthenticateAsync(_options.UserName, _options.Password, cancellationToken);
            }

            await client.SendAsync(mime, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);

            _logger.LogInformation("Sent '{Subject}' to {Recipient}.", message.Subject, message.To);
            return EmailDeliveryResult.Sent();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Failed to deliver '{Subject}' to {Recipient}.",
                message.Subject,
                message.To);

            return EmailDeliveryResult.Failed(ex.Message);
        }
    }
}

/// <summary>
/// Development sender that writes the message to the log instead of delivering it, so the whole
/// onboarding flow can be exercised without an SMTP server.
/// </summary>
public sealed class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger) => _logger = logger;

    public Task<EmailDeliveryResult> SendAsync(
        EmailMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var bcc = EmailRecipients.BlindCopies(message);

        // From and Bcc are logged too: they are configurable per email type, and a development
        // mailbox that hides them cannot show whether that configuration took effect.
        var attachments = message.Attachments ?? [];

        _logger.LogInformation(
            "[DEV EMAIL] From: {Sender} | To: {Recipient} | Bcc: {Bcc} | Files: {Files} | "
            + "Subject: {Subject}{NewLine}{Body}",
            message.FromAddress ?? "(default)",
            message.To,
            bcc.Count == 0 ? "(none)" : string.Join(", ", bcc),
            attachments.Count == 0
                ? "(none)"
                : string.Join(", ", attachments.Select(file => file.FileName)),
            message.Subject,
            Environment.NewLine,
            message.PlainTextBody);

        // Reported as Logged, not Delivered: nothing left the machine, and a test send that said
        // "delivered" here would be telling an operator something untrue.
        return Task.FromResult(EmailDeliveryResult.Logged());
    }
}

/// <summary>
/// Shared by the senders: the blind copies worth attempting, minus the obvious mistakes. Public so
/// the hygiene rules can be pinned by tests rather than inferred from a provider rejecting a send.
/// </summary>
public static class EmailRecipients
{
    /// <summary>
    /// Drops blanks, duplicates, and the recipient's own address — copying someone on their own
    /// email delivers it twice and looks like a bug to them.
    /// </summary>
    public static IReadOnlyList<string> BlindCopies(EmailMessage message)
    {
        if (message.Bcc is not { Count: > 0 })
        {
            return [];
        }

        return message.Bcc
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Select(address => address.Trim())
            .Where(address => !string.Equals(address, message.To?.Trim(), StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
