using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Common;
using DataVerification.Domain.Entities;
using DataVerification.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DataVerification.Infrastructure.Services;

/// <summary>
/// Keeps the SendGrid API key in <c>SiteSettings</c>, encrypted, so an operator can rotate it from
/// the admin panel without a redeploy. The configured <c>SendGrid:ApiKey</c> stays as a fallback,
/// which is what keeps an environment that has never opened the settings page working unchanged.
/// </summary>
public sealed class EmailSettingsStore : IEmailSettingsStore
{
    /// <summary>The <c>SiteSettings</c> row holding the encrypted key.</summary>
    public const string SendGridApiKeySettingKey = "email.sendgrid.apiKey";

    /// <summary>The <c>SiteSettings</c> rows holding the platform-wide default sender.</summary>
    public const string DefaultFromAddressSettingKey = "email.default.fromAddress";
    public const string DefaultFromNameSettingKey = "email.default.fromName";

    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _protector;
    private readonly SendGridOptions _options;
    private readonly SmtpOptions _smtpOptions;

    public EmailSettingsStore(
        IApplicationDbContext db,
        ISecretProtector protector,
        IOptions<SendGridOptions> options,
        IOptions<SmtpOptions> smtpOptions)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(smtpOptions);

        _db = db;
        _protector = protector;
        _options = options.Value;
        _smtpOptions = smtpOptions.Value;
    }

    /// <summary>
    /// Mirrors the transport selection made at startup. Kept in step with the registration in
    /// DependencyInjection: SendGrid wins when enabled, then SMTP, otherwise mail is only logged.
    /// </summary>
    public EmailTransportDefaults GetTransportDefaults()
    {
        if (_options.Enabled)
        {
            return new EmailTransportDefaults("SendGrid", _options.FromAddress, _options.FromName);
        }

        if (_smtpOptions.Enabled)
        {
            return new EmailTransportDefaults("Smtp", _smtpOptions.FromAddress, _smtpOptions.FromName);
        }

        return new EmailTransportDefaults("Log", _options.FromAddress, _options.FromName);
    }

    /// <summary>
    /// The stored default where an operator has set one, and the transport's configured sender
    /// otherwise. Not encrypted: a from-address is on every email that leaves the platform, so it
    /// is not a secret in the way the API key is.
    /// </summary>
    public async Task<EmailDefaultSender> GetDefaultSenderAsync(
        CancellationToken cancellationToken = default)
    {
        var configured = GetTransportDefaults();

        var rows = await _db.SiteSettings
            .AsNoTracking()
            .Where(s => s.Key == DefaultFromAddressSettingKey || s.Key == DefaultFromNameSettingKey)
            .ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);

        var address = rows.GetValueOrDefault(DefaultFromAddressSettingKey);

        // The address is what decides: a name on its own has nothing to be a name for, and would
        // otherwise silently rename the configured sender.
        if (string.IsNullOrWhiteSpace(address))
        {
            return new EmailDefaultSender(
                configured.FromAddress,
                configured.FromName,
                EmailDefaultSenderSource.Configuration);
        }

        var name = rows.GetValueOrDefault(DefaultFromNameSettingKey);

        return new EmailDefaultSender(
            address.Trim(),
            string.IsNullOrWhiteSpace(name) ? configured.FromName : name.Trim(),
            EmailDefaultSenderSource.Database);
    }

    public async Task SetDefaultSenderAsync(
        string? fromAddress,
        string? fromName,
        CancellationToken cancellationToken = default)
    {
        var address = fromAddress?.Trim();
        var name = fromName?.Trim();

        // Clearing the address clears the pair: a stored name with no address is a setting that
        // cannot apply, and leaving it behind would surprise whoever set the address next.
        if (string.IsNullOrEmpty(address))
        {
            await RemoveSettingAsync(DefaultFromAddressSettingKey, cancellationToken);
            await RemoveSettingAsync(DefaultFromNameSettingKey, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        await UpsertSettingAsync(DefaultFromAddressSettingKey, address, cancellationToken);

        if (string.IsNullOrEmpty(name))
        {
            await RemoveSettingAsync(DefaultFromNameSettingKey, cancellationToken);
        }
        else
        {
            await UpsertSettingAsync(DefaultFromNameSettingKey, name, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task UpsertSettingAsync(string key, string value, CancellationToken cancellationToken)
    {
        var row = await _db.SiteSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);

        if (row is null)
        {
            _db.SiteSettings.Add(new SiteSetting { Key = key, Value = value });
        }
        else
        {
            row.Value = value;
        }
    }

    private async Task RemoveSettingAsync(string key, CancellationToken cancellationToken)
    {
        var row = await _db.SiteSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);

        if (row is not null)
        {
            _db.SiteSettings.Remove(row);
        }
    }

    public async Task<SendGridKey> GetSendGridApiKeyAsync(CancellationToken cancellationToken = default)
    {
        var stored = await _db.SiteSettings
            .AsNoTracking()
            .Where(s => s.Key == SendGridApiKeySettingKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(stored))
        {
            var plaintext = _protector.Unprotect(stored);

            // A stored-but-unreadable key is reported as such rather than falling back to the
            // configured one: silently sending with a key the operator believes they replaced is
            // worse than saying the stored value cannot be read.
            return string.IsNullOrWhiteSpace(plaintext)
                ? new SendGridKey(null, SendGridKeySource.Unreadable)
                : new SendGridKey(plaintext, SendGridKeySource.Database);
        }

        return string.IsNullOrWhiteSpace(_options.ApiKey)
            ? new SendGridKey(null, SendGridKeySource.None)
            : new SendGridKey(_options.ApiKey, SendGridKeySource.Configuration);
    }

    public async Task SetSendGridApiKeyAsync(
        string? apiKey,
        CancellationToken cancellationToken = default)
    {
        var row = await _db.SiteSettings
            .FirstOrDefaultAsync(s => s.Key == SendGridApiKeySettingKey, cancellationToken);

        var trimmed = apiKey?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            if (row is not null)
            {
                _db.SiteSettings.Remove(row);
                await _db.SaveChangesAsync(cancellationToken);
            }

            return;
        }

        if (!_protector.IsEnabled)
        {
            throw new DomainException(
                "settings.encryption_not_configured",
                "The server has no encryption key configured, so the API key cannot be stored "
                + "safely. Set Security:EncryptionKey and try again.");
        }

        var encrypted = _protector.Protect(trimmed)!;

        if (row is null)
        {
            _db.SiteSettings.Add(new SiteSetting
            {
                Key = SendGridApiKeySettingKey,
                Value = encrypted,
            });
        }
        else
        {
            row.Value = encrypted;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
