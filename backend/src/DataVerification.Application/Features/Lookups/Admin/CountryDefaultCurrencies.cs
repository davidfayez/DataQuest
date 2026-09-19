using DataVerification.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Lookups.Admin;

/// <summary>
/// Keeps each country with exactly one main currency — the one an order is set up in, now that
/// the applicant no longer chooses.
///
/// A country that has currencies but no main one gets one picked for it: the currency shared by the
/// fewest countries, so Egypt lands on EGP rather than USD, then the lowest code as a tiebreak. A
/// country left with a main currency it no longer offers loses the flag and gets a new one.
/// </summary>
public static class CountryDefaultCurrencies
{
    /// <summary>Repairs the given countries, or every country when none are named.</summary>
    public static async Task EnsureAsync(
        IApplicationDbContext db,
        IReadOnlyCollection<Guid>? countryIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        var links = await db.CountryCurrencies
            .Where(cc => countryIds == null || countryIds.Contains(cc.CountryId))
            .ToListAsync(cancellationToken);

        if (links.Count == 0) return;

        // How widely each currency is shared, across every country, not just the ones being fixed.
        var currencyIds = links.Select(cc => cc.CurrencyId).Distinct().ToList();
        var sharedBy = await db.CountryCurrencies
            .Where(cc => currencyIds.Contains(cc.CurrencyId))
            .GroupBy(cc => cc.CurrencyId)
            .Select(group => new { CurrencyId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.CurrencyId, row => row.Count, cancellationToken);

        var codes = await db.Currencies
            .Where(c => currencyIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Code, cancellationToken);

        var changed = false;

        foreach (var country in links.GroupBy(cc => cc.CountryId))
        {
            var defaults = country.Where(cc => cc.IsDefault).ToList();
            if (defaults.Count == 1) continue;

            // More than one flagged (should not survive the unique index, but repaired anyway):
            // keep the one the rule would have picked.
            foreach (var extra in defaults) extra.IsDefault = false;

            var pick = country
                .OrderBy(cc => sharedBy.GetValueOrDefault(cc.CurrencyId))
                .ThenBy(cc => codes.GetValueOrDefault(cc.CurrencyId), StringComparer.Ordinal)
                .First();
            pick.IsDefault = true;
            changed = true;
        }

        if (changed)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
