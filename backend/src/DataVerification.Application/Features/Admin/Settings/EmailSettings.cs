using DataVerification.Application.Common.Interfaces;
using FluentValidation;
using MediatR;

namespace DataVerification.Application.Features.Admin.Settings;

/// <summary>The email settings as the admin page shows them. Never carries the key itself.</summary>
public sealed record GetEmailSettingsQuery : IRequest<EmailSettingsDto>;

/// <param name="IsConfigured">True when a usable key is available from either source.</param>
/// <param name="MaskedApiKey">
/// Enough of the key to recognise which one is in use (<c>SG.…AbCd</c>), never enough to use it.
/// Null when nothing is configured.
/// </param>
/// <param name="Source">
/// <c>Database</c> — set from this page; <c>Configuration</c> — still falling back to
/// <c>SendGrid:ApiKey</c>; <c>Unreadable</c> — stored under a different encryption key;
/// <c>None</c> — email cannot be sent.
/// </param>
/// <param name="CanEdit">
/// False when the server has no encryption key, in which case the page explains why saving is
/// unavailable rather than failing on submit.
/// </param>
public sealed record EmailSettingsDto(
    bool IsConfigured,
    string? MaskedApiKey,
    string Source,
    bool CanEdit);

/// <summary>
/// Sets or clears the SendGrid API key. Clearing it (an empty value) falls back to whatever
/// configuration holds, which is how an operator undoes a mistake without a redeploy.
/// </summary>
public sealed record UpdateEmailSettingsCommand(string? ApiKey) : IRequest<EmailSettingsDto>;

public sealed class UpdateEmailSettingsCommandValidator : AbstractValidator<UpdateEmailSettingsCommand>
{
    public UpdateEmailSettingsCommandValidator()
    {
        // SendGrid keys are ~69 characters; the cap is a sanity bound, not a format rule, because
        // the provider is free to change the shape of what it issues.
        RuleFor(c => c.ApiKey)
            .MaximumLength(500)
            .When(c => !string.IsNullOrWhiteSpace(c.ApiKey));
    }
}

public sealed class EmailSettingsHandlers :
    IRequestHandler<GetEmailSettingsQuery, EmailSettingsDto>,
    IRequestHandler<UpdateEmailSettingsCommand, EmailSettingsDto>
{
    private readonly IEmailSettingsStore _store;
    private readonly ISecretProtector _protector;
    private readonly IAuditLogger _auditLogger;

    public EmailSettingsHandlers(
        IEmailSettingsStore store,
        ISecretProtector protector,
        IAuditLogger auditLogger)
    {
        _store = store;
        _protector = protector;
        _auditLogger = auditLogger;
    }

    public Task<EmailSettingsDto> Handle(
        GetEmailSettingsQuery request,
        CancellationToken cancellationToken) =>
        DescribeAsync(cancellationToken);

    public async Task<EmailSettingsDto> Handle(
        UpdateEmailSettingsCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var isClearing = string.IsNullOrWhiteSpace(request.ApiKey);
        await _store.SetSendGridApiKeyAsync(request.ApiKey, cancellationToken);

        // The action is recorded; the key never is — not even masked, since the audit trail is
        // readable by a different permission than the one that set it.
        await _auditLogger.LogAsync(
            isClearing ? "Settings.EmailApiKeyCleared" : "Settings.EmailApiKeyUpdated",
            "SiteSetting",
            null,
            new { Setting = "SendGrid.ApiKey" },
            cancellationToken);

        return await DescribeAsync(cancellationToken);
    }

    private async Task<EmailSettingsDto> DescribeAsync(CancellationToken cancellationToken)
    {
        var key = await _store.GetSendGridApiKeyAsync(cancellationToken);

        return new EmailSettingsDto(
            !string.IsNullOrWhiteSpace(key.Value),
            Mask(key.Value),
            key.Source.ToString(),
            _protector.IsEnabled);
    }

    /// <summary>
    /// Keeps the leading <c>SG.</c> and the last four characters so an operator can tell which key
    /// is loaded, and hides the rest. Short values are hidden entirely rather than half-revealed.
    /// </summary>
    private static string? Mask(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        const int VisibleSuffix = 4;
        const int VisiblePrefix = 3;

        if (value.Length <= VisiblePrefix + VisibleSuffix)
        {
            return new string('•', value.Length);
        }

        return $"{value[..VisiblePrefix]}{new string('•', 8)}{value[^VisibleSuffix..]}";
    }
}
