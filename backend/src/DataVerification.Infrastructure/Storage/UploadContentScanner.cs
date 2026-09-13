using System.Text;
using DataVerification.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace DataVerification.Infrastructure.Storage;

/// <summary>
/// Looks past the file signature for content that has no business being in a scanned document.
///
/// Matching the leading bytes proves a file starts like a PDF or an image; it does not prove the
/// rest is inert. A PDF can carry JavaScript that runs when the reviewer opens it, and a file that
/// begins with a valid image header can still have HTML appended so that a browser persuaded to
/// render it executes the tail instead.
///
/// The hard part is doing that without rejecting real documents. A scanned photo or a compressed
/// PDF stream is effectively random data, and a short marker searched over megabytes of it is
/// certain to appear by chance — an earlier version of this scanner rejected 100% of ordinary
/// images above 50 KB. Three rules keep it honest:
///
///   * markup is looked for only in the first few kilobytes, which is the window a browser sniffs;
///   * a PDF is searched through its structure only, never inside stream data;
///   * PDF names are matched case-sensitively and must end at a name boundary, as the spec defines
///     them, so <c>/AA</c> no longer matches <c>/AArgh</c> or a random <c>/aa</c> in a photo.
///
/// This is a cheap guard against careless content, not an antivirus. Files are stored outside the
/// web root and served as attachments, which is what actually stops them being executed.
/// </summary>
public sealed class UploadContentScanner : IUploadContentScanner
{
    /// <summary>
    /// Constructs that make a PDF do something rather than just display.
    ///
    /// <c>/OpenAction</c> is deliberately absent: in the overwhelming majority of files it points
    /// at a page destination ("open at page 1, fit width"), which Word and most scanners write as
    /// a matter of course. It is dangerous only when it points at JavaScript, and the JavaScript
    /// itself is caught by the two entries below.
    /// </summary>
    private static readonly string[] ActivePdfMarkers =
    [
        "/JavaScript",
        "/JS",
        "/Launch",
        "/EmbeddedFile",
        "/AA",           // additional-actions: fires on open, close, print
        "/RichMedia",
        "/XFA",
    ];

    /// <summary>
    /// Markup a browser would act on if it were persuaded to render the file.
    ///
    /// <c>&lt;%</c> and <c>&lt;?php</c> are deliberately absent: both need a server-side
    /// interpreter, and these files are only ever read from disk and streamed back as attachments.
    /// <c>&lt;%</c> in particular is two bytes long, so it occurs by chance in essentially every
    /// photograph.
    /// </summary>
    private static readonly string[] ActiveMarkupMarkers =
    [
        "<script",
        "javascript:",
        "<iframe",
        "<embed",
        "<object",
        "onerror=",
        "onload=",
        "<svg",
    ];

    /// <summary>
    /// How much of a non-PDF upload is examined for markup. Browsers sniff the first 512 bytes to
    /// decide whether something is HTML, so a polyglot has to put its markup near the front to be
    /// useful. Scanning further buys nothing and starts matching photo data by chance.
    /// </summary>
    private const int MarkupScanBytes = 4096;

    private static readonly byte[] ExecutableHeader = [0x4D, 0x5A];      // "MZ"
    private static readonly byte[] ElfHeader = [0x7F, 0x45, 0x4C, 0x46]; // "\x7FELF"

    private readonly ILogger<UploadContentScanner> _logger;

    public UploadContentScanner(ILogger<UploadContentScanner> logger) => _logger = logger;

