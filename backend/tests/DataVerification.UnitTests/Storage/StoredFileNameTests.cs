using DataVerification.Infrastructure.Storage;
using FluentAssertions;

namespace DataVerification.UnitTests.Storage;

/// <summary>
/// The stored name is built from a client-supplied file name, so these cover both halves of the
/// contract: the shape the platform asked for, and the fact that nothing hostile survives into it.
/// </summary>
public class StoredFileNameTests
{
    private static readonly DateTime Moment = new(2026, 7, 31, 18, 45, 0, 123, DateTimeKind.Utc);
    private const string Scope = "3f2a1b4c5d6e7f80";

    [Fact]
    public void Build_ComposesOriginalNameScopeAndTimestamp()
    {
        var result = StoredFileName.Build("passport.pdf", Scope, ".pdf", Moment);

        result.Should().Be($"passport_{Scope}_20260731T184500123.pdf");
    }

    [Fact]
    public void Build_OmitsTheScopeSegmentWhenThereIsNoOwner()
    {
        var result = StoredFileName.Build("passport.pdf", null, ".pdf", Moment);

        result.Should().Be("passport_20260731T184500123.pdf");
    }

    [Theory]
    // A separator or traversal sequence must not survive into the stored name.
    [InlineData("../../etc/passwd.pdf", "passwd")]
    [InlineData(@"..\..\windows\system32\cmd.pdf", "cmd")]
    [InlineData("C:/Users/admin/secret.pdf", "secret")]
    // A second extension is flattened, so nothing downstream can dispatch on it.
    [InlineData("invoice.php.pdf", "invoice-php")]
    [InlineData("shell.aspx.pdf", "shell-aspx")]
    // Spaces, punctuation and case are normalised.
    [InlineData("My Passport Scan (final).pdf", "my-passport-scan-final")]
    [InlineData("report...v2.pdf", "report-v2")]
    public void Build_StripsAnythingThatCouldBeReadAsAPath(string original, string expectedBase)
    {
        var result = StoredFileName.Build(original, Scope, ".pdf", Moment);

        result.Should().Be($"{expectedBase}_{Scope}_20260731T184500123.pdf");
        result.Should().NotContain("..");
        result.Should().NotContain("/");
        result.Should().NotContain("\\");
    }

    [Theory]
    [InlineData("شهادة الميلاد.pdf")]  // entirely non-ASCII
    [InlineData("....pdf")]
    [InlineData("")]
    public void Build_FallsBackWhenNothingUsableRemains(string original)
    {
        var result = StoredFileName.Build(original, Scope, ".pdf", Moment);

        result.Should().Be($"upload_{Scope}_20260731T184500123.pdf");
    }

    [Fact]
    public void Build_TruncatesAnAbsurdlyLongNameSoThePathStaysWithinLimits()
    {
        var result = StoredFileName.Build(new string('a', 500) + ".pdf", Scope, ".pdf", Moment);

        result.Length.Should().BeLessThan(150);
        result.Should().EndWith(".pdf");
    }

    [Fact]
    public void Build_AddsASuffixSoASecondFileInTheSameMillisecondCannotOverwriteTheFirst()
    {
        var first = StoredFileName.Build("scan.pdf", Scope, ".pdf", Moment);
        var second = StoredFileName.Build("scan.pdf", Scope, ".pdf", Moment, attempt: 1);

        second.Should().NotBe(first);
        second.Should().Be($"scan_{Scope}_20260731T184500123-1.pdf");
    }

    [Fact]
    public void Build_AlwaysUsesTheValidatedExtensionRatherThanTheClaimedOne()
    {
        // The caller passes the extension the signature check agreed to, so a name claiming
        // something else cannot change what the file is stored as.
        var result = StoredFileName.Build("payload.exe", Scope, ".png", Moment);

        result.Should().EndWith(".png");
        result.Should().NotContain(".exe");
    }
}
