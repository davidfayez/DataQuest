using DataVerification.Domain.Common;
using FluentAssertions;

namespace DataVerification.UnitTests.Storage;

/// <summary>
/// What a downloaded file is called once it leaves the platform.
///
/// The name a person chose on their own machine says nothing in a folder of thirty others, so the
/// download carries the two references that identify what it belongs to and the label of the
/// document it satisfies.
/// </summary>
public sealed class DownloadFileNameTests
{
    [Fact]
    public void NamesAFileForItsApplicationOrderAndDocument()
    {
        DownloadFileName.Compose("APP-2026-0007", "NEN800433937", "صورة البطاقة", "scan.pdf")
            .Should().Be("APP-2026-0007_NEN800433937_صورة البطاقة.pdf");
    }

    [Fact]
    public void KeepsTheOriginalExtension()
    {
        DownloadFileName.Compose("APP-1", "NEN-1", "Photo", "IMG_2024.JPEG")
            .Should().EndWith(".JPEG");

        DownloadFileName.Compose("APP-1", "NEN-1", "Sheet", "data.xlsx")
            .Should().EndWith(".xlsx");
    }

    [Fact]
    public void FallsBackToTheUploadedNameWhenNothingDescribesTheFile()
    {
        // A result the review team attached satisfies no particular document.
        DownloadFileName.Compose("APP-2026-0007", "NEN800433937", null, "verification-result.pdf")
            .Should().Be("APP-2026-0007_NEN800433937_verification-result.pdf");
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a:b")]
    [InlineData("a*b")]
    [InlineData("a?b")]
    [InlineData("a\"b")]
    [InlineData("a<b")]
    [InlineData("a>b")]
    [InlineData("a|b")]
    public void StripsCharactersAFileNameCannotCarry(string label)
    {
        var name = DownloadFileName.Compose("APP-1", "NEN-1", label, "scan.pdf");

        name.Should().Be("APP-1_NEN-1_ab.pdf");
    }

    [Fact]
    public void CannotBeTalkedIntoEscapingItsFolder()
    {
        // A document label is operator-supplied text; it must never become a path.
        var name = DownloadFileName.Compose("APP-1", "NEN-1", "../../etc/passwd", "scan.pdf");

        name.Should().NotContain("/");
        name.Should().NotContain("\\");
    }

    [Fact]
    public void CollapsesWhitespaceAndTrimsTheEnds()
    {
        DownloadFileName.Compose("APP-1", "NEN-1", "  صورة    البطاقة  ", "scan.pdf")
            .Should().Be("APP-1_NEN-1_صورة البطاقة.pdf");
    }

    [Fact]
    public void DoesNotEndInADotOrSpace()
    {
        // Windows cannot store either, and the file arrives corrupted or not at all.
        DownloadFileName.Compose("APP-1", "NEN-1", "Trailing dot.", "scan")
            .Should().NotEndWith(".");
    }

    [Fact]
    public void KeepsTheNameShortEnoughToStore()
    {
        var name = DownloadFileName.Compose("APP-1", "NEN-1", new string('x', 500), "scan.pdf");

        name.Length.Should().BeLessThan(200);
        name.Should().EndWith(".pdf", "the extension survives the truncation");
    }

    [Fact]
    public void SkipsAReferenceThatIsMissing()
    {
        DownloadFileName.Compose(null, "NEN800433937", "Passport", "scan.pdf")
            .Should().Be("NEN800433937_Passport.pdf");

        DownloadFileName.Compose("APP-1", null, "Passport", "scan.pdf")
            .Should().Be("APP-1_Passport.pdf");
    }

    [Fact]
    public void GivesAUsableNameWhenItKnowsNothingAtAll()
    {
        // Never ".pdf" — some browsers refuse to save a file whose name is only an extension.
        DownloadFileName.Compose(null, null, null, ".pdf").Should().Be("download.pdf");
        DownloadFileName.Compose(null, null, null, null).Should().Be("download");
    }
}
