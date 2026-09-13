using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace DataVerification.UnitTests.Domain;

/// <summary>
/// The contact directory shown on the public page.
///
/// The rule worth pinning is what counts as reachable: an entry naming a country with no way to
/// contact anyone in it tells a visitor there is someone there while giving them nothing to act
/// on, which is worse than leaving the country off the list.
/// </summary>
public sealed class ContactDirectoryTests
{
    private static ContactDirectoryEntry Entry(ContactEntryKind kind = ContactEntryKind.AuthorizedAgent) =>
        new() { Kind = kind, CountryId = Guid.NewGuid() };

    [Fact]
    public void AnEntryWithNothingToContactByIsNotReachable()
    {
        Entry().IsReachable.Should().BeFalse();
    }

    [Theory]
    [InlineData("+201005551234", null, null)]
    [InlineData(null, "agent@example.com", null)]
    [InlineData(null, null, "154 Al-Sharif Hussein St, Amman")]
    public void AnyOneContactDetailIsEnough(string? phone, string? email, string? address)
    {
        var entry = Entry();
        entry.Phone = phone;
        entry.Email = email;
        entry.AddressEn = address;

        entry.IsReachable.Should().BeTrue();
    }

    [Fact]
    public void AnArabicOnlyAddressStillCounts()
    {
        // A directory maintained only in Arabic is a real case, not an edge one.
        var entry = Entry();
        entry.AddressAr = "١٥٤ الشريف حسين – عمان";

        entry.IsReachable.Should().BeTrue();
    }

    [Fact]
    public void WhitespaceIsNotAContactDetail()
    {
        var entry = Entry();
        entry.Phone = "   ";
        entry.Email = "  ";

        entry.IsReachable.Should().BeFalse();
    }

    [Fact]
    public void ATitleResolvesToTheRequestedLanguage()
    {
        var entry = Entry();
        entry.TitleEn = "Head office";
        entry.TitleAr = "المكتب الرئيسي";

        entry.ResolveTitle("en").Should().Be("Head office");
        entry.ResolveTitle("ar").Should().Be("المكتب الرئيسي");
    }

    [Fact]
    public void ATitleFallsBackToTheOtherScriptRatherThanShowingNothing()
    {
        // Half-translated data is the norm while a directory is being filled in; showing the one
        // language that exists beats showing a blank line.
        var entry = Entry();
        entry.TitleEn = "Head office";

        entry.ResolveTitle("ar").Should().Be("Head office");
    }

    [Fact]
    public void AnEntryWithNoTitleAtAllResolvesToNull()
    {
        // Null rather than empty, so the page can decide to show the country name instead.
        Entry().ResolveTitle("en").Should().BeNull();
    }

    [Fact]
    public void AddressesResolveTheSameWay()
    {
        var entry = Entry(ContactEntryKind.Administration);
        entry.AddressAr = "١٥٤ الشريف حسين – عمان – الأردن";

        entry.ResolveAddress("ar").Should().Be("١٥٤ الشريف حسين – عمان – الأردن");
        entry.ResolveAddress("en").Should().Be("١٥٤ الشريف حسين – عمان – الأردن");
    }

    [Fact]
    public void AnEntryIsActiveUntilSomebodySaysOtherwise()
    {
        Entry().IsActive.Should().BeTrue();
    }
}
