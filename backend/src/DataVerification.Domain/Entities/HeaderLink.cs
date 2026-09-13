using DataVerification.Domain.Common;
using DataVerification.Domain.Enums;

namespace DataVerification.Domain.Entities;

/// <summary>
/// One entry in the site header, managed from the admin panel.
///
/// Deliberately close to <see cref="FooterLink"/> — a label and somewhere to go, in an order an
/// operator chooses — with one difference. A row the site ships with carries a <see cref="Key"/>
/// and no label of its own, so the header keeps the translation bundled with the app in all ten
/// languages. Writing a label here overrides that, one language at a time.
/// </summary>
public sealed class HeaderLink : Entity
{
    /// <summary>
    /// Names a built-in entry ("home", "services", …), whose label and icon the app already has.
    /// Null for a row an operator added, which has nothing to fall back to and so must be labelled.
    /// </summary>
    public string? Key { get; set; }

    /// <summary>
    /// Who the row is shown to. The marketing anchors ship as <c>SignedOut</c> because they point
    /// at the landing page, which is not where a signed-in visitor is working.
    /// </summary>
    public HeaderLinkVisibility Visibility { get; set; }

    /// <summary>
    /// Where the row goes, held exactly as typed — the same rule the footer follows:
    /// <c>https://…</c> opens in a new tab, <c>#anchor</c> links to that anchor on the home page,
    /// and anything else is an in-app path that gets the language prefix at render time.
    /// </summary>
    public required string Url { get; set; }

    /// <summary>Ascending display order across the header.</summary>
    public int SortOrder { get; set; }

    /// <summary>Only active rows reach the public site.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>One row per language somebody has overridden the label in. Usually empty.</summary>
    public ICollection<HeaderLinkTranslation> Translations { get; set; } = [];

    /// <summary>
    /// The override for a language, or <c>null</c> when there is none and the app should use its
    /// own translation. Unlike the footer this never falls back to English: every built-in entry is
    /// already translated in the app, and answering a Turkish reader with English would be worse
    /// than answering with nothing.
    /// </summary>
    public string? ResolveLabel(string? languageCode)
    {
        if (Translations.Count == 0 || string.IsNullOrWhiteSpace(languageCode))
        {
            return FallbackLabel(languageCode);
        }

        var match = Translations.FirstOrDefault(
            t => string.Equals(t.LanguageCode, languageCode, StringComparison.OrdinalIgnoreCase));

        return string.IsNullOrWhiteSpace(match?.Label) ? FallbackLabel(languageCode) : match!.Label;
    }

    /// <summary>
    /// A row an operator added has no built-in wording, so it falls back to English rather than
    /// rendering as a gap in the header. A built-in row returns null and the app fills it in.
    /// </summary>
    private string? FallbackLabel(string? languageCode)
    {
        if (Key is not null) return null;

        var english = Translations.FirstOrDefault(
            t => string.Equals(t.LanguageCode, LandingLanguages.Default, StringComparison.OrdinalIgnoreCase));

        return english?.Label ?? Translations.OrderBy(t => t.LanguageCode).FirstOrDefault()?.Label;
    }
}

/// <summary>The label of one <see cref="HeaderLink"/> in a single language.</summary>
public sealed class HeaderLinkTranslation : Entity
{
    public Guid HeaderLinkId { get; set; }

    public HeaderLink? HeaderLink { get; set; }

    /// <summary>Two-letter language code, e.g. <c>en</c>, <c>ar</c>.</summary>
    public required string LanguageCode { get; set; }

    public required string Label { get; set; }
}
