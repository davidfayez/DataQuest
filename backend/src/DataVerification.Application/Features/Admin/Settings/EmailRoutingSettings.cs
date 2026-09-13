using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Admin.Settings;

/// <summary>
/// Who each kind of outgoing email comes from, and who is blind-copied on it.
/// </summary>
public sealed record GetEmailRoutingQuery : IRequest<IReadOnlyList<EmailRoutingDto>>;

/// <param name="IsWired">
/// False for a kind nothing sends yet, so the admin panel can say so rather than let an operator
/// configure something that will never fire.
/// </param>
/// <param name="EffectiveFromAddress">
/// The address this kind of email will actually be sent from — the override where one is set, and
/// the platform default where it is not. Shown on the page so an operator can see the fallback
/// working rather than having to infer it from an empty field.
/// </param>
/// <param name="UsesDefaultSender">True when nothing here overrides the platform default.</param>
public sealed record EmailRoutingDto(
    EmailType Type,
    string TypeName,
    string? FromAddress,
    string? FromName,
    bool IsWired,
    IReadOnlyList<EmailBccRecipientDto> Bcc,
    string EffectiveFromAddress,
    string? EffectiveFromName,
    bool UsesDefaultSender);

public sealed record EmailBccRecipientDto(Guid Id, string Email, string? DisplayName);

public sealed record EmailBccRecipientInput(Guid? Id, string Email, string? DisplayName);

/// <summary>
/// Whether the platform can send email at all, ahead of any per-type configuration.
/// </summary>
/// <param name="Transport">SendGrid, Smtp, or Log when nothing is enabled.</param>
/// <param name="CanSend">
/// False when the chosen transport cannot deliver — the usual cause being a missing SendGrid key,
/// which otherwise fails silently and looks exactly like "the feature is broken".
/// </param>
public sealed record EmailDeliveryStatusDto(
    string Transport,
    string DefaultFromAddress,
    string? DefaultFromName,
    bool CanSend,
    string? Reason);

public sealed record GetEmailDeliveryStatusQuery : IRequest<EmailDeliveryStatusDto>;

/// <summary>
/// The sender every kind of email falls back to when it sets no override of its own.
/// </summary>
/// <param name="Source">
/// <c>Database</c> when an operator set it here, <c>Configuration</c> when it is still the
/// address the server was deployed with — so the page can say which is in force rather than
/// showing a value whose origin is a guess.
/// </param>
/// <param name="ConfiguredFromAddress">
/// What the transport would fall back to if the stored default were cleared. Shown beside the
/// field so clearing it is not a leap in the dark.
/// </param>
public sealed record EmailDefaultSenderDto(
    string FromAddress,
    string? FromName,
    string Source,
    string ConfiguredFromAddress,
    string? ConfiguredFromName);

public sealed record GetEmailDefaultSenderQuery : IRequest<EmailDefaultSenderDto>;

/// <summary>
/// Sets the platform-wide default sender. A blank address clears it, restoring the configured one.
/// </summary>
public sealed record SaveEmailDefaultSenderCommand(string? FromAddress, string? FromName)
    : IRequest<EmailDefaultSenderDto>;

public sealed class SaveEmailDefaultSenderCommandValidator
    : AbstractValidator<SaveEmailDefaultSenderCommand>
{
    public SaveEmailDefaultSenderCommandValidator()
    {
        RuleFor(c => c.FromAddress)
            .MaximumLength(256)
            .EmailAddress().When(c => !string.IsNullOrWhiteSpace(c.FromAddress))
            .WithMessage("That is not a valid email address.");

        RuleFor(c => c.FromName).MaximumLength(200);

        // A display name with nothing to display it against cannot take effect, and storing it
        // would leave a setting that appears saved and does nothing.
        RuleFor(c => c.FromName)
            .Empty()
            .When(c => string.IsNullOrWhiteSpace(c.FromAddress))
            .WithMessage("Enter the default address before giving it a display name.");
    }
}

/// <summary>
/// Sends a real test email for one kind, through the same routing and transport a live one takes.
/// </summary>
/// <remarks>
/// The point is the answer, not the email: an operator asking "why did no password reset arrive"
/// gets the provider's actual refusal back — an unverified sender, a missing key — instead of a
/// success screen and silence.
/// </remarks>
public sealed record SendTestEmailCommand(EmailType Type, string To) : IRequest<TestEmailResultDto>;

