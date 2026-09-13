using System.IO.Compression;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Common;
using DataVerification.Domain.Entities;

namespace DataVerification.Infrastructure.Storage;

/// <summary>
/// Identifies an upload by its leading bytes rather than its extension. Renaming <c>payload.exe</c>
/// to <c>scan.pdf</c> gets it past an extension check but not past this one, and the extension must
/// additionally agree with the detected type so a PNG cannot be stored claiming to be a PDF.
/// </summary>
public sealed class FileTypeValidator : IFileTypeValidator
{
    private static readonly FileSignature[] Signatures =
    [
        // "%PDF"
        new("application/pdf", [0x25, 0x50, 0x44, 0x46], [".pdf"]),
        // JPEG start-of-image marker; the third byte varies by encoder.
        new("image/jpeg", [0xFF, 0xD8, 0xFF], [".jpg", ".jpeg"]),
        new("image/png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], [".png"]),
        // Both modern Office formats are ZIP archives, so the signature only gets us as far as
        // "a zip". Which kind of document it is has to be read from inside — see LooksLikeAsync.
        new(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [0x50, 0x4B, 0x03, 0x04],
            [".docx"]),
        new(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [0x50, 0x4B, 0x03, 0x04],
            [".xlsx"]),
    ];

    private static readonly int LongestSignature = Signatures.Max(s => s.Magic.Length);

    /// <summary>
    /// The entry that proves what an Office package actually is. A .docx renamed to .xlsx has
    /// <c>word/document.xml</c> inside it and no workbook, so the extension is a lie.
    /// </summary>
    private static readonly Dictionary<string, string> OfficeMarker =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".docx"] = "word/document.xml",
            [".xlsx"] = "xl/workbook.xml",
        };

    public Task<string?> DetectAllowedContentTypeAsync(
        Stream content,
        string fileName,
        CancellationToken cancellationToken = default) =>
        DetectAllowedContentTypeAsync(content, fileName, null, cancellationToken);

    public async Task<string?> DetectAllowedContentTypeAsync(
        Stream content,
        string fileName,
        IReadOnlyList<string>? allowedFileTypes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();

        if (!ApplicationFile.IsAllowedExtension(extension))
        {
            return null;
        }

        // A document may narrow what the platform accepts, never widen it. Null means the caller
        // has no per-document rule — an admin avatar, a ticket attachment — and the platform-wide
        // list is the only bar.
        if (allowedFileTypes is not null)
        {
            var code = DocumentFileTypes.CodeForExtension(extension);
            if (code is null || !allowedFileTypes.Contains(code, StringComparer.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        if (!content.CanSeek)
        {
            throw new ArgumentException("A seekable stream is required to sniff the file type.", nameof(content));
        }

        var originalPosition = content.Position;

        try
        {
            var header = new byte[LongestSignature];
            var read = await content.ReadAtLeastAsync(
                header,
                LongestSignature,
                throwOnEndOfStream: false,
                cancellationToken);

            foreach (var signature in Signatures)
            {
                if (read < signature.Magic.Length)
                {
                    continue;
                }

                if (!header.AsSpan(0, signature.Magic.Length).SequenceEqual(signature.Magic))
                {
                    continue;
                }

                // The claimed extension has to match what the bytes actually are.
                if (!signature.Extensions.Contains(extension))
                {
                    continue;
                }

                if (OfficeMarker.TryGetValue(extension, out var marker))
                {
                    content.Position = originalPosition;
                    return await IsOfficePackageAsync(content, marker, cancellationToken)
                        ? signature.ContentType
                        : null;
                }

                return signature.ContentType;
            }

            return null;
        }
        finally
        {
            content.Position = originalPosition;
        }
    }

    /// <summary>
    /// Confirms a ZIP is the Office document its extension claims, and carries no macro project.
    /// </summary>
    /// <remarks>
    /// Only the central directory is read — entry names, never their contents — so this costs
    /// almost nothing and cannot be turned into a decompression bomb. A package containing
    /// <c>vbaProject.bin</c> is refused outright: the macro-free extensions are the only ones
    /// accepted, so a macro project inside one is a file pretending to be something it is not.
    /// </remarks>
    private static async Task<bool> IsOfficePackageAsync(
        Stream content,
        string marker,
        CancellationToken cancellationToken)
    {
        try
        {
            // Copied because ZipArchive disposes of what it is given and seeks freely; the caller
            // still needs the original stream afterwards to store the file.
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            copy.Position = 0;

            using var archive = new ZipArchive(copy, ZipArchiveMode.Read, leaveOpen: true);

            var names = archive.Entries.Select(entry => entry.FullName).ToList();

            if (names.Any(name => name.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            return names.Contains(marker, StringComparer.OrdinalIgnoreCase);
        }
        catch (InvalidDataException)
        {
            // Not a readable archive, whatever its first four bytes said.
            return false;
        }
    }

    private sealed record FileSignature(string ContentType, byte[] Magic, string[] Extensions);
}
