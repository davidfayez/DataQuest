using System.Globalization;
using System.Net;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Enums;
using Microsoft.Extensions.Options;

namespace DataVerification.Infrastructure.Email;

/// <summary>
/// Renders the transactional emails in each of the seven supported locales. Arabic and Urdu bodies
/// are marked <c>dir="rtl"</c> so they render correctly in mail clients.
/// </summary>
public sealed class EmailTemplateRenderer : IEmailTemplateRenderer
{
    private readonly AppOptions _app;

    public EmailTemplateRenderer(IOptions<AppOptions> appOptions)
    {
        ArgumentNullException.ThrowIfNull(appOptions);
        _app = appOptions.Value;
    }

    public EmailMessage RenderOrderCredentials(
        string to,
        string orderNumber,
        string password,
        string languageCode)
    {
        var content = CredentialsContent.For(languageCode);
        var loginUrl = BuildLoginUrl(languageCode, orderNumber);

        var html = BuildHtml(content, orderNumber, password, loginUrl);
        var text = BuildPlainText(content, orderNumber, password, loginUrl);

        // Appended rather than woven into each locale's wording: the number is the one identifier
        // an applicant always has, so it stays searchable in every language without ten edits.
        return new EmailMessage(to, $"{content.Subject} - {orderNumber}", html, text);
    }

    public EmailMessage RenderOrderPasswordReset(
        string to,
        string orderNumber,
        string password,
        string languageCode,
        int validityMinutes)
    {
        var content = OrderResetContent.For(languageCode);
        var validityNote = string.Format(
            CultureInfo.InvariantCulture, content.ValidityNote, validityMinutes);
        var direction = content.IsRtl ? "rtl" : "ltr";
        var align = content.IsRtl ? "right" : "left";
        var loginUrl = BuildLoginUrl(languageCode, orderNumber);

        // The order number is in the subject so a mailbox full of platform mail can be searched by
        // it, which is the one identifier an applicant always has to hand.
        var subject = $"{content.SubjectPrefix} - {orderNumber}";

        var html = $"""
            <!doctype html>
            <html lang="{WebUtility.HtmlEncode(content.LanguageCode)}" dir="{direction}">
              <head><meta charset="utf-8" /><title>{WebUtility.HtmlEncode(subject)}</title></head>
              <body style="margin:0;padding:24px;background:#f5f6f8;font-family:Segoe UI,Tahoma,Arial,sans-serif;color:#1f2430;">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;margin:0 auto;background:#ffffff;border-radius:12px;padding:32px;text-align:{align};">
                  <tr><td>
                    <h1 style="margin:0 0 16px;font-size:20px;">{WebUtility.HtmlEncode(content.Heading)}</h1>
                    <p style="margin:0 0 24px;line-height:1.7;color:#4a5160;">{WebUtility.HtmlEncode(content.Intro)}</p>

                    <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="background:#f5f6f8;border-radius:8px;padding:16px;margin-bottom:8px;">
                      <tr>
                        <td style="padding:0 0 6px;color:#6b7280;font-size:13px;">{WebUtility.HtmlEncode(content.OrderLabel)}</td>
                      </tr>
                      <tr>
                        <td style="font-family:Consolas,Menlo,monospace;font-size:18px;font-weight:600;color:#1b3a5b;letter-spacing:0.5px;" dir="ltr">{WebUtility.HtmlEncode(orderNumber)}</td>
                      </tr>
                    </table>

                    <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="background:#eef6f1;border:1px solid #cde5d8;border-radius:8px;padding:16px;margin-bottom:24px;">
                      <tr>
                        <td style="padding:0 0 6px;color:#4a7c62;font-size:13px;">{WebUtility.HtmlEncode(content.PasswordLabel)}</td>
                      </tr>
                      <tr>
                        <td style="font-family:Consolas,Menlo,monospace;font-size:22px;font-weight:700;color:#14532d;letter-spacing:1px;" dir="ltr">{WebUtility.HtmlEncode(password)}</td>
                      </tr>
                    </table>

                    <p style="margin:0 0 24px;">
                      <a href="{WebUtility.HtmlEncode(loginUrl)}" style="display:inline-block;background:#1b3a5b;color:#ffffff;text-decoration:none;padding:12px 24px;border-radius:8px;font-weight:600;">{WebUtility.HtmlEncode(content.ButtonText)}</a>
                    </p>

                    <p style="margin:0 0 8px;color:#6b7280;font-size:13px;line-height:1.7;">{WebUtility.HtmlEncode(validityNote)}</p>
                    <p style="margin:0;color:#6b7280;font-size:13px;line-height:1.7;">{WebUtility.HtmlEncode(content.SecurityNote)}</p>
                  </td></tr>
                </table>
              </body>
            </html>
            """;

        var text = string.Join(
            Environment.NewLine,
            content.Heading,
            string.Empty,
            content.Intro,
            string.Empty,
            $"{content.OrderLabel}: {orderNumber}",
            $"{content.PasswordLabel}: {password}",
            string.Empty,
            $"{content.ButtonText}: {loginUrl}",
            string.Empty,
            validityNote,
            content.SecurityNote);

        return new EmailMessage(to, subject, html, text);
    }

