using DataVerification.Domain.Common;
using DataVerification.Domain.Enums;

namespace DataVerification.Domain.Entities;

/// <summary>
/// One way to reach the organisation, shown on the public contact page.
///
/// Both kinds of entry — an authorised agent in a country, and the administration itself — live in
/// one table because they are the same shape: somewhere to reach, and the details to reach it by.
/// What differs is only which fields an operator fills in, and that is a matter of what they type
/// rather than of what the model can hold.
/// </summary>
public class ContactDirectoryEntry : Entity
{
    public ContactEntryKind Kind { get; set; }

    /// <summary>
    /// The country this entry belongs to. Optional: the administration is one office rather than a
    /// country's representative, and an agent's country is what the list is grouped by.
    /// </summary>
    public Guid? CountryId { get; set; }

    public Country? Country { get; set; }

    /// <summary>
    /// A name for the entry — the agent's company, or "Head office". Optional, because the
    /// reference an operator is copying often names only a country and a number.
    /// </summary>
    public string? TitleAr { get; set; }

    public string? TitleEn { get; set; }

    public string? AddressAr { get; set; }

    public string? AddressEn { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Ascending display order within its kind; ties fall back to the country name.</summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// True when there is at least one way to actually make contact.
    ///
    /// An entry with a country and nothing else is a heading, not a contact — it tells a visitor
    /// there is someone in their country while giving them no way to reach them, which is worse
    /// than not listing the country at all.
    /// </summary>
    public bool IsReachable =>
        !string.IsNullOrWhiteSpace(Phone)
        || !string.IsNullOrWhiteSpace(Email)
        || !string.IsNullOrWhiteSpace(AddressEn)
        || !string.IsNullOrWhiteSpace(AddressAr);

    /// <summary>Picks the title for a language, falling back to the other script when one is blank.</summary>
    public string? ResolveTitle(string? languageCode) => Localised(TitleAr, TitleEn, languageCode);

    public string? ResolveAddress(string? languageCode) =>
        Localised(AddressAr, AddressEn, languageCode);

    private static string? Localised(string? arabic, string? english, string? languageCode)
    {
        var isArabic = languageCode?.StartsWith("ar", StringComparison.OrdinalIgnoreCase) == true;
        var preferred = isArabic ? arabic : english;
        var fallback = isArabic ? english : arabic;

        return string.IsNullOrWhiteSpace(preferred)
            ? (string.IsNullOrWhiteSpace(fallback) ? null : fallback)
            : preferred;
    }
}
