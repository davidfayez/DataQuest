using DataVerification.Domain.Common;

namespace DataVerification.Domain.Entities;

public class Currency : LocalizedLookup
{
    /// <summary>ISO 4217 code, e.g. <c>EGP</c>.</summary>
    public required string Code { get; set; }

    public required string Symbol { get; set; }

    public ICollection<CountryCurrency> CountryCurrencies { get; set; } = [];
}

/// <summary>Join table: the set of currencies a country exposes during order setup.</summary>
public class CountryCurrency : Entity
{
    public Guid CountryId { get; set; }

    public Country? Country { get; set; }

    public Guid CurrencyId { get; set; }

    public Currency? Currency { get; set; }

    /// <summary>
    /// The country's main currency: the one an order is set up in. Exactly one per country that
    /// has currencies at all.
    /// </summary>
    public bool IsDefault { get; set; }
}