public sealed record TestEmailResultDto(
    bool Delivered,
    string Status,
    string? Detail,
    string FromAddress,
    string To);

public sealed class SendTestEmailCommandValidator : AbstractValidator<SendTestEmailCommand>
{
    public SendTestEmailCommandValidator()
    {
        RuleFor(c => c.Type).IsInEnum();
        RuleFor(c => c.To)
            .NotEmpty().WithMessage("Enter an address to send the test to.")
            .MaximumLength(256)
            .EmailAddress().WithMessage("That is not a valid email address.");
    }
}

public sealed record SaveEmailRoutingCommand(
    EmailType Type,
    string? FromAddress,
    string? FromName,
    IReadOnlyList<EmailBccRecipientInput> Bcc) : IRequest<EmailRoutingDto>;

public sealed class SaveEmailRoutingCommandValidator : AbstractValidator<SaveEmailRoutingCommand>
{
    public SaveEmailRoutingCommandValidator()
    {
        RuleFor(c => c.Type).IsInEnum();
        RuleFor(c => c.FromName).MaximumLength(200);

        RuleFor(c => c.FromAddress)
            .MaximumLength(256)
            .EmailAddress().When(c => !string.IsNullOrWhiteSpace(c.FromAddress))
            .WithMessage("That is not a valid sender address.");

        RuleForEach(c => c.Bcc).ChildRules(recipient =>
        {
            recipient.RuleFor(r => r.Email)
                .NotEmpty().WithMessage("A blind copy needs an email address.")
                .MaximumLength(256)
                .EmailAddress().WithMessage("That is not a valid email address.");
            recipient.RuleFor(r => r.DisplayName).MaximumLength(200);
        });

        // A generous cap, but a cap: every one of these is copied on every single send.
        RuleFor(c => c.Bcc).Must(list => list.Count <= 20)
            .WithMessage("At most 20 blind copies per email type.");

        RuleFor(c => c.Bcc)
            .Must(list => list
                .Select(r => r.Email?.Trim())
                .Where(email => !string.IsNullOrEmpty(email))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == list.Count(r => !string.IsNullOrWhiteSpace(r.Email)))
            .WithMessage("The same address is listed more than once.");
    }
}

