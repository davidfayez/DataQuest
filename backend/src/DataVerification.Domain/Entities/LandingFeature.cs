using DataVerification.Domain.Common;

namespace DataVerification.Domain.Entities;

/// <summary>
/// A single card in the landing page "features" section, managed from the admin panel. Its copy is
/// translated per language in <see cref="Translations"/>; the icon is a stable key the web maps to a
/// concrete glyph. English is the guaranteed fallback when a requested language has no translation.
/// </summary>
public sealed class LandingFeature : Entity
{
    /// <summary>Icon key mapped to a glyph by the web (<c>timeline</c>, <c>wallet</c>, <c>language</c>, <c>security</c>, ...).</summary>
    public required string Icon { get; set; }

    /// <summary>Ascending display order on the landing page.</summary>
    public int SortOrder { get; set; }

    /// <summary>Only published cards are returned to the public site.</summary>
    public bool IsPublished { get; set; } = true;

    /// <summary>One row per language the card has been translated into.</summary>
    public ICollection<LandingFeatureTranslation> Translations { get; set; } = [];

    public string ResolveTitle(string? languageCode) => Pick(languageCode)?.Title ?? string.Empty;

    public string ResolveBody(string? languageCode) => Pick(languageCode)?.Body ?? string.Empty;

    /// <summary>Requested language, then English, then whatever exists — so the card never renders blank.</summary>
    private LandingFeatureTranslation? Pick(string? languageCode)
    {
        if (Translations.Count == 0)
        {
            return null;
        }

        LandingFeatureTranslation? match = null;

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

/// <summary>The title and body of one <see cref="LandingFeature"/> in a single language.</summary>
public sealed class LandingFeatureTranslation : Entity
{
    public Guid LandingFeatureId { get; set; }

    public LandingFeature? LandingFeature { get; set; }

    /// <summary>Two-letter language code, e.g. <c>en</c>, <c>ar</c>.</summary>
    public required string LanguageCode { get; set; }

    public required string Title { get; set; }

    public required string Body { get; set; }
}

/// <summary>The languages landing content can be authored in, mirroring the web app's locales.</summary>
public static class LandingLanguages
{
    public const string Default = "en";

    public static readonly IReadOnlyList<string> All = PlatformLanguages.All;

    public static bool IsSupported(string? code) => PlatformLanguages.IsSupported(code);
}
