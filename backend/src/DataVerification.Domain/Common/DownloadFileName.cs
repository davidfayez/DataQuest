using System.Text;

namespace DataVerification.Domain.Common;

/// <summary>
/// Names a downloaded application file <c>{application}_{order}_{label}.{ext}</c>, for example
/// <c>APP-2026-0007_NEN800433937_صورة البطاقة.pdf</c>.
/// </summary>
/// <remarks>
/// Files leave the platform and land in a folder beside dozens of others. The name a person chose
/// on their own computer — <c>scan.pdf</c>, <c>IMG_2024.jpg</c> — says nothing once it is out of
/// context, so the download carries the two references that identify what it belongs to and the
/// label of the document it satisfies.
///
/// The composed name is built once here and used by both the API's <c>Content-Disposition</c>
/// header and the browser's own download attribute, which is the name that actually wins when a
/// front end fetches a file as a blob.
/// </remarks>
public static class DownloadFileName
{
    /// <summary>
    /// Characters a file name cannot carry on Windows, plus the ones that would let a name escape
    /// its folder. Arabic and other letters pass through untouched.
    /// </summary>
    private static readonly char[] Illegal =
        ['\\', '/', ':', '*', '?', '"', '<', '>', '|', '\r', '\n', '\t'];

    /// <summary>
    /// Long enough for the two references and a descriptive label, short enough to stay well
    /// inside the limits of every filesystem once a download folder's path is added.
    /// </summary>
    private const int MaxStemLength = 150;

    /// <param name="applicationNumber">The application's human reference, e.g. <c>APP-2026-0007</c>.</param>
    /// <param name="orderNumber">The order's human reference, e.g. <c>NEN800433937</c>.</param>
    /// <param name="label">
    /// What the file is — the required document's name in the reader's language. Falls back to the
    /// uploaded file's own name when the file satisfies no particular document, such as a result
    /// the review team attached.
    /// </param>
    /// <param name="originalFileName">The name as uploaded; supplies the extension.</param>
    public static string Compose(
        string? applicationNumber,
        string? orderNumber,
        string? label,
        string? originalFileName)
    {
        var extension = Path.GetExtension(originalFileName ?? string.Empty);

        var descriptor = Clean(label);
        if (descriptor.Length == 0)
        {
            // Nothing describes this file but the name it arrived with, so use that — minus its
            // extension, which is put back at the end.
            descriptor = Clean(Path.GetFileNameWithoutExtension(originalFileName ?? string.Empty));
        }

        var parts = new[] { Clean(applicationNumber), Clean(orderNumber), descriptor }
            .Where(part => part.Length > 0)
            .ToList();

        // Every part was blank — a file with no references and no name. Better a generic name than
        // a file called ".pdf", which some browsers refuse to save at all.
        var stem = parts.Count > 0 ? string.Join('_', parts) : "download";

        if (stem.Length > MaxStemLength)
        {
            stem = stem[..MaxStemLength].TrimEnd();
        }

        return stem + extension;
    }

    /// <summary>
    /// Strips what a file name may not contain and collapses the whitespace, leaving the letters
    /// of any script alone.
    /// </summary>
    private static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var builder = new StringBuilder(value.Length);
        var lastWasSpace = false;

        foreach (var character in value.Trim())
        {
            if (Illegal.Contains(character) || char.IsControl(character))
            {
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                // One space between words; a run of them in a file name helps nobody.
                if (!lastWasSpace && builder.Length > 0) builder.Append(' ');
                lastWasSpace = true;
                continue;
            }

            builder.Append(character);
            lastWasSpace = false;
        }

        // A name ending in a dot or space is not storable on Windows.
        return builder.ToString().TrimEnd('.', ' ');
    }
}
