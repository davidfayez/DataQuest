using DataVerification.Domain.Common;

namespace DataVerification.Domain.Entities;

/// <summary>
/// جهة التحقق — the body that performs the verification. An authority belongs to a country and is
/// offered only for the sub-transaction types it is explicitly mapped to.
/// </summary>
public class VerificationAuthority : DescribedLookup, ICodedLookup
{
    /// <summary>Unique within its kind and stored upper-case — see <see cref="LookupCode"/>.</summary>
    public string Code { get; set; } = string.Empty;

    public Guid CountryId { get; set; }

    public Country? Country { get; set; }

    public ICollection<AuthoritySubTransactionType> SubTransactionTypeLinks { get; set; } = [];

    public ICollection<ServiceType> ServiceTypes { get; set; } = [];

    public bool HandlesSubTransactionType(Guid subTransactionTypeId) =>
        SubTransactionTypeLinks.Any(link => link.SubTransactionTypeId == subTransactionTypeId);
}

/// <summary>Junction controlling which authorities appear for a given sub-transaction type.</summary>
public class AuthoritySubTransactionType : Entity
{
    public Guid VerificationAuthorityId { get; set; }

    public VerificationAuthority? VerificationAuthority { get; set; }

    public Guid SubTransactionTypeId { get; set; }

    public SubTransactionType? SubTransactionType { get; set; }
}
