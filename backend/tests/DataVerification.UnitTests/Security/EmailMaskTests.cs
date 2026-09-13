using DataVerification.Application.Features.Orders.Commands;
using FluentAssertions;

namespace DataVerification.UnitTests.Security;

/// <summary>
/// The mask shown on the "we have emailed you" page and in the reset log.
///
/// It has one job with two halves to it: tell the applicant which of their mailboxes to open, and
/// tell anybody else as little as possible about an address they do not already know.
/// </summary>
public sealed class EmailMaskTests
{
    [Theory]
    [InlineData("david93@gmail.com", "da*****93@**mail.com")]
    [InlineData("someone@hotmail.com", "so*****ne@**mail.com")]
    [InlineData("itep@nen-global.org", "i*****p@**obal.org")]
    public void MasksAnAddressToItsRecognisableShape(string email, string expected) =>
        EmailMask.Apply(email).Should().Be(expected);

    [Fact]
    public void KeepsTheTopLevelDomainWhole()
    {
        // The part that tells someone which of their mailboxes this is.
        EmailMask.Apply("david93@gmail.com").Should().EndWith(".com");
        EmailMask.Apply("david93@example.co.uk").Should().EndWith(".uk");
    }

    [Theory]
    [InlineData("a@b.com")]
    [InlineData("ab@cd.com")]
    [InlineData("abc@qq.com")]
    public void NeverShowsAShortPartWhole(string email)
    {
        var masked = EmailMask.Apply(email);

        var local = email[..email.IndexOf('@', StringComparison.Ordinal)];
        var domain = email[(email.IndexOf('@', StringComparison.Ordinal) + 1)..];
        var label = domain[..domain.LastIndexOf('.')];

        // A two-letter mailbox or domain would otherwise be handed over intact by a mask that
        // keeps "the first and last two characters".
        masked.Should().NotContain(local);
        masked.Should().NotContain(label);
    }

    [Fact]
    public void HidesHowLongTheHiddenPartIs()
    {
        // Both runs of stars are fixed length, so the mask says nothing about what it covers.
        EmailMask.Apply("dave@gmail.com").Should().Be("d*****e@**mail.com");
        EmailMask.Apply("davidfayez1234567@gmail.com").Should().Be("da*****67@**mail.com");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GivesNothingBackForNoAddress(string? email) =>
        EmailMask.Apply(email).Should().BeEmpty();

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("@nolocal.com")]
    [InlineData("trailing@")]
    public void MasksAnythingItCannotParseRatherThanGuessing(string email)
    {
        // Better to say nothing than to reveal half of a string we did not understand.
        EmailMask.Apply(email).Should().Be("*****");
    }

    [Fact]
    public void MasksADomainThatHasNoDot()
    {
        EmailMask.Apply("david93@localhost").Should().Be("da*****93@**host");
    }
}
