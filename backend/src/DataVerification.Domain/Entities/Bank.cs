using DataVerification.Domain.Common;

namespace DataVerification.Domain.Entities;

/// <summary>
/// A bank an applicant can transfer to, scoped to the country it operates in.
///
/// Country-scoped rather than global on purpose: the register differs everywhere, and a payment
/// method offered in Egypt has no business listing a Jordanian bank. Seeded with Egypt's banks as
/// a starting point, but it is an admin-managed lookup — the catalogue is data, not code, so a
/// merger or a new licence is an edit rather than a deployment.
/// </summary>
public class Bank : LocalizedLookup
{
    public Guid CountryId { get; set; }

    public Country? Country { get; set; }

    /// <summary>
    /// SWIFT/BIC code, 8 or 11 characters. Optional: an operator may not know it, and a domestic
    /// transfer never needs it.
    /// </summary>
    public string? SwiftCode { get; set; }

    /// <summary>Ascending display order; unranked banks fall back to their name.</summary>
    public int SortOrder { get; set; }
}