    public EmailMessage RenderAdminPasswordReset(
        string to,
        string fullName,
        string resetUrl,
        string languageCode)
    {
        var content = ResetContent.For(languageCode);
        var direction = content.IsRtl ? "rtl" : "ltr";
        var align = content.IsRtl ? "right" : "left";
        var greeting = string.Format(
            CultureInfo.InvariantCulture, content.Greeting, WebUtility.HtmlEncode(fullName));

        var html = $"""
            <!doctype html>
            <html lang="{WebUtility.HtmlEncode(content.LanguageCode)}" dir="{direction}">
              <head><meta charset="utf-8" /><title>{WebUtility.HtmlEncode(content.Subject)}</title></head>
              <body style="margin:0;padding:24px;background:#f5f6f8;font-family:Segoe UI,Tahoma,Arial,sans-serif;color:#1f2430;">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;margin:0 auto;background:#ffffff;border-radius:12px;padding:32px;text-align:{align};">
                  <tr><td>
                    <h1 style="margin:0 0 16px;font-size:20px;">{WebUtility.HtmlEncode(content.Heading)}</h1>
                    <p style="margin:0 0 8px;line-height:1.7;color:#4a5160;">{greeting}</p>
                    <p style="margin:0 0 24px;line-height:1.7;color:#4a5160;">{WebUtility.HtmlEncode(content.Intro)}</p>
                    <p style="margin:0 0 24px;">
                      <a href="{WebUtility.HtmlEncode(resetUrl)}" style="display:inline-block;background:#1b3a5b;color:#ffffff;text-decoration:none;padding:12px 24px;border-radius:8px;font-weight:600;">{WebUtility.HtmlEncode(content.ButtonText)}</a>
                    </p>
                    <p style="margin:0;color:#6b7280;font-size:13px;line-height:1.7;">{WebUtility.HtmlEncode(content.SecurityNote)}</p>
                  </td></tr>
                </table>
              </body>
            </html>
            """;

        var text = string.Join(
            Environment.NewLine,
            content.Heading,
            string.Empty,
            string.Format(CultureInfo.InvariantCulture, content.Greeting, fullName),
            content.Intro,
            string.Empty,
            $"{content.ButtonText}: {resetUrl}",
            string.Empty,
            content.SecurityNote);

        return new EmailMessage(to, content.Subject, html, text);
    }

    public EmailMessage RenderPaymentNotification(
        string to,
        WalletRequestStatus status,
        string orderNumber,
        string methodName,
        decimal amount,
        string currencyCode,
        string? referenceNumber,
        string? decidedBy,
        string? reviewerNote,
        string languageCode)
    {
        var content = PaymentNotificationContent.For(languageCode);
        var direction = content.IsRtl ? "rtl" : "ltr";
        var align = content.IsRtl ? "right" : "left";
        var queueUrl = $"{_app.AdminUrl.TrimEnd('/')}/wallet-requests";

        var headline = content.Headline(status);
        var money = string.Format(
            CultureInfo.InvariantCulture, "{0:N2} {1}", amount, currencyCode);

        var rows = new List<(string Label, string Value)>
        {
            (content.OrderLabel, orderNumber),
            (content.MethodLabel, methodName),
            (content.AmountLabel, money),
        };

        if (!string.IsNullOrWhiteSpace(referenceNumber))
        {
            rows.Add((content.ReferenceLabel, referenceNumber));
        }

        if (!string.IsNullOrWhiteSpace(decidedBy))
        {
            rows.Add((content.DecidedByLabel, decidedBy));
        }

        if (!string.IsNullOrWhiteSpace(reviewerNote))
        {
            rows.Add((content.NoteLabel, reviewerNote));
        }

        var rowsHtml = string.Concat(rows.Select(row => $"""
            <tr>
              <td style="padding:6px 0;color:#6b7280;font-size:13px;white-space:nowrap;">{WebUtility.HtmlEncode(row.Label)}</td>
              <td style="padding:6px 0 6px 16px;color:#1f2430;font-weight:600;">{WebUtility.HtmlEncode(row.Value)}</td>
            </tr>
            """));

        var html = $"""
            <!doctype html>
            <html lang="{WebUtility.HtmlEncode(content.LanguageCode)}" dir="{direction}">
              <head><meta charset="utf-8" /><title>{WebUtility.HtmlEncode(headline)}</title></head>
              <body style="margin:0;padding:24px;background:#f5f6f8;font-family:Segoe UI,Tahoma,Arial,sans-serif;color:#1f2430;">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;margin:0 auto;background:#ffffff;border-radius:12px;padding:32px;text-align:{align};">
                  <tr><td>
                    <h1 style="margin:0 0 16px;font-size:20px;">{WebUtility.HtmlEncode(headline)}</h1>
                    <p style="margin:0 0 24px;line-height:1.7;color:#4a5160;">{WebUtility.HtmlEncode(content.Intro(status))}</p>
                    <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="background:#f5f6f8;border-radius:8px;padding:16px;margin-bottom:24px;">
                      {rowsHtml}
                    </table>
                    <p style="margin:0 0 24px;">
                      <a href="{WebUtility.HtmlEncode(queueUrl)}" style="display:inline-block;background:#1b3a5b;color:#ffffff;text-decoration:none;padding:12px 24px;border-radius:8px;font-weight:600;">{WebUtility.HtmlEncode(content.ButtonText)}</a>
                    </p>
                    <p style="margin:0;color:#6b7280;font-size:13px;line-height:1.7;">{WebUtility.HtmlEncode(content.Footer)}</p>
                  </td></tr>
                </table>
              </body>
            </html>
            """;

        var text = string.Join(
            Environment.NewLine,
            new[] { headline, string.Empty, content.Intro(status), string.Empty }
                .Concat(rows.Select(row => $"{row.Label}: {row.Value}"))
                .Concat(new[] { string.Empty, $"{content.ButtonText}: {queueUrl}", string.Empty, content.Footer }));

        return new EmailMessage(to, $"{content.SubjectPrefix} {headline}", html, text);
    }

