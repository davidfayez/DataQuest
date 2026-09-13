using System.IO.Compression;
using System.Text;
using DataVerification.Domain.Common;
using DataVerification.Infrastructure.Storage;
using FluentAssertions;

namespace DataVerification.UnitTests.Storage;

/// <summary>
/// Identifying an upload by its bytes.
///
/// The extension is a claim the uploader makes; the signature is evidence. Both have to agree, and
/// for the Office formats — which are ZIP archives and so all share one signature — the evidence
/// is what is inside the archive.
/// </summary>
public sealed class FileTypeValidatorTests
{
    private readonly FileTypeValidator _validator = new();

    private static MemoryStream Bytes(params byte[] content) => new(content);

    private static MemoryStream Pdf() => Bytes(0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34);

    private static MemoryStream Png() =>
        Bytes(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00);

    private static MemoryStream Jpeg() => Bytes(0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46);

    /// <summary>A real ZIP container holding the entries that make it the named Office format.</summary>
    private static MemoryStream OfficePackage(params string[] entryNames)
    {
        var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in entryNames)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open(), Encoding.UTF8);
                writer.Write("<?xml version=\"1.0\"?><root />");
            }
        }

        buffer.Position = 0;
        return buffer;
    }

    private static MemoryStream Docx() => OfficePackage("[Content_Types].xml", "word/document.xml");

    private static MemoryStream Xlsx() => OfficePackage("[Content_Types].xml", "xl/workbook.xml");

    [Fact]
    public async Task RecognisesThePlatformsFormats()
    {
        (await _validator.DetectAllowedContentTypeAsync(Pdf(), "scan.pdf")).Should().Be("application/pdf");
        (await _validator.DetectAllowedContentTypeAsync(Jpeg(), "photo.jpg")).Should().Be("image/jpeg");
        (await _validator.DetectAllowedContentTypeAsync(Jpeg(), "photo.jpeg")).Should().Be("image/jpeg");
        (await _validator.DetectAllowedContentTypeAsync(Png(), "shot.png")).Should().Be("image/png");
    }

    [Fact]
    public async Task RecognisesWordAndExcel()
    {
        (await _validator.DetectAllowedContentTypeAsync(Docx(), "form.docx"))
            .Should().Be("application/vnd.openxmlformats-officedocument.wordprocessingml.document");

        (await _validator.DetectAllowedContentTypeAsync(Xlsx(), "sheet.xlsx"))
            .Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }

    [Fact]
    public async Task TellsWordAndExcelApart()
    {
        // Both are ZIPs with the same four leading bytes, so the extension alone proves nothing —
        // what is inside the archive is the only thing that separates them.
        (await _validator.DetectAllowedContentTypeAsync(Docx(), "actually-word.xlsx")).Should().BeNull();
        (await _validator.DetectAllowedContentTypeAsync(Xlsx(), "actually-excel.docx")).Should().BeNull();
    }

    [Fact]
    public async Task RefusesAPlainZipWearingAnOfficeExtension()
    {
        var zip = OfficePackage("holiday-photos/beach.jpg");

        (await _validator.DetectAllowedContentTypeAsync(zip, "payload.docx")).Should().BeNull();
    }

    [Fact]
    public async Task RefusesAnOfficeFileCarryingAMacroProject()
    {
        // The macro-free extensions are the only ones accepted, so a VBA project inside one is a
        // .docm that has been renamed.
        var macroLaden = OfficePackage("word/document.xml", "word/vbaProject.bin");

        (await _validator.DetectAllowedContentTypeAsync(macroLaden, "invoice.docx")).Should().BeNull();
    }

    [Fact]
    public async Task RefusesBytesThatDoNotMatchTheExtension()
    {
        (await _validator.DetectAllowedContentTypeAsync(Png(), "renamed.pdf")).Should().BeNull();
        (await _validator.DetectAllowedContentTypeAsync(Pdf(), "renamed.png")).Should().BeNull();
    }

    [Theory]
    [InlineData("payload.exe")]
    [InlineData("script.js")]
    [InlineData("archive.zip")]
    [InlineData("legacy.doc")]
    [InlineData("legacy.xls")]
    [InlineData("macro.docm")]
    [InlineData("noextension")]
    public async Task RefusesAnExtensionThePlatformDoesNotAccept(string fileName)
    {
        // .doc and .xls are refused deliberately: one OLE signature covers every compound file, so
        // accepting them would mean trusting the extension.
        (await _validator.DetectAllowedContentTypeAsync(Pdf(), fileName)).Should().BeNull();
    }

    [Fact]
    public async Task RestoresTheStreamSoTheCallerCanStillStoreTheFile()
    {
        var content = Docx();
        content.Position = 0;

        await _validator.DetectAllowedContentTypeAsync(content, "form.docx");

        content.Position.Should().Be(0);
        content.Length.Should().BeGreaterThan(0, "reading the archive must not consume the stream");
    }

    // ----------------------------------------------------- per-document narrowing

    [Fact]
    public async Task ADocumentMayNarrowWhatItAccepts()
    {
        IReadOnlyList<string> pdfOnly = [DocumentFileTypes.Pdf];

        (await _validator.DetectAllowedContentTypeAsync(Pdf(), "scan.pdf", pdfOnly))
            .Should().Be("application/pdf");

        // A format the platform allows but this document does not.
        (await _validator.DetectAllowedContentTypeAsync(Png(), "shot.png", pdfOnly)).Should().BeNull();
    }

    [Fact]
    public async Task ADocumentCannotWidenWhatThePlatformAccepts()
    {
        // Even asked for explicitly, an extension outside the platform list stays refused.
        IReadOnlyList<string> everything = [.. DocumentFileTypes.All];

        (await _validator.DetectAllowedContentTypeAsync(Pdf(), "payload.exe", everything))
            .Should().BeNull();
    }

    [Fact]
    public async Task NoPerDocumentRuleMeansThePlatformListApplies()
    {
        // An admin avatar or a ticket attachment has no document behind it.
        (await _validator.DetectAllowedContentTypeAsync(Png(), "shot.png", null))
            .Should().Be("image/png");
    }

    [Fact]
    public async Task AWordOnlyDocumentTakesWordAndNothingElse()
    {
        IReadOnlyList<string> wordOnly = [DocumentFileTypes.Word];

        (await _validator.DetectAllowedContentTypeAsync(Docx(), "form.docx", wordOnly))
            .Should().NotBeNull();

        (await _validator.DetectAllowedContentTypeAsync(Xlsx(), "sheet.xlsx", wordOnly))
            .Should().BeNull();
        (await _validator.DetectAllowedContentTypeAsync(Pdf(), "scan.pdf", wordOnly))
            .Should().BeNull();
    }
}
