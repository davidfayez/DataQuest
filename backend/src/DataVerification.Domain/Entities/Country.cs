using DataVerification.Domain.Common;

namespace DataVerification.Domain.Entities;

/// <summary>
/// A verification country. It scopes the whole cascade — transaction types, authorities and the
/// currencies an order may hold are all filtered by the country chosen during order setup.
/// </summary>
public class Country : LocalizedLookup
{
    /// <summary>ISO 3166-1 alpha-2 code, e.g. <c>EG</c>.</summary>
    public required string Code { get; set; }

    /// <summary>International calling prefix, e.g. <c>+20</c> for Egypt.</summary>
    public string PhoneCode { get; set; } = string.Empty;

    public ICollection<CountryCurrency> CountryCurrencies { get; set; } = [];

    public ICollection<TransactionTypeCountry> TransactionTypeLinks { get; set; } = [];

    public ICollection<VerificationAuthority> VerificationAuthorities { get; set; } = [];

    /// <summary>
    /// The currency an order in this country is set up in: the one marked as main, or the only one
    /// when the country has a single currency. Null when neither applies.
    /// </summary>
    public Guid? ResolveDefaultCurrencyId() =>
        CountryCurrencies.FirstOrDefault(cc => cc.IsDefault)?.CurrencyId
        ?? (CountryCurrencies.Count == 1 ? CountryCurrencies.First().CurrencyId : null);

    /// <summary>True when the currency is on this country's approved list.</summary>
    public bool SupportsCurrency(Guid currencyId) =>
        CountryCurrencies.Any(cc => cc.CurrencyId == currencyId);
}