    public EmailMessage RenderApplicationStatusChanged(
        string to,
        string applicationNumber,
        ApplicationStatus status,
        string? messageToApplicant,
        string languageCode)
    {
        var content = StatusChangedContent.For(languageCode);
        var direction = content.IsRtl ? "rtl" : "ltr";
        var align = content.IsRtl ? "right" : "left";
        var statusLabel = content.StatusLabel(status);
        var loginUrl = BuildAppLoginUrl(languageCode);

        var intro = string.Format(
            CultureInfo.InvariantCulture, content.Intro, WebUtility.HtmlEncode(applicationNumber));

        var hasMessage = !string.IsNullOrWhiteSpace(messageToApplicant);
        var messageHtml = hasMessage
            ? $"""
                <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="background:#f5f6f8;border-radius:8px;padding:16px;margin-bottom:24px;">
                  <tr><td style="padding:0 0 6px;color:#6b7280;font-size:13px;">{WebUtility.HtmlEncode(content.MessageLabel)}</td></tr>
                  <tr><td style="line-height:1.7;color:#1f2430;">{WebUtility.HtmlEncode(messageToApplicant).Replace("\n", "<br />", StringComparison.Ordinal)}</td></tr>
                </table>
                """
            : string.Empty;

        var html = $"""
            <!doctype html>
            <html lang="{WebUtility.HtmlEncode(content.LanguageCode)}" dir="{direction}">
              <head><meta charset="utf-8" /><title>{WebUtility.HtmlEncode(content.Subject)}</title></head>
              <body style="margin:0;padding:24px;background:#f5f6f8;font-family:Segoe UI,Tahoma,Arial,sans-serif;color:#1f2430;">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;margin:0 auto;background:#ffffff;border-radius:12px;padding:32px;text-align:{align};">
                  <tr><td>
                    <h1 style="margin:0 0 16px;font-size:20px;">{WebUtility.HtmlEncode(content.Heading)}</h1>
                    <p style="margin:0 0 16px;line-height:1.7;color:#4a5160;">{intro}</p>
                    <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="background:#f5f6f8;border-radius:8px;padding:16px;margin-bottom:24px;">
                      <tr><td style="padding:0 0 6px;color:#6b7280;font-size:13px;">{WebUtility.HtmlEncode(content.StatusLabelText)}</td></tr>
                      <tr><td style="font-size:18px;font-weight:600;color:#1b3a5b;">{WebUtility.HtmlEncode(statusLabel)}</td></tr>
                    </table>
                    {messageHtml}
                    <p style="margin:0 0 24px;">
                      <a href="{WebUtility.HtmlEncode(loginUrl)}" style="display:inline-block;background:#1b3a5b;color:#ffffff;text-decoration:none;padding:12px 24px;border-radius:8px;font-weight:600;">{WebUtility.HtmlEncode(content.ButtonText)}</a>
                    </p>
                    <p style="margin:0;color:#6b7280;font-size:13px;line-height:1.7;">{WebUtility.HtmlEncode(content.Footer)}</p>
                  </td></tr>
                </table>
              </body>
            </html>
            """;

        var textLines = new List<string>
        {
            content.Heading,
            string.Empty,
            string.Format(CultureInfo.InvariantCulture, content.Intro, applicationNumber),
            $"{content.StatusLabelText}: {statusLabel}",
        };

        if (hasMessage)
        {
            textLines.Add(string.Empty);
            textLines.Add($"{content.MessageLabel}: {messageToApplicant}");
        }

        textLines.Add(string.Empty);
        textLines.Add($"{content.ButtonText}: {loginUrl}");
        textLines.Add(string.Empty);
        textLines.Add(content.Footer);

        return new EmailMessage(
            to,
            $"{content.Subject} — {applicationNumber}",
            html,
            string.Join(Environment.NewLine, textLines));
    }

    private string BuildAppLoginUrl(string languageCode)
    {
        var language = _app.SupportedLanguages.Contains(languageCode, StringComparer.OrdinalIgnoreCase)
            ? languageCode.ToLowerInvariant()
            : "en";

        return $"{_app.WebUrl.TrimEnd('/')}/{language}/login";
    }

    private string BuildLoginUrl(string languageCode, string orderNumber)
    {
        var language = _app.SupportedLanguages.Contains(languageCode, StringComparer.OrdinalIgnoreCase)
            ? languageCode.ToLowerInvariant()
            : "en";

        return $"{_app.WebUrl.TrimEnd('/')}/{language}/login?order={Uri.EscapeDataString(orderNumber)}";
    }

    private static string BuildHtml(
        CredentialsContent content,
        string orderNumber,
        string password,
        string loginUrl)
    {
        // Everything interpolated into the markup is HTML-encoded; the order number and password
        // are generated from a fixed alphabet, but encoding them keeps the template safe by default.
        var direction = content.IsRtl ? "rtl" : "ltr";
        var align = content.IsRtl ? "right" : "left";

        return $"""
            <!doctype html>
            <html lang="{WebUtility.HtmlEncode(content.LanguageCode)}" dir="{direction}">
              <head><meta charset="utf-8" /><title>{WebUtility.HtmlEncode(content.Subject)}</title></head>
              <body style="margin:0;padding:24px;background:#f5f6f8;font-family:Segoe UI,Tahoma,Arial,sans-serif;color:#1f2430;">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;margin:0 auto;background:#ffffff;border-radius:12px;padding:32px;text-align:{align};">
                  <tr><td>
                    <h1 style="margin:0 0 16px;font-size:20px;">{WebUtility.HtmlEncode(content.Heading)}</h1>
                    <p style="margin:0 0 24px;line-height:1.7;color:#4a5160;">{WebUtility.HtmlEncode(content.Intro)}</p>
                    <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="background:#f5f6f8;border-radius:8px;padding:16px;margin-bottom:24px;">
                      <tr><td style="padding:6px 0;color:#6b7280;font-size:13px;">{WebUtility.HtmlEncode(content.OrderNumberLabel)}</td></tr>
                      <tr><td style="padding:0 0 12px;font-size:20px;font-weight:600;letter-spacing:2px;font-family:Consolas,monospace;direction:ltr;">{WebUtility.HtmlEncode(orderNumber)}</td></tr>
                      <tr><td style="padding:6px 0;color:#6b7280;font-size:13px;">{WebUtility.HtmlEncode(content.PasswordLabel)}</td></tr>
                      <tr><td style="font-size:20px;font-weight:600;letter-spacing:2px;font-family:Consolas,monospace;direction:ltr;">{WebUtility.HtmlEncode(password)}</td></tr>
                    </table>
                    <p style="margin:0 0 24px;">
                      <a href="{WebUtility.HtmlEncode(loginUrl)}" style="display:inline-block;background:#2563eb;color:#ffffff;text-decoration:none;padding:12px 24px;border-radius:8px;font-weight:600;">{WebUtility.HtmlEncode(content.ButtonText)}</a>
                    </p>
                    <p style="margin:0;color:#6b7280;font-size:13px;line-height:1.7;">{WebUtility.HtmlEncode(content.SecurityNote)}</p>
                  </td></tr>
                </table>
              </body>
            </html>
            """;
    }