    public async Task<string?> FindThreatAsync(
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (!content.CanSeek)
        {
            throw new ArgumentException("A seekable stream is required to scan content.", nameof(content));
        }

        var originalPosition = content.Position;

        try
        {
            content.Position = 0;
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            var bytes = buffer.ToArray();

            // Only the file's own header. Hunting for "MZ" anywhere in the body matched roughly
            // half of all photographs, and an appended executable cannot run from here anyway.
            if (StartsWith(bytes, ExecutableHeader) || StartsWith(bytes, ElfHeader))
            {
                _logger.LogWarning("Rejected an upload whose own header is an executable.");
                return "file.executable_content";
            }

            // Latin1 keeps every byte a distinct character, so a marker split across a multi-byte
            // sequence cannot slip through the way it could with a lossy UTF-8 decode.
            if (contentType == "application/pdf")
            {
                var structure = ExtractPdfStructure(Encoding.Latin1.GetString(bytes));

                foreach (var marker in ActivePdfMarkers)
                {
                    if (ContainsPdfName(structure, marker))
                    {
                        _logger.LogWarning(
                            "Rejected a PDF upload: active-content marker {Marker} found in the document structure.",
                            marker);
                        return "file.active_content";
                    }
                }

                return null;
            }

            var head = Encoding.Latin1.GetString(bytes, 0, Math.Min(bytes.Length, MarkupScanBytes));

            foreach (var marker in ActiveMarkupMarkers)
            {
                var at = head.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (at >= 0)
                {
                    _logger.LogWarning(
                        "Rejected an upload: markup marker {Marker} found at byte {Offset}.", marker, at);
                    return "file.active_content";
                }
            }

            return null;
        }
        finally
        {
            content.Position = originalPosition;
        }
    }

    private static bool StartsWith(byte[] bytes, byte[] signature) =>
        bytes.Length >= signature.Length && bytes.AsSpan(0, signature.Length).SequenceEqual(signature);

    /// <summary>
    /// Returns the PDF with the contents of every <c>stream … endstream</c> removed.
    ///
    /// Stream payloads are compressed image and font data — effectively random bytes, in which any
    /// short marker appears by chance. Active content has to be declared in an object dictionary to
    /// take effect, and dictionaries live outside stream data, so this is where the markers matter.
    /// A file that hides JavaScript inside a compressed object stream will pass; that is the
    /// deliberate trade for not rejecting every real document.
    /// </summary>
    private static string ExtractPdfStructure(string text)
    {
        const string StreamKeyword = "stream";
        const string EndStreamKeyword = "endstream";

        var structure = new StringBuilder(Math.Min(text.Length, 64 * 1024));
        var index = 0;

        while (index < text.Length)
        {
            var streamAt = text.IndexOf(StreamKeyword, index, StringComparison.Ordinal);
            if (streamAt < 0)
            {
                structure.Append(text, index, text.Length - index);
                break;
            }

            // "endstream" contains "stream"; only the opening keyword starts a payload.
            if (streamAt >= 3 && string.CompareOrdinal(text, streamAt - 3, "end", 0, 3) == 0)
            {
                var through = streamAt + StreamKeyword.Length;
                structure.Append(text, index, through - index);
                index = through;
                continue;
            }

            structure.Append(text, index, streamAt - index);

            var endAt = text.IndexOf(EndStreamKeyword, streamAt, StringComparison.Ordinal);
            if (endAt < 0)
            {
                // Truncated or malformed: no closing keyword, so the rest is payload.
                break;
            }

            index = endAt + EndStreamKeyword.Length;
        }

        return structure.ToString();
    }

    /// <summary>
    /// True when the marker appears as a complete PDF name. Names are case-sensitive and end at a
    /// delimiter, so <c>/AA</c> must not match <c>/AArgh</c>, and a lower-case <c>/aa</c> in a
    /// document title is not an additional-actions dictionary.
    /// </summary>
    private static bool ContainsPdfName(string structure, string marker)
    {
        var from = 0;

        while (from <= structure.Length - marker.Length)
        {
            var at = structure.IndexOf(marker, from, StringComparison.Ordinal);
            if (at < 0)
            {
                return false;
            }

            var next = at + marker.Length;
            if (next >= structure.Length || !char.IsLetterOrDigit(structure[next]))
            {
                return true;
            }

            from = at + 1;
        }

        return false;
    }
}
