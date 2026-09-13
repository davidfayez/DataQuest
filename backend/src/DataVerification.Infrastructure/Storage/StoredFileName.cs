using System.Globalization;
using System.Text;

namespace DataVerification.Infrastructure.Storage;

/// <summary>
/// Builds the name an upload is stored under: the original file name, the owning scope and the
/// upload time, e.g. <c>passport-scan_3f2a…_20260731T184500123.pdf</c>.
///
/// The original name comes from the client and is therefore untrusted. It is reduced to a
/// conservative character set before use, which is what stops a crafted name from introducing a
/// directory separator, a <c>..</c> traversal, an alternate data stream, or a second extension
/// that a downstream tool might honour instead of the real one.
/// </summary>
public static class StoredFileName
{
    /// <summary>Long enough to stay recognisable, short enough to keep the full path well under the OS limit.</summary>
    private const int MaxBaseLength = 60;

    private const string Fallback = "upload";

    public static string Build(
        string? originalFileName,
        string? scopeId,
        string extension,
        DateTime utcNow,
        int attempt = 0)
    {
        var baseName = Sanitize(Path.GetFileNameWithoutExtension(originalFileName ?? string.Empty), MaxBaseLength);
        if (baseName.Length == 0) baseName = Fallback;

        var scope = Sanitize(scopeId, 40);
        var timestamp = utcNow.ToString("yyyyMMdd'T'HHmmssfff", CultureInfo.InvariantCulture);
        var suffix = attempt > 0 ? $"-{attempt}" : string.Empty;

        // The extension is taken from the validated allow-list, never from the raw name.
        return scope.Length > 0
            ? $"{baseName}_{scope}_{timestamp}{suffix}{extension}"
            : $"{baseName}_{timestamp}{suffix}{extension}";
    }

    /// <summary>
    /// Keeps letters, digits, dash and underscore; everything else — separators, dots, spaces,
    /// control characters, non-ASCII — collapses to a single dash.
    /// </summary>
    private static string Sanitize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var builder = new StringBuilder(value.Length);
        var lastWasDash = false;

        foreach (var character in value)
        {
            if (char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            {
                builder.Append(character);
                lastWasDash = false;
                continue;
            }

            if (!lastWasDash && builder.Length > 0)
            {
                builder.Append('-');
                lastWasDash = true;
            }
        }

        var result = builder.ToString().Trim('-').ToLowerInvariant();
        return result.Length <= maxLength ? result : result[..maxLength].Trim('-');
    }
}