    private static string BuildPlainText(
        CredentialsContent content,
        string orderNumber,
        string password,
        string loginUrl) =>
        string.Join(
            Environment.NewLine,
            content.Heading,
            string.Empty,
            content.Intro,
            string.Empty,
            $"{content.OrderNumberLabel}: {orderNumber}",
            $"{content.PasswordLabel}: {password}",
            string.Empty,
            $"{content.ButtonText}: {loginUrl}",
            string.Empty,
            content.SecurityNote);

    /// <summary>The localized strings for the credentials email, one record per supported locale.</summary>
    public EmailMessage RenderTicketCreated(
        string to,
        string ticketNumber,
        string name,
        string categoryName,
        string subject,
        string languageCode)
    {
        var content = TicketCreatedContent.For(languageCode);
        var direction = content.IsRtl ? "rtl" : "ltr";
        var align = content.IsRtl ? "right" : "left";

        // The reference goes in the subject: it is the only handle the sender has on this
        // conversation, and a mailbox is searched by subject line.
        var mailSubject = $"{content.SubjectPrefix} - {ticketNumber}";

        var html = $"""
            <!doctype html>
            <html lang="{WebUtility.HtmlEncode(content.LanguageCode)}" dir="{direction}">
              <head><meta charset="utf-8" /><title>{WebUtility.HtmlEncode(mailSubject)}</title></head>
              <body style="margin:0;padding:24px;background:#f5f6f8;font-family:Segoe UI,Tahoma,Arial,sans-serif;color:#1f2430;">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;margin:0 auto;background:#ffffff;border-radius:12px;padding:32px;text-align:{align};">
                  <tr><td>
                    <h1 style="margin:0 0 16px;font-size:20px;">{WebUtility.HtmlEncode(content.Heading)}</h1>
                    <p style="margin:0 0 24px;line-height:1.7;color:#4a5160;">{WebUtility.HtmlEncode(string.Format(CultureInfo.CurrentCulture, content.Intro, name))}</p>

                    <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="background:#f5f6f8;border-radius:8px;padding:16px;margin-bottom:24px;">
                      <tr>
                        <td style="padding:0 0 6px;color:#6b7280;font-size:13px;">{WebUtility.HtmlEncode(content.ReferenceLabel)}</td>
                      </tr>
                      <tr>
                        <td style="font-family:Consolas,Menlo,monospace;font-size:20px;font-weight:700;color:#1b3a5b;letter-spacing:0.5px;" dir="ltr">{WebUtility.HtmlEncode(ticketNumber)}</td>
                      </tr>
                    </table>

                    <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="border:1px solid #e5e7eb;border-radius:8px;padding:16px;margin-bottom:24px;">
                      <tr>
                        <td style="padding:0 0 4px;color:#6b7280;font-size:13px;">{WebUtility.HtmlEncode(content.CategoryLabel)}</td>
                      </tr>
                      <tr>
                        <td style="padding:0 0 14px;font-size:15px;">{WebUtility.HtmlEncode(categoryName)}</td>
                      </tr>
                      <tr>
                        <td style="padding:0 0 4px;color:#6b7280;font-size:13px;">{WebUtility.HtmlEncode(content.SubjectLabel)}</td>
                      </tr>
                      <tr>
                        <td style="font-size:15px;font-weight:600;">{WebUtility.HtmlEncode(subject)}</td>
                      </tr>
                    </table>

                    <p style="margin:0 0 8px;color:#6b7280;font-size:13px;line-height:1.7;">{WebUtility.HtmlEncode(content.NextSteps)}</p>
                    <p style="margin:0;color:#6b7280;font-size:13px;line-height:1.7;">{WebUtility.HtmlEncode(content.QuoteNote)}</p>
                  </td></tr>
                </table>
              </body>
            </html>
            """;

        var text = string.Join(
            Environment.NewLine,
            content.Heading,
            string.Empty,
            string.Format(CultureInfo.CurrentCulture, content.Intro, name),
            string.Empty,
            $"{content.ReferenceLabel}: {ticketNumber}",
            $"{content.CategoryLabel}: {categoryName}",
            $"{content.SubjectLabel}: {subject}",
            string.Empty,
            content.NextSteps,
            content.QuoteNote);

        return new EmailMessage(to, mailSubject, html, text);
    }

