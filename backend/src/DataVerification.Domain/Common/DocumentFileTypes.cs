namespace DataVerification.Domain.Common;

/// <summary>
/// The upload formats the platform understands, and the one place that maps a format to the
/// extensions and content types that stand for it.
/// </summary>
/// <remarks>
/// A required document names the formats it accepts by these codes, so an administrator asking for
/// a scanned certificate can accept PDF and images while a form to fill in accepts Word.
///
/// Only formats whose bytes can actually be identified are here. The platform refuses an upload
/// whose signature does not match its extension, so a format that cannot be told apart from any
/// other file would be an extension check wearing a disguise — see <c>FileTypeValidator</c>.
/// </remarks>
public static class DocumentFileTypes
{
    public const string Pdf = "pdf";
    public const string Jpg = "jpg";
    public const string Png = "png";
    public const string Word = "word";
    public const string Excel = "excel";

    /// <summary>Every code, in the order an administrator sees them.</summary>
    public static readonly IReadOnlyList<string> All = [Pdf, Jpg, Png, Word, Excel];

    /// <summary>
    /// What a document accepts when an administrator has not narrowed it — the formats the
    /// platform allowed before this was configurable, so an existing document keeps behaving the
    /// same way.
    /// </summary>
    public static readonly IReadOnlyList<string> Default = [Pdf, Jpg, Png];

    private static readonly Dictionary<string, string[]> ExtensionsByCode =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [Pdf] = [".pdf"],
            [Jpg] = [".jpg", ".jpeg"],
            [Png] = [".png"],
            // Modern Office only. The 1990s .doc and .xls formats share one signature with every
            // other OLE compound file, so accepting them would mean trusting the extension — and
            // they are the formats that carry macros.
            [Word] = [".docx"],
            [Excel] = [".xlsx"],
        };

    public static bool IsSupported(string? code) =>
        code is not null && ExtensionsByCode.ContainsKey(code.Trim());

    /// <summary>The file extensions a code stands for, lower-cased and dot-prefixed.</summary>
    public static IReadOnlyList<string> ExtensionsFor(string code) =>
        ExtensionsByCode.TryGetValue(code.Trim(), out var extensions) ? extensions : [];

    /// <summary>Every extension the given codes allow between them.</summary>
    public static IReadOnlyList<string> ExtensionsForAll(IEnumerable<string>? codes) =>
        Normalize(codes).SelectMany(ExtensionsFor).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The code an extension belongs to, or null when no format claims it.</summary>
    public static string? CodeForExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return null;

        var normalized = extension.Trim().ToLowerInvariant();

        return All.FirstOrDefault(code =>
            ExtensionsByCode[code].Contains(normalized, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Lower-cases and de-duplicates a submitted set, preserving the platform's own order and
    /// dropping anything unrecognised.
    /// </summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string>? codes)
    {
        if (codes is null) return [];

        var wanted = codes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return All.Where(wanted.Contains).ToList();
    }

    /// <summary>
    /// The set to enforce for a document: what it was configured with, or the platform default
    /// when it was configured with nothing.
    /// </summary>
    public static IReadOnlyList<string> Resolve(IEnumerable<string>? configured)
    {
        var normalized = Normalize(configured);
        return normalized.Count > 0 ? normalized : Default;
    }
}
