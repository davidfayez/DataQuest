using DataVerification.Domain.Common;
using DataVerification.Domain.Enums;

namespace DataVerification.Domain.Entities;

/// <summary>
/// One row in a footer column, managed from the admin panel.
///
/// The three columns share a table because they are the same shape — a label and somewhere to go —
/// and differ only by which heading they sit under.
/// </summary>
public sealed class FooterLink : Entity
{
    public FooterColumn Column { get; set; }

    /// <summary>
    /// Who the row is shown to. Only meaningful in the Account column today, but stored on every
    /// row so a marketing link could be hidden from signed-in visitors without a new concept.
    /// </summary>
    public FooterLinkVisibility Visibility { get; set; }

    /// <summary>
    /// Where the row goes, held exactly as typed. The site decides how to render it:
    /// <c>https://…</c> opens in a new tab, <c>#anchor</c> becomes a link to that anchor on the
    /// home page, and anything else is treated as an in-app path and gets the language prefix.
    /// Storing the finished URL instead would bake today's language into the row.
    /// </summary>
    public required string Url { get; set; }

    /// <summary>Ascending display order within its column.</summary>
    public int SortOrder { get; set; }

    /// <summary>Only active rows reach the public site.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>One row per language the label has been written in.</summary>
    public ICollection<FooterLinkTranslation> Translations { get; set; } = [];

    public string ResolveLabel(string? languageCode) => Pick(languageCode)?.Label ?? string.Empty;

    /// <summary>Requested language, then English, then whatever exists — so a row never renders blank.</summary>
    private FooterLinkTranslation? Pick(string? languageCode)
    {
        if (Translations.Count == 0)
        {
            return null;
        }

        FooterLinkTranslation? match = null;

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

/// <summary>The label of one <see cref="FooterLink"/> in a single language.</summary>
public sealed class FooterLinkTranslation : Entity
{
    public Guid FooterLinkId { get; set; }

    public FooterLink? FooterLink { get; set; }

    /// <summary>Two-letter language code, e.g. <c>en</c>, <c>ar</c>.</summary>
    public required string LanguageCode { get; set; }

    public required string Label { get; set; }
}
