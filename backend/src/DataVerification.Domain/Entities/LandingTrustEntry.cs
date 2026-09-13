using DataVerification.Domain.Common;

namespace DataVerification.Domain.Entities;

/// <summary>
/// One body named in the landing page's "Trusted for verification with" strip.
///
/// Deliberately separate from <see cref="VerificationAuthority"/>, which is the operational lookup
/// the application wizard offers. This strip is marketing copy: it names bodies the platform works
/// with, and some of those are not authorities an applicant can pick a service from. Flagging the
/// real lookup instead would mean creating an authority record — with a country and sub-type
/// mappings — for every name someone wanted on the marketing page, and each one would then turn up
/// in the wizard's cascade.
/// </summary>
public sealed class LandingTrustEntry : Entity
{
    /// <summary>
    /// Icon key mapped to a glyph by the web, sharing the landing vocabulary. Optional: the strip
    /// falls back to a neutral building mark, which is what it drew for every entry before these
    /// were editable.
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>Ascending display order in the strip.</summary>
    public int SortOrder { get; set; }

    /// <summary>Only published entries reach the public site.</summary>
    public bool IsPublished { get; set; } = true;

    /// <summary>One row per language the name has been written in.</summary>
    public ICollection<LandingTrustEntryTranslation> Translations { get; set; } = [];

    public string ResolveName(string? languageCode) => Pick(languageCode)?.Name ?? string.Empty;

    /// <summary>Requested language, then English, then whatever exists — so a name never renders blank.</summary>
    private LandingTrustEntryTranslation? Pick(string? languageCode)
    {
        if (Translations.Count == 0)
        {
            return null;
        }

        LandingTrustEntryTranslation? match = null;

        if (!string.IsNullOrWhiteSpace(languageCode))
        {
            match = Translations.FirstOrDefault(
                t => string.Equals(t.LanguageCode, languageCode, StringComparison.OrdinalIgnoreCase));
        }

        match ??= Translations.FirstOrDefault(
            t => string.Equals(t.LanguageCode, LandingLanguages.Default, StringComparison.OrdinalIgnoreCase));

        return match ?? Translations.OrderBy(t => t.LanguageCode).First();
    }
}

/// <summary>The name of one <see cref="LandingTrustEntry"/> in a single language.</summary>
public sealed class LandingTrustEntryTranslation : Entity
{
    public Guid LandingTrustEntryId { get; set; }

    public LandingTrustEntry? LandingTrustEntry { get; set; }

    /// <summary>Two-letter language code, e.g. <c>en</c>, <c>ar</c>.</summary>
    public required string LanguageCode { get; set; }

    /// <summary>The body's name as it should read, e.g. <c>Ministry of Higher Education</c>.</summary>
    public required string Name { get; set; }
}