    public EmailMessage RenderTicketReply(
        string to,
        string ticketNumber,
        string name,
        string subject,
        string? body,
        IReadOnlyList<TicketReplyAttachment> attachments,
        string languageCode)
    {
        ArgumentNullException.ThrowIfNull(attachments);

        var content = TicketReplyContent.For(languageCode);
        var direction = content.IsRtl ? "rtl" : "ltr";
        var align = content.IsRtl ? "right" : "left";
        var mailSubject = $"{content.SubjectPrefix} - {ticketNumber}";

        // Support writes prose, not markup. Encoding it and then restoring only the line breaks
        // keeps their paragraphs intact without letting anything they typed become HTML.
        var writtenHtml = string.IsNullOrWhiteSpace(body)
            ? string.Empty
            : $"""
                    <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="background:#f8fafc;border:1px solid #e5e7eb;border-radius:8px;padding:16px;margin-bottom:24px;">
                      <tr><td style="font-size:15px;line-height:1.8;color:#1f2430;">{WebUtility.HtmlEncode(body).Replace("\n", "<br />", StringComparison.Ordinal)}</td></tr>
                    </table>
                """;

        var documentsHtml = attachments.Count == 0
            ? string.Empty
            : $"""
                    <p style="margin:0 0 8px;font-size:13px;color:#6b7280;">{WebUtility.HtmlEncode(content.DocumentsLabel)}</p>
                    <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="margin-bottom:24px;">
                      {string.Concat(attachments.Select(DocumentRow))}
                    </table>
                """;

        var html = $"""
            <!doctype html>
            <html lang="{WebUtility.HtmlEncode(content.LanguageCode)}" dir="{direction}">
              <head><meta charset="utf-8" /><title>{WebUtility.HtmlEncode(mailSubject)}</title></head>
              <body style="margin:0;padding:24px;background:#f5f6f8;font-family:Segoe UI,Tahoma,Arial,sans-serif;color:#1f2430;">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;margin:0 auto;background:#ffffff;border-radius:12px;padding:32px;text-align:{align};">
                  <tr><td>
                    <h1 style="margin:0 0 16px;font-size:20px;">{WebUtility.HtmlEncode(content.Heading)}</h1>
                    <p style="margin:0 0 20px;line-height:1.7;color:#4a5160;">{WebUtility.HtmlEncode(string.Format(CultureInfo.CurrentCulture, content.Intro, name))}</p>

                    <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="background:#f5f6f8;border-radius:8px;padding:12px 16px;margin-bottom:20px;">
                      <tr>
                        <td style="color:#6b7280;font-size:13px;">{WebUtility.HtmlEncode(content.ReferenceLabel)}</td>
                        <td style="text-align:{(content.IsRtl ? "left" : "right")};font-family:Consolas,Menlo,monospace;font-size:15px;font-weight:700;color:#1b3a5b;" dir="ltr">{WebUtility.HtmlEncode(ticketNumber)}</td>
                      </tr>
                      <tr>
                        <td style="color:#6b7280;font-size:13px;padding-top:6px;">{WebUtility.HtmlEncode(content.SubjectLabel)}</td>
                        <td style="text-align:{(content.IsRtl ? "left" : "right")};font-size:14px;padding-top:6px;">{WebUtility.HtmlEncode(subject)}</td>
                      </tr>
                    </table>
            {writtenHtml}
            {documentsHtml}
                    <p style="margin:0;color:#6b7280;font-size:13px;line-height:1.7;">{WebUtility.HtmlEncode(content.ReplyNote)}</p>
                  </td></tr>
                </table>
              </body>
            </html>
            """;

        var lines = new List<string>
        {
            content.Heading,
            string.Empty,
            string.Format(CultureInfo.CurrentCulture, content.Intro, name),
            string.Empty,
            $"{content.ReferenceLabel}: {ticketNumber}",
            $"{content.SubjectLabel}: {subject}",
            string.Empty,
        };

        if (!string.IsNullOrWhiteSpace(body))
        {
            lines.Add(body);
            lines.Add(string.Empty);
        }

        if (attachments.Count > 0)
        {
            lines.Add(content.DocumentsLabel);

            foreach (var attachment in attachments)
            {
                lines.Add(string.IsNullOrWhiteSpace(attachment.Description)
                    ? $"- {attachment.Title}"
                    : $"- {attachment.Title}: {attachment.Description}");
            }

            lines.Add(string.Empty);
        }

        lines.Add(content.ReplyNote);

        return new EmailMessage(to, mailSubject, html, string.Join(Environment.NewLine, lines));
    }

    /// <summary>One document's row in the reply email: its title, and the note support wrote on it.</summary>
    private static string DocumentRow(TicketReplyAttachment attachment) =>
        $"""
              <tr>
                <td style="border:1px solid #e5e7eb;border-radius:8px;padding:12px 16px;">
                  <div style="font-size:15px;font-weight:600;color:#1f2430;">{WebUtility.HtmlEncode(attachment.Title)}</div>
                  {(string.IsNullOrWhiteSpace(attachment.Description)
                      ? string.Empty
                      : $"<div style=\"font-size:13px;color:#6b7280;line-height:1.6;padding-top:4px;\">{WebUtility.HtmlEncode(attachment.Description)}</div>")}
                </td>
              </tr>
              <tr><td style="height:8px;"></td></tr>
        """;

