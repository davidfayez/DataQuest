using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace DataVerification.Application.Features.Payments;

/// <summary>
/// Tells a payment method's configured mailboxes that money moved through it — a deposit claimed,
/// approved or rejected.
///
/// One place rather than repeated at each call site, because the rules are the same wherever the
/// event comes from: only the recipients who asked for that event, never twice to one address, and
/// never in a way that can fail the operation that triggered it.
/// </summary>
public sealed class PaymentNotifier
{
    /// <summary>
    /// These go to staff mailboxes, not applicants, so the ten applicant locales do not apply.
    /// English is the platform's lingua franca for internal data; the renderer also has Arabic if
    /// a per-recipient language is ever added.
    /// </summary>
    private const string OperatorLanguage = "en";

    private readonly IEmailSender _emailSender;
    private readonly IEmailTemplateRenderer _renderer;
    private readonly ILogger<PaymentNotifier> _logger;

    public PaymentNotifier(
        IEmailSender emailSender,
        IEmailTemplateRenderer renderer,
        ILogger<PaymentNotifier> logger)
    {
        _emailSender = emailSender;
        _renderer = renderer;
        _logger = logger;
    }

    /// <summary>
    /// Sends one notification per interested recipient. Never throws: the money has already moved
    /// by the time this runs, so a mail failure must not roll it back or surface as an API error.
    /// </summary>
    /// <param name="method">Must have its <c>NotificationEmails</c> loaded, or nobody is told.</param>
    public async Task NotifyAsync(
        PaymentMethod method,
        WalletRequestStatus status,
        string orderNumber,
        decimal amount,
        string currencyCode,
        string? referenceNumber,
        string? decidedBy,
        string? reviewerNote,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(method);

        var recipients = method.RecipientsFor(status);
        if (recipients.Count == 0)
        {
            return;
        }

        foreach (var recipient in recipients)
        {
            try
            {
                var message = _renderer.RenderPaymentNotification(
                    recipient,
                    status,
                    orderNumber,
                    method.ResolveName(OperatorLanguage),
                    amount,
                    currencyCode,
                    referenceNumber,
                    decidedBy,
                    reviewerNote,
                    OperatorLanguage);

                await _emailSender.SendAsync(message, cancellationToken);
            }
            catch (Exception ex)
            {
                // One bad address must not stop the rest of the list being told.
                _logger.LogWarning(
                    ex,
                    "Failed to send the {Status} payment notification for method {Method}.",
                    status,
                    method.Id);
            }
        }
    }
}
