using DataVerification.Application.Common.Interfaces;
using DataVerification.Infrastructure.Email;
using FluentAssertions;
using Xunit;

namespace DataVerification.UnitTests.Email;

/// <summary>
/// The blind-copy list is administrator-typed, so the senders clean it before handing it to a
/// provider. These are the rules that stop a bad row turning into a rejected send or a duplicate
/// email landing in someone's inbox.
/// </summary>
public sealed class EmailBlindCopyTests
{
    private static EmailMessage Message(params string[] bcc) =>
        new("applicant@example.com", "Subject", "<p>html</p>", "text", Bcc: bcc);

    [Fact]
    public void NoBlindCopiesMeansNobodyIsCopied()
    {
        EmailRecipients.BlindCopies(Message()).Should().BeEmpty();

        var withoutList = new EmailMessage("a@example.com", "s", "h", "t");
        EmailRecipients.BlindCopies(withoutList).Should().BeEmpty();
    }

    [Fact]
    public void BlanksAreDroppedRatherThanSentAsEmptyRecipients()
    {
        // Some providers reject the whole send over one empty address.
        EmailRecipients.BlindCopies(Message("finance@example.com", "   ", ""))
            .Should().ContainSingle().Which.Should().Be("finance@example.com");
    }

    [Fact]
    public void AddressesAreTrimmed()
    {
        EmailRecipients.BlindCopies(Message("  finance@example.com  "))
            .Should().ContainSingle().Which.Should().Be("finance@example.com");
    }

    [Fact]
    public void TheSameMailboxIsOnlyCopiedOnce()
    {
        EmailRecipients.BlindCopies(Message("finance@example.com", "FINANCE@example.com"))
            .Should().ContainSingle();
    }

    [Fact]
    public void TheRecipientIsNeverBlindCopiedOnTheirOwnEmail()
    {
        // Copying them would deliver it twice and read as a bug to the person receiving it.
        EmailRecipients.BlindCopies(Message("APPLICANT@example.com", "finance@example.com"))
            .Should().ContainSingle().Which.Should().Be("finance@example.com");
    }
}