public sealed class EmailRoutingHandlers :
    IRequestHandler<GetEmailRoutingQuery, IReadOnlyList<EmailRoutingDto>>,
    IRequestHandler<SaveEmailRoutingCommand, EmailRoutingDto>,
    IRequestHandler<GetEmailDeliveryStatusQuery, EmailDeliveryStatusDto>,
    IRequestHandler<GetEmailDefaultSenderQuery, EmailDefaultSenderDto>,
    IRequestHandler<SaveEmailDefaultSenderCommand, EmailDefaultSenderDto>,
    IRequestHandler<SendTestEmailCommand, TestEmailResultDto>
{
    /// <summary>
    /// The kinds something actually sends today, so the admin page can flag one that is configured
    /// but wired to nothing. Every value is live now that the contact form sends both its receipt
    /// and its replies through <see cref="EmailType.ContactUs"/>.
    /// </summary>
    private static readonly HashSet<EmailType> Wired =
        [EmailType.OrderCreated, EmailType.ForgotPassword, EmailType.ContactUs];

    private readonly IApplicationDbContext _db;
    private readonly IAuditLogger _auditLogger;
    private readonly IEmailSettingsStore _settings;
    private readonly IEmailSender _emailSender;
    private readonly IEmailRouting _routing;

    public EmailRoutingHandlers(
        IApplicationDbContext db,
        IAuditLogger auditLogger,
        IEmailSettingsStore settings,
        IEmailSender emailSender,
        IEmailRouting routing)
    {
        _db = db;
        _auditLogger = auditLogger;
        _settings = settings;
        _emailSender = emailSender;
        _routing = routing;
    }

    public async Task<EmailDefaultSenderDto> Handle(
        GetEmailDefaultSenderQuery request,
        CancellationToken cancellationToken) =>
        Describe(
            await _settings.GetDefaultSenderAsync(cancellationToken),
            _settings.GetTransportDefaults());

    public async Task<EmailDefaultSenderDto> Handle(
        SaveEmailDefaultSenderCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _settings.SetDefaultSenderAsync(
            request.FromAddress,
            request.FromName,
            cancellationToken);

        // Worth an audit line: it changes the address on every email the platform sends, and the
        // question afterwards is always "who changed it, and to what".
        await _auditLogger.LogAsync(
            "EmailDefaultSender.Saved",
            nameof(SiteSetting),
            null,
            new
            {
                FromAddress = request.FromAddress?.Trim(),
                FromName = request.FromName?.Trim(),
                Cleared = string.IsNullOrWhiteSpace(request.FromAddress),
            },
            cancellationToken);

        return Describe(
            await _settings.GetDefaultSenderAsync(cancellationToken),
            _settings.GetTransportDefaults());
    }

    /// <summary>
    /// The sender an unoverridden kind actually goes out as: the operator's default where one is
    /// set, and the transport's configured address otherwise.
    /// </summary>
    /// <remarks>
    /// Every panel on the page states this fallback, so it has to be resolved the same way the
    /// sender resolves it. Reading the configured address directly is what made the per-kind
    /// panels contradict the default sender panel above them.
    /// </remarks>
    private async Task<EmailTransportDefaults> EffectiveDefaultsAsync(CancellationToken cancellationToken)
    {
        var transport = _settings.GetTransportDefaults();
        var sender = await _settings.GetDefaultSenderAsync(cancellationToken);

        return new EmailTransportDefaults(transport.Transport, sender.FromAddress, sender.FromName);
    }

    private static EmailDefaultSenderDto Describe(
        EmailDefaultSender sender,
        EmailTransportDefaults configured) =>
        new(
            sender.FromAddress,
            sender.FromName,
            sender.Source.ToString(),
            configured.FromAddress,
            configured.FromName);

    public async Task<EmailDeliveryStatusDto> Handle(
        GetEmailDeliveryStatusQuery request,
        CancellationToken cancellationToken)
    {
        // The address reported here is the one an email would actually leave with, so the banner
        // agrees with the Default sender panel instead of naming the configured address the
        // operator has just overridden.
        var defaults = await EffectiveDefaultsAsync(cancellationToken);

        // Only SendGrid can be un-sendable for a reason worth naming here; SMTP failures show up
        // when a send is attempted, and the log transport always "works".
        if (defaults.Transport == "SendGrid")
        {
            var key = await _settings.GetSendGridApiKeyAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(key.Value))
            {
                return new EmailDeliveryStatusDto(
                    defaults.Transport,
                    defaults.FromAddress,
                    defaults.FromName,
                    false,
                    $"NoApiKey:{key.Source}");
            }
        }

        return new EmailDeliveryStatusDto(
            defaults.Transport,
            defaults.FromAddress,
            defaults.FromName,
            true,
            defaults.Transport == "Log" ? "LogOnly" : null);
    }

    public async Task<TestEmailResultDto> Handle(
        SendTestEmailCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var defaults = _settings.GetTransportDefaults();
        var to = request.To.Trim();

        var message = new EmailMessage(
            to,
            $"Data Verification - test email - {request.Type}",
            TestBodyHtml(request.Type),
            TestBodyText(request.Type));

        // Routed exactly as the real one would be, or the test would prove nothing about it.
        message = await _routing.ApplyAsync(request.Type, message, cancellationToken);

        var result = await _emailSender.SendAsync(message, cancellationToken);

        await _auditLogger.LogAsync(
            "EmailRouting.TestSent",
            nameof(EmailTypeSetting),
            null,
            new { Type = request.Type.ToString(), To = to, result.Status },
            cancellationToken);

        return new TestEmailResultDto(
            result.Delivered,
            result.Status,
            result.Detail,
            message.FromAddress ?? defaults.FromAddress,
            to);
    }

    private static string TestBodyText(EmailType type) =>
        $"This is a test email from Data Verification for the '{type}' email type. "
        + "If you received it, that kind of email can reach this address.";

    private static string TestBodyHtml(EmailType type) =>
        $"""
        <!doctype html>
        <html><body style="font-family:Segoe UI,Tahoma,Arial,sans-serif;padding:24px;color:#1f2430;">
          <h1 style="font-size:18px;margin:0 0 12px;">Test email</h1>
          <p style="margin:0 0 8px;line-height:1.7;">
            This is a test from Data Verification for the <strong>{type}</strong> email type.
          </p>
          <p style="margin:0;color:#6b7280;font-size:13px;">
            If you received it, that kind of email can reach this address.
          </p>
        </body></html>
        """;

    public async Task<IReadOnlyList<EmailRoutingDto>> Handle(
        GetEmailRoutingQuery request,
        CancellationToken cancellationToken)
    {
        var stored = await _db.EmailTypeSettings
            .AsNoTracking()
            .Include(setting => setting.BccRecipients)
            .ToDictionaryAsync(setting => setting.Type, cancellationToken);

        var defaults = await EffectiveDefaultsAsync(cancellationToken);

        // Every kind is returned whether or not it has a row, so the page renders the full set and
        // an unconfigured kind is an empty form rather than a missing one.
        return Enum.GetValues<EmailType>()
            .Select(type => stored.TryGetValue(type, out var setting)
                ? ToDto(setting, defaults)
                : new EmailRoutingDto(
                    type,
                    type.ToString(),
                    null,
                    null,
                    Wired.Contains(type),
                    [],
                    defaults.FromAddress,
                    defaults.FromName,
                    true))
            .ToList();
    }

    public async Task<EmailRoutingDto> Handle(
        SaveEmailRoutingCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var setting = await _db.EmailTypeSettings
            .Include(s => s.BccRecipients)
            .FirstOrDefaultAsync(s => s.Type == request.Type, cancellationToken);

        if (setting is null)
        {
            setting = new EmailTypeSetting { Type = request.Type };
            _db.EmailTypeSettings.Add(setting);
        }

        setting.FromAddress = Trim(request.FromAddress);
        setting.FromName = Trim(request.FromName);
        setting.UpdatedAtUtc = DateTime.UtcNow;

        SyncRecipients(setting, request.Bcc);

        await _db.SaveChangesAsync(cancellationToken);

        // The sender on outgoing mail is worth a trail: it is how a platform's mail starts coming
        // from somewhere it should not.
        await _auditLogger.LogAsync(
            "EmailRouting.Saved",
            nameof(EmailTypeSetting),
            setting.Id,
            new
            {
                Type = request.Type.ToString(),
                setting.FromAddress,
                setting.FromName,
                Bcc = setting.BccAddresses().Count,
            },
            cancellationToken);

        return ToDto(setting, await EffectiveDefaultsAsync(cancellationToken));
    }

    /// <summary>Replace-set semantics: an address removed in the editor stops being copied.</summary>
    private void SyncRecipients(
        EmailTypeSetting setting,
        IReadOnlyList<EmailBccRecipientInput> submitted)
    {
        var keptIds = submitted
            .Where(recipient => recipient.Id is { } id && id != Guid.Empty)
            .Select(recipient => recipient.Id!.Value)
            .ToHashSet();

        foreach (var removed in setting.BccRecipients.Where(r => !keptIds.Contains(r.Id)).ToList())
        {
            setting.BccRecipients.Remove(removed);
            _db.EmailBccRecipients.Remove(removed);
        }

        foreach (var input in submitted.Where(r => !string.IsNullOrWhiteSpace(r.Email)))
        {
            var existing = input.Id is { } id && id != Guid.Empty
                ? setting.BccRecipients.FirstOrDefault(recipient => recipient.Id == id)
                : null;

            if (existing is null)
            {
                existing = new EmailBccRecipient
                {
                    EmailTypeSettingId = setting.Id,
                    Email = input.Email.Trim(),
                };

                setting.BccRecipients.Add(existing);
            }

            existing.Email = input.Email.Trim();
            existing.DisplayName = Trim(input.DisplayName);
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    private static EmailRoutingDto ToDto(EmailTypeSetting setting, EmailTransportDefaults defaults)
    {
        // The same fallback the sender applies, so the page shows what will really happen rather
        // than a blank that leaves an operator guessing.
        var usesDefault = string.IsNullOrWhiteSpace(setting.FromAddress);

        return new EmailRoutingDto(
            setting.Type,
            setting.Type.ToString(),
            setting.FromAddress,
            setting.FromName,
            Wired.Contains(setting.Type),
            setting.BccRecipients
                .OrderBy(recipient => recipient.Email)
                .Select(recipient => new EmailBccRecipientDto(
                    recipient.Id, recipient.Email, recipient.DisplayName))
                .ToList(),
            usesDefault ? defaults.FromAddress : setting.FromAddress!.Trim(),
            string.IsNullOrWhiteSpace(setting.FromName) ? defaults.FromName : setting.FromName.Trim(),
            usesDefault);
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
