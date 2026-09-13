using DataVerification.Domain.Common;

namespace DataVerification.Domain.Entities;

/// <summary>
/// One step in the landing page's "how it works" section, managed from the admin panel.
///
/// The number shown on the card is not stored: it comes from the step's position in the published
/// list, so reordering renumbers them and an operator never has to keep a "3." in the copy in step
/// with where the card actually sits.
/// </summary>
public sealed class LandingStep : Entity
{
    /// <summary>Icon key mapped to a glyph by the web, sharing the landing vocabulary.</summary>
    public required string Icon { get; set; }

    /// <summary>Ascending display order, and therefore the number drawn on the card.</summary>
    public int SortOrder { get; set; }

    /// <summary>Only published steps reach the public site.</summary>
    public bool IsPublished { get; set; } = true;

    /// <summary>One row per language the step has been translated into.</summary>
    public ICollection<LandingStepTranslation> Translations { get; set; } = [];

    public string ResolveTitle(string? languageCode) => Pick(languageCode)?.Title ?? string.Empty;

    public string ResolveBody(string? languageCode) => Pick(languageCode)?.Body ?? string.Empty;

    /// <summary>Requested language, then English, then whatever exists — so a step never renders blank.</summary>
    private LandingStepTranslation? Pick(string? languageCode)
    {
        if (Translations.Count == 0)
        {
            return null;
        }

        LandingStepTranslation? match = null;

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

/// <summary>The title and body of one <see cref="LandingStep"/> in a single language.</summary>
public sealed class LandingStepTranslation : Entity
{
    public Guid LandingStepId { get; set; }

    public LandingStep? LandingStep { get; set; }

    /// <summary>Two-letter language code, e.g. <c>en</c>, <c>ar</c>.</summary>
    public required string LanguageCode { get; set; }

    public required string Title { get; set; }

    public required string Body { get; set; }
}
