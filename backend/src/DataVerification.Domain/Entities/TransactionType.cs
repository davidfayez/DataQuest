using DataVerification.Domain.Common;

namespace DataVerification.Domain.Entities;

/// <summary>
/// نوع المعاملة — the first level of the cascade. One type may be offered in several countries.
/// </summary>
public class TransactionType : DescribedLookup, ICodedLookup
{
    /// <summary>Unique within its kind and stored upper-case — see <see cref="LookupCode"/>.</summary>
    public string Code { get; set; } = string.Empty;

    public ICollection<TransactionTypeCountry> CountryLinks { get; set; } = [];

    public ICollection<SubTransactionType> SubTransactionTypes { get; set; } = [];
}

/// <summary>Join table: countries in which a transaction type is available.</summary>
public class TransactionTypeCountry : Entity
{
    public Guid TransactionTypeId { get; set; }

    public TransactionType? TransactionType { get; set; }

    public Guid CountryId { get; set; }

    public Country? Country { get; set; }
}

/// <summary>نوع المعاملة الفرعية — second level, always owned by one transaction type.</summary>
public class SubTransactionType : DescribedLookup, ICodedLookup
{
    /// <summary>Unique within its kind and stored upper-case — see <see cref="LookupCode"/>.</summary>
    public string Code { get; set; } = string.Empty;

    public Guid TransactionTypeId { get; set; }

    public TransactionType? TransactionType { get; set; }

    /// <summary>
    /// Countries this sub-type is available in. Always a subset of the parent transaction type's
    /// countries — a sub-type cannot reach somewhere its parent does not.
    /// </summary>
    public ICollection<SubTransactionTypeCountry> CountryLinks { get; set; } = [];

    public ICollection<AuthoritySubTransactionType> AuthorityLinks { get; set; } = [];
}

/// <summary>Join table: countries in which a sub-transaction type is available.</summary>
public class SubTransactionTypeCountry : Entity
{
    public Guid SubTransactionTypeId { get; set; }

    public SubTransactionType? SubTransactionType { get; set; }

    public Guid CountryId { get; set; }

    public Country? Country { get; set; }
}
