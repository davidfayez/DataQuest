using DataVerification.Domain.Common;

namespace DataVerification.Domain.Entities;

/// <summary>
/// A body an application can be addressed to — the ministry, embassy or employer that asked for
/// the verification.
/// </summary>
/// <remarks>
/// The list exists to save typing and to keep the common destinations spelled one way, not to
/// restrict what an applicant may write. <see cref="VerificationApplication.AddressedTo"/> stays
/// free text: an applicant who cannot find their addressee types it, and nothing about the
/// application depends on the entry existing here.
/// </remarks>
public class Addressee : LocalizedLookup
{
    /// <summary>
    /// Controls where the entry sits in the applicant's list, so the most-used ones can be lifted
    /// above an alphabetical ordering. Equal values fall back to the English name.
    /// </summary>
    public int SortOrder { get; set; }
}
