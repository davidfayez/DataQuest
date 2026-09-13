using DataVerification.Domain.Common;

namespace DataVerification.Domain.Entities;

/// <summary>
/// One image in the row of marks beside the footer's brand block — accreditations, partners, the
/// organisations the platform works under.
///
/// Separate from the platform logo in Settings: that one is the site's own mark, drawn in the
/// header, the admin sidebar and the sign-in screens. These are a gallery, and there can be any
/// number of them.
/// </summary>
public sealed class FooterLogo : Entity
{
    /// <summary>Storage-relative path of the uploaded image. Never exposed; served by id.</summary>
    public string? ImageStoragePath { get; set; }

    /// <summary>Content type recorded at upload, so the download replays it without sniffing.</summary>
    public string? ImageContentType { get; set; }

    /// <summary>Original file name, shown in the editor so an admin can tell images apart.</summary>
    public string? ImageFileName { get; set; }

    /// <summary>Optional address the mark links to, e.g. the partner's own site.</summary>
    public string? Url { get; set; }

    /// <summary>Ascending display order in the row.</summary>
    public int SortOrder { get; set; }

    /// <summary>Only active marks reach the public site.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>One row per language the alternative text has been written in.</summary>
    public ICollection<FooterLogoTranslation> Translations { get; set; } = [];

    /// <summary>True once the row actually has a file behind it.</summary>
    public bool HasImage => !string.IsNullOrWhiteSpace(ImageStoragePath);

    /// <summary>
    /// True when the mark is complete enough to show. A row is created before its file is
    /// uploaded, so this is what keeps a half-finished one off the public site.
    /// </summary>
    public bool IsShowable => IsActive && HasImage;

    public string ResolveAlt(string? languageCode) => Pick(languageCode)?.Alt ?? string.Empty;

    /// <summary>Requested language, then English, then whatever exists.</summary>
    private FooterLogoTranslation? Pick(string? languageCode)
    {
        if (Translations.Count == 0)
        {
            return null;
        }

        FooterLogoTranslation? match = null;

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

/// <summary>The alternative text of one <see cref="FooterLogo"/> in a single language.</summary>
public sealed class FooterLogoTranslation : Entity
{
    public Guid FooterLogoId { get; set; }

    public FooterLogo? FooterLogo { get; set; }

    /// <summary>Two-letter language code, e.g. <c>en</c>, <c>ar</c>.</summary>
    public required string LanguageCode { get; set; }

    /// <summary>What the mark says, for a reader who cannot see it.</summary>
    public required string Alt { get; set; }
}