    private sealed record CredentialsContent(
        string LanguageCode,
        bool IsRtl,
        string Subject,
        string Heading,
        string Intro,
        string OrderNumberLabel,
        string PasswordLabel,
        string ButtonText,
        string SecurityNote)
    {
        private static readonly Dictionary<string, CredentialsContent> ByLanguage =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new(
                    "en", false,
                    "Your NEN Verification credentials",
                    "Your order is ready",
                    "Use the order number and password below to sign in and submit your verification applications.",
                    "Order number",
                    "Password",
                    "Sign in to your order",
                    "Keep these details private. If you did not request this order, you can safely ignore this message."),

                ["ar"] = new(
                    "ar", true,
                    "بيانات الدخول الخاصة بك في منصة التحقق من البيانات",
                    "تم إنشاء طلبك بنجاح",
                    "استخدم رقم الطلب وكلمة المرور أدناه لتسجيل الدخول وتقديم طلبات التحقق الخاصة بك.",
                    "رقم الطلب",
                    "كلمة المرور",
                    "تسجيل الدخول إلى طلبك",
                    "يرجى الحفاظ على سرية هذه البيانات. إذا لم تكن أنت من أنشأ هذا الطلب، يمكنك تجاهل هذه الرسالة."),

                ["ru"] = new(
                    "ru", false,
                    "Ваши данные для входа в NEN Verification",
                    "Ваш заказ создан",
                    "Используйте номер заказа и пароль ниже, чтобы войти в систему и подать заявки на проверку документов.",
                    "Номер заказа",
                    "Пароль",
                    "Войти в заказ",
                    "Никому не сообщайте эти данные. Если вы не создавали этот заказ, просто проигнорируйте это письмо."),

                ["tr"] = new(
                    "tr", false,
                    "NEN Verification giriş bilgileriniz",
                    "Siparişiniz hazır",
                    "Doğrulama başvurularınızı göndermek için aşağıdaki sipariş numarası ve şifre ile giriş yapın.",
                    "Sipariş numarası",
                    "Şifre",
                    "Siparişinize giriş yapın",
                    "Bu bilgileri gizli tutun. Bu siparişi siz oluşturmadıysanız bu iletiyi yok sayabilirsiniz."),

                ["uz"] = new(
                    "uz", false,
                    "NEN Verification kirish maʼlumotlaringiz",
                    "Buyurtmangiz tayyor",
                    "Tasdiqlash arizalarini yuborish uchun quyidagi buyurtma raqami va parol bilan tizimga kiring.",
                    "Buyurtma raqami",
                    "Parol",
                    "Buyurtmaga kirish",
                    "Bu maʼlumotlarni maxfiy saqlang. Agar bu buyurtmani siz yaratmagan boʻlsangiz, xabarni eʼtiborsiz qoldiring."),

                ["de"] = new(
                    "de", false,
                    "Ihre Zugangsdaten für NEN Verification",
                    "Ihr Auftrag wurde erstellt",
                    "Melden Sie sich mit der unten stehenden Auftragsnummer und dem Passwort an, um Ihre Verifizierungsanträge einzureichen.",
                    "Auftragsnummer",
                    "Passwort",
                    "Beim Auftrag anmelden",
                    "Bitte behandeln Sie diese Daten vertraulich. Falls Sie diesen Auftrag nicht angelegt haben, können Sie diese Nachricht ignorieren."),

                ["hi"] = new(
                    "hi", false,
                    "NEN Verification के लिए आपके लॉगिन विवरण",
                    "आपका ऑर्डर तैयार है",
                    "साइन इन करने और अपने सत्यापन आवेदन जमा करने के लिए नीचे दिए गए ऑर्डर नंबर और पासवर्ड का उपयोग करें।",
                    "ऑर्डर नंबर",
                    "पासवर्ड",
                    "अपने ऑर्डर में साइन इन करें",
                    "कृपया इन विवरणों को गोपनीय रखें। यदि आपने यह ऑर्डर नहीं बनाया है, तो इस संदेश को अनदेखा कर सकते हैं।"),

                ["zh"] = new(
                    "zh", false,
                    "您的 NEN Verification 登录凭据",
                    "您的订单已创建",
                    "请使用下方的订单编号和密码登录，并提交您的认证申请。",
                    "订单编号",
                    "密码",
                    "登录您的订单",
                    "请妥善保管这些信息。如果这不是您本人创建的订单，可以忽略此邮件。"),

                ["ja"] = new(
                    "ja", false,
                    "NEN Verification のログイン情報",
                    "ご注文の準備ができました",
                    "下記の注文番号とパスワードでログインし、認証申請を提出してください。",
                    "注文番号",
                    "パスワード",
                    "注文にログイン",
                    "これらの情報は大切に保管してください。ご自身で作成した注文でない場合は、このメールを無視して問題ありません。"),

                ["pl"] = new(
                    "pl", false,
                    "Twoje dane logowania do NEN Verification",
                    "Twoje zamówienie jest gotowe",
                    "Użyj poniższego numeru zamówienia i hasła, aby się zalogować i złożyć wnioski o weryfikację.",
                    "Numer zamówienia",
                    "Hasło",
                    "Zaloguj się do zamówienia",
                    "Zachowaj te dane w tajemnicy. Jeśli to nie Ty utworzyłeś to zamówienie, możesz zignorować tę wiadomość."),
            };

