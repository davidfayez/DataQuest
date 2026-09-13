using System.Globalization;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Infrastructure.Services;

/// <summary>
/// Keeps the password-reset window in <c>SiteSettings</c>, so an operator can change it from the
/// admin panel without a redeploy.
/// </summary>
public sealed class PasswordResetSettingsStore : IPasswordResetSettingsStore
{
    public const string ValidityMinutesSettingKey = "security.passwordReset.validityMinutes";

    /// <summary>An hour, which is what the window was before it was configurable.</summary>
    public const int DefaultValidityMinutes = 60;

    /// <summary>
    /// Bounds an operator cannot usefully cross. Under a minute the email cannot arrive in time;
    /// a week is long enough that an unused password stops being a reset and becomes a second
    /// standing credential.
    /// </summary>
    public const int MinValidityMinutes = 1;
    public const int MaxValidityMinutes = 10_080;

    private readonly IApplicationDbContext _db;

    public PasswordResetSettingsStore(IApplicationDbContext db) => _db = db;

    public async Task<int> GetValidityMinutesAsync(CancellationToken cancellationToken = default)
    {
        var stored = await _db.SiteSettings
            .AsNoTracking()
            .Where(s => s.Key == ValidityMinutesSettingKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);

        // A row that cannot be read is treated as absent: a bad settings value must not stop
        // people resetting their passwords. Parsed invariantly to match how it was written — under
        // an Arabic culture the current-culture pair would write Arabic-Indic digits and then fail
        // to read them back, silently resetting the window to the default.
        return int.TryParse(stored, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
            && minutes is >= MinValidityMinutes and <= MaxValidityMinutes
            ? minutes
            : DefaultValidityMinutes;
    }

    public async Task SetValidityMinutesAsync(int minutes, CancellationToken cancellationToken = default)
    {
        var value = Math.Clamp(minutes, MinValidityMinutes, MaxValidityMinutes)
            .ToString(CultureInfo.InvariantCulture);

        var row = await _db.SiteSettings
            .FirstOrDefaultAsync(s => s.Key == ValidityMinutesSettingKey, cancellationToken);

        if (row is null)
        {
            _db.SiteSettings.Add(new SiteSetting { Key = ValidityMinutesSettingKey, Value = value });
        }
        else
        {
            row.Value = value;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
