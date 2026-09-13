using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DataVerification.Infrastructure.Email;

/// <summary>
/// Applies the administrator's per-kind sender and blind-copy list to a rendered email.
/// </summary>
/// <remarks>
/// Deliberately forgiving: an unconfigured kind, a blank sender, or a lookup that fails all leave
/// the message as it was. Getting the sender wrong should mean the email arrives from the platform
/// default, never that it fails to arrive.
/// </remarks>
public sealed class EmailRouting : IEmailRouting
{
    private readonly IApplicationDbContext _db;
    private readonly IEmailSettingsStore _settings;
    private readonly ILogger<EmailRouting> _logger;

    public EmailRouting(
        IApplicationDbContext db,
        IEmailSettingsStore settings,
        ILogger<EmailRouting> logger)
    {
        _db = db;
        _settings = settings;
        _logger = logger;
    }

    public async Task<EmailMessage> ApplyAsync(
        EmailType type,
        EmailMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            var setting = await _db.EmailTypeSettings
                .AsNoTracking()
                .Include(s => s.BccRecipients)
                .FirstOrDefaultAsync(s => s.Type == type, cancellationToken);

            // Two levels of fallback, applied outermost first: the kind's own override, then the
            // platform default an operator set on the Emails configurations page, then — left to
            // the sender, by leaving the address null — whatever the transport is configured with.
            var fallback = await _settings.GetDefaultSenderAsync(cancellationToken);

            var fromAddress = Coalesce(setting?.FromAddress, message.FromAddress, fallback.FromAddress);
            var fromName = Coalesce(setting?.FromName, message.FromName, fallback.FromName);

            var bcc = setting?.BccAddresses() ?? [];

            return message with
            {
                FromAddress = fromAddress,
                FromName = fromName,
                Bcc = bcc.Count > 0 ? bcc : message.Bcc,
            };
        }
        catch (Exception ex)
        {
            // The email is more important than the routing on it.
            _logger.LogWarning(
                ex, "Could not read the email routing for {EmailType}; sending with defaults.", type);
            return message;
        }
    }

    /// <summary>The first value that is actually set. A blank override is the same as none.</summary>
    private static string? Coalesce(params string?[] candidates) =>
        Array.Find(candidates, value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