        /// <summary>Falls back to English for any locale that is not configured.</summary>
        public static CredentialsContent For(string languageCode) =>
            ByLanguage.TryGetValue(languageCode ?? string.Empty, out var content)
                ? content
                : ByLanguage["en"];
    }

    /// <summary>
    /// Localized strings for the admin password-reset email. The admin panel ships in English and
    /// Arabic, so those are the two bodies; anything else falls back to English.
    /// </summary>
    private sealed record ResetContent(
        string LanguageCode,
        bool IsRtl,
        string Subject,
        string Heading,
        string Greeting,
        string Intro,
        string ButtonText,
        string SecurityNote)
    {
        private static readonly Dictionary<string, ResetContent> ByLanguage =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new(
                    "en", false,
                    "Reset your NEN Verification password",
                    "Password reset requested",
                    "Hello {0},",
                    "We received a request to reset the password for your administrator account. Use the button below to choose a new one. This link expires in one hour.",
                    "Reset my password",
                    "If you did not request this, no action is needed and your password stays the same."),

                ["ar"] = new(
                    "ar", true,
                    "إعادة تعيين كلمة المرور في منصة التحقق من البيانات",
                    "تم طلب إعادة تعيين كلمة المرور",
                    "مرحبًا {0}،",
                    "لقد تلقّينا طلبًا لإعادة تعيين كلمة المرور لحساب المسؤول الخاص بك. استخدم الزر أدناه لاختيار كلمة مرور جديدة. تنتهي صلاحية هذا الرابط خلال ساعة واحدة.",
                    "إعادة تعيين كلمة المرور",
                    "إذا لم تكن أنت من طلب ذلك، فلا حاجة لأي إجراء وستبقى كلمة المرور كما هي."),
            };

        public static ResetContent For(string languageCode) =>
            ByLanguage.TryGetValue(languageCode ?? string.Empty, out var content)
                ? content
                : ByLanguage["en"];
    }

    /// <summary>
    /// Localized strings for the application status-change notification. English and Arabic bodies
    /// are provided; any other locale falls back to English. <see cref="Intro"/> takes the
    /// application number as its single format argument.
    /// </summary>
    private sealed record StatusChangedContent(
        string LanguageCode,
        bool IsRtl,
        string Subject,
        string Heading,
        string Intro,
        string StatusLabelText,
        string MessageLabel,
        string ButtonText,
        string Footer,
        IReadOnlyDictionary<ApplicationStatus, string> StatusLabels)
    {
        public string StatusLabel(ApplicationStatus status) =>
            StatusLabels.TryGetValue(status, out var label) ? label : status.ToString();

        private static readonly Dictionary<string, StatusChangedContent> ByLanguage =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new(
                    "en", false,
                    "Update on your NEN Verification application",
                    "Your application has been updated",
                    "There is an update on your verification application {0}.",
                    "Current status",
                    "Message from the review team",
                    "Sign in to your order",
                    "Sign in to your order to see the full details. Please do not reply to this email.",
                    new Dictionary<ApplicationStatus, string>
                    {
                        [ApplicationStatus.Pending] = "Received — in the review queue",
                        [ApplicationStatus.InProgress] = "In progress",
                        [ApplicationStatus.MissedInfo] = "More information needed",
                        [ApplicationStatus.Success] = "Completed",
                        [ApplicationStatus.Failed] = "Not verified",
                        [ApplicationStatus.Refunded] = "Refunded",
                    }),

                ["ar"] = new(
                    "ar", true,
                    "تحديث بخصوص طلب التحقق الخاص بك",
                    "تم تحديث حالة طلبك",
                    "هناك تحديث بخصوص طلب التحقق الخاص بك {0}.",
                    "الحالة الحالية",
                    "رسالة من فريق المراجعة",
                    "تسجيل الدخول إلى طلبك",
                    "سجّل الدخول إلى طلبك للاطلاع على كامل التفاصيل. يُرجى عدم الرد على هذه الرسالة.",
                    new Dictionary<ApplicationStatus, string>
                    {
                        [ApplicationStatus.Pending] = "تم الاستلام — في قائمة المراجعة",
                        [ApplicationStatus.InProgress] = "قيد المعالجة",
                        [ApplicationStatus.MissedInfo] = "مطلوب معلومات إضافية",
                        [ApplicationStatus.Success] = "مكتمل",
                        [ApplicationStatus.Failed] = "لم يتم التحقق",
                        [ApplicationStatus.Refunded] = "تم الاسترداد",
                    }),
            };

        public static StatusChangedContent For(string languageCode) =>
            ByLanguage.TryGetValue(languageCode ?? string.Empty, out var content)
                ? content
                : ByLanguage["en"];
    }

    /// <summary>
    /// Copy for the operator-facing payment notifications. English and Arabic only: these go to
    /// staff mailboxes, not to applicants, so the ten applicant locales do not apply.
    /// </summary>
    private sealed record PaymentNotificationContent(
        string LanguageCode,
        bool IsRtl,
        string SubjectPrefix,
        string SubmittedHeadline,
        string ApprovedHeadline,
        string RejectedHeadline,
        string SubmittedIntro,
        string ApprovedIntro,
        string RejectedIntro,
        string OrderLabel,
        string MethodLabel,
        string AmountLabel,
        string ReferenceLabel,
        string DecidedByLabel,
        string NoteLabel,
        string ButtonText,
        string Footer)
    {
        public string Headline(WalletRequestStatus status) => status switch
        {
            WalletRequestStatus.Approved => ApprovedHeadline,
            WalletRequestStatus.Rejected => RejectedHeadline,
            _ => SubmittedHeadline,
        };

        public string Intro(WalletRequestStatus status) => status switch
        {
            WalletRequestStatus.Approved => ApprovedIntro,
            WalletRequestStatus.Rejected => RejectedIntro,
            _ => SubmittedIntro,
        };

        private static readonly Dictionary<string, PaymentNotificationContent> ByLanguage =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new(
                    "en", false,
                    "[NEN]",
                    "New top-up awaiting review",
                    "Top-up approved",
                    "Top-up rejected",
                    "An applicant says they have paid through one of your payment methods. It is waiting for a decision.",
                    "A top-up was approved and the applicant's wallet has been credited.",
                    "A top-up was rejected. No money was credited.",
                    "Order",
                    "Payment method",
                    "Amount",
                    "Reference",
                    "Decided by",
                    "Reviewer note",
                    "Open the wallet requests queue",
                    "You are receiving this because your address is on this payment method's notification list. Please do not reply to this email."),

                ["ar"] = new(
                    "ar", true,
                    "[NEN]",
                    "طلب إيداع جديد بانتظار المراجعة",
                    "تمت الموافقة على الإيداع",
                    "تم رفض الإيداع",
                    "يقول أحد المتقدمين إنه دفع عبر إحدى طرق الدفع لديك. الطلب بانتظار القرار.",
                    "تمت الموافقة على الإيداع وتم إضافة المبلغ إلى محفظة المتقدم.",
                    "تم رفض الإيداع. لم يتم إضافة أي مبلغ.",
                    "الطلب",
                    "طريقة الدفع",
                    "المبلغ",
                    "الرقم المرجعي",
                    "قررها",
                    "ملاحظة المراجع",
                    "فتح قائمة طلبات المحفظة",
                    "وصلتك هذه الرسالة لأن بريدك مدرج في قائمة إشعارات طريقة الدفع هذه. يُرجى عدم الرد على هذه الرسالة."),
            };

        public static PaymentNotificationContent For(string languageCode) =>
            ByLanguage.TryGetValue(languageCode ?? string.Empty, out var content)
                ? content
                : ByLanguage["en"];
    }

    /// <summary>
    /// Copy for the applicant's "I forgot my password" email. The subject prefix is fixed brand
    /// wording; the order number is appended by the renderer so the mail can be found by it.
    /// </summary>
    private sealed record OrderResetContent(
        string LanguageCode,
        bool IsRtl,
        string SubjectPrefix,
        string Heading,
        string Intro,
        string OrderLabel,
        string PasswordLabel,
        string ButtonText,
        /// <summary>Takes the number of minutes; the window is set in the admin panel.</summary>
        string ValidityNote,
        string SecurityNote)
    {
        private static readonly Dictionary<string, OrderResetContent> ByLanguage =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new(
                    "en", false,
                    "Data Verification - Forget password - Order number",
                    "Here is your new password",
                    "You asked for a new password for your NEN Verification order. Use the details below to sign in.",
                    "Order number",
                    "New password",
                    "Sign in to your order",
                    "This password is valid for {0} minutes and only replaces your old one once you sign in with it. Until then your previous password still works — and if you never use this one, it stays that way.",
                    "If you did not ask for this, sign in and contact support. Please do not reply to this email."),

                ["ar"] = new(
                    "ar", true,
                    "Data Verification - Forget password - Order number",
                    "كلمة المرور الجديدة الخاصة بك",
                    "لقد طلبت كلمة مرور جديدة لطلبك في NEN Verification. استخدم البيانات أدناه لتسجيل الدخول.",
                    "رقم الطلب",
                    "كلمة المرور الجديدة",
                    "تسجيل الدخول إلى طلبك",
                    "كلمة المرور هذه صالحة لمدة {0} دقيقة، ولا تحل محل كلمة مرورك القديمة إلا بعد تسجيل الدخول بها. وحتى ذلك الحين تظل كلمة مرورك السابقة صالحة — وإذا لم تستخدم هذه فستبقى كذلك.",
                    "إذا لم تطلب ذلك، سجل الدخول وتواصل مع الدعم. يُرجى عدم الرد على هذه الرسالة."),
            };

        public static OrderResetContent For(string languageCode) =>
            ByLanguage.TryGetValue(languageCode ?? string.Empty, out var content)
                ? content
                : ByLanguage["en"];
    }

    private sealed record TicketCreatedContent(
        string LanguageCode,
        bool IsRtl,
        string SubjectPrefix,
        string Heading,
        string Intro,
        string ReferenceLabel,
        string CategoryLabel,
        string SubjectLabel,
        string NextSteps,
        string QuoteNote)
    {
        private static readonly Dictionary<string, TicketCreatedContent> ByLanguage =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new(
                    "en", false,
                    "Data Verification - Support ticket",
                    "We have your message",
                    "Thank you for getting in touch, {0}. Your enquiry has reached our support team and is waiting to be picked up.",
                    "Ticket number",
                    "Category",
                    "Subject",
                    "Someone will look at it and reply to this email address.",
                    "Please quote the ticket number above if you need to follow this up."),

                ["ar"] = new(
                    "ar", true,
                    "Data Verification - Support ticket",
                    "لقد استلمنا رسالتك",
                    "شكرًا لتواصلك معنا يا {0}. وصل استفسارك إلى فريق الدعم وهو في انتظار المراجعة.",
                    "رقم التذكرة",
                    "الفئة",
                    "الموضوع",
                    "سيقوم أحد أعضاء الفريق بمراجعته والرد على هذا البريد الإلكتروني.",
                    "يرجى ذكر رقم التذكرة أعلاه عند المتابعة."),
            };

        public static TicketCreatedContent For(string languageCode) =>
            ByLanguage.TryGetValue(languageCode ?? string.Empty, out var content)
                ? content
                : ByLanguage["en"];
    }

    private sealed record TicketReplyContent(
        string LanguageCode,
        bool IsRtl,
        string SubjectPrefix,
        string Heading,
        string Intro,
        string ReferenceLabel,
        string SubjectLabel,
        string DocumentsLabel,
        string ReplyNote)
    {
        private static readonly Dictionary<string, TicketReplyContent> ByLanguage =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new(
                    "en", false,
                    "Data Verification - Support ticket",
                    "A reply to your support ticket",
                    "Hello {0}, our support team has replied to your enquiry.",
                    "Ticket number",
                    "Subject",
                    "Attached documents",
                    "You can reply to this email and it will reach the same team."),

                ["ar"] = new(
                    "ar", true,
                    "Data Verification - Support ticket",
                    "رد على تذكرة الدعم الخاصة بك",
                    "مرحبًا {0}، لقد قام فريق الدعم بالرد على استفسارك.",
                    "رقم التذكرة",
                    "الموضوع",
                    "المستندات المرفقة",
                    "يمكنك الرد على هذه الرسالة وستصل إلى الفريق نفسه."),
            };

        public static TicketReplyContent For(string languageCode) =>
            ByLanguage.TryGetValue(languageCode ?? string.Empty, out var content)
                ? content
                : ByLanguage["en"];
    }
}
