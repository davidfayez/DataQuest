using DataVerification.Domain.Entities;
using FluentAssertions;

namespace DataVerification.UnitTests.Domain;

/// <summary>Picking the description a reader sees, in the same way names are picked.</summary>
public sealed class DescribedLookupTests
{
    private static TransactionType Type(string? ar, string? en) => new()
    {
        NameAr = "نوع",
        NameEn = "Type",
        DescriptionAr = ar,
        DescriptionEn = en,
    };

    [Fact]
    public void ReadsTheArabicDescriptionInArabic() =>
        Type("عربي", "English").ResolveDescription("ar").Should().Be("عربي");

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData(null)]
    public void ReadsTheEnglishDescriptionEverywhereElse(string? language) =>
        Type("عربي", "English").ResolveDescription(language).Should().Be("English");

    [Fact]
    public void FallsBackToTheOtherLanguageWhenOneIsBlank()
    {
        Type("   ", "English").ResolveDescription("ar").Should().Be("English");
        Type("عربي", null).ResolveDescription("en").Should().Be("عربي");
    }

    [Fact]
    public void GivesNothingForARowSavedBeforeDescriptionsExisted() =>
        Type(null, null).ResolveDescription("en").Should().BeNull();
}
