using DataVerification.Domain.Common;

namespace DataVerification.Domain.Entities;

/// <summary>
/// One figure in the landing page's statistics strip — "48,000+ documents verified" — managed from
/// the admin panel.
///
/// Both halves are translated rather than only the label. The figure is not always a bare number:
/// "7 days" carries a word, and a locale may want its own numerals. Keeping the pair together in
/// one translation means an operator writes what the card should read and sees exactly that, rather
/// than assembling it from a shared number and a translated suffix that only agree in English.
/// </summary>
public sealed class LandingStat : Entity
{
    /// <summary>
    /// Icon key mapped to a glyph by the web, sharing the feature cards' vocabulary.
    ///
    /// Optional: a strip of bare numbers is a legitimate design, and forcing a glyph onto every
    /// figure would mean picking a meaningless one for anything that has no obvious symbol. Null
    /// or blank draws the figure alone.
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>Ascending display order in the strip.</summary>
    public int SortOrder { get; set; }

    /// <summary>Only published figures reach the public site.</summary>
    public bool IsPublished { get; set; } = true;

    /// <summary>One row per language the figure has been written in.</summary>
    public ICollection<LandingStatTranslation> Translations { get; set; } = [];

    public string ResolveValue(string? languageCode) => Pick(languageCode)?.Value ?? string.Empty;

    public string ResolveLabel(string? languageCode) => Pick(languageCode)?.Label ?? string.Empty;

    /// <summary>Requested language, then English, then whatever exists — so a card never renders blank.</summary>
    private LandingStatTranslation? Pick(string? languageCode)
    {
        if (Translations.Count == 0)
        {
            return null;
        }

        LandingStatTranslation? match = null;

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

/// <summary>The figure and its caption for one <see cref="LandingStat"/> in a single language.</summary>
public sealed class LandingStatTranslation : Entity
{
    public Guid LandingStatId { get; set; }

    public LandingStat? LandingStat { get; set; }

    /// <summary>Two-letter language code, e.g. <c>en</c>, <c>ar</c>.</summary>
    public required string LanguageCode { get; set; }

    /// <summary>The figure as it should read, e.g. <c>48,000+</c> or <c>7 days</c>.</summary>
    public required string Value { get; set; }

    /// <summary>What the figure counts, e.g. <c>documents verified</c>.</summary>
    public required string Label { get; set; }
}
