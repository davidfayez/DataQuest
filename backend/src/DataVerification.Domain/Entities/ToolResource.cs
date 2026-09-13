using DataVerification.Domain.Common;
using DataVerification.Domain.Enums;

namespace DataVerification.Domain.Entities;

/// <summary>
/// One entry on the public "how to use the platform" page: either a video the applicant can watch
/// or an illustration they can look at, with a name and description in each language it has been
/// translated into.
///
/// An entry is one or the other, never both — <see cref="Kind"/> decides which of
/// <see cref="VideoUrl"/> and <see cref="ImageStoragePath"/> carries the content.
/// </summary>
public sealed class ToolResource : Entity
{
    public ToolResourceKind Kind { get; set; }

    /// <summary>Set only when <see cref="Kind"/> is <see cref="ToolResourceKind.Video"/>.</summary>
    public string? VideoUrl { get; set; }

    /// <summary>
    /// Storage-relative path of the uploaded image, set only when <see cref="Kind"/> is
    /// <see cref="ToolResourceKind.Image"/>. Never exposed; the public site fetches the bytes
    /// through an endpoint keyed on the entry's id.
    /// </summary>
    public string? ImageStoragePath { get; set; }

    /// <summary>Content type recorded at upload, so the download replays it without sniffing.</summary>
    public string? ImageContentType { get; set; }

    /// <summary>Original file name, shown in the editor so an admin can tell images apart.</summary>
    public string? ImageFileName { get; set; }

    /// <summary>Ascending display order on the public page.</summary>
    public int SortOrder { get; set; }

    /// <summary>Only published entries are returned to the public site.</summary>
    public bool IsPublished { get; set; } = true;

    /// <summary>One row per language the entry has been translated into.</summary>
    public ICollection<ToolResourceTranslation> Translations { get; set; } = [];

    /// <summary>True once an image entry actually has a file behind it.</summary>
    public bool HasImage => Kind == ToolResourceKind.Image && !string.IsNullOrWhiteSpace(ImageStoragePath);

    /// <summary>
    /// True when the entry is complete enough to show: a video needs a link, an image needs a file.
    /// An image entry is created before its file is uploaded, so this is what keeps a half-finished
    /// entry off the public page even if it was marked published.
    /// </summary>
    public bool IsShowable => Kind switch
    {
        ToolResourceKind.Video => !string.IsNullOrWhiteSpace(VideoUrl),
        ToolResourceKind.Image => HasImage,
        _ => false,
    };

    public string ResolveName(string? languageCode) => Pick(languageCode)?.Name ?? string.Empty;

    public string ResolveDescription(string? languageCode) => Pick(languageCode)?.Description ?? string.Empty;

    /// <summary>Requested language, then English, then whatever exists — so an entry never renders blank.</summary>
    private ToolResourceTranslation? Pick(string? languageCode)
    {
        if (Translations.Count == 0)
        {
            return null;
        }

        ToolResourceTranslation? match = null;

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

/// <summary>The name and description of one <see cref="ToolResource"/> in a single language.</summary>
public sealed class ToolResourceTranslation : Entity
{
    public Guid ToolResourceId { get; set; }

    public ToolResource? ToolResource { get; set; }

    /// <summary>Two-letter language code, e.g. <c>en</c>, <c>ar</c>.</summary>
    public required string LanguageCode { get; set; }

    public required string Name { get; set; }

    public required string Description { get; set; }
}
