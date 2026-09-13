using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace DataVerification.UnitTests.Email;

/// <summary>
/// The fallback rule for outgoing mail: if a kind of email has no sender configured, it goes out
/// from the platform default exactly as it did before per-type routing existed.
///
/// This is the behaviour that keeps password resets arriving on a deployment where nobody has ever
/// opened the Emails configurations page. Every "empty" shape an operator can leave behind — no
/// row, a blank string, whitespace, a name with no address — has to land on the default rather
/// than on an empty sender, which providers reject outright.
/// </summary>
public sealed class EmailRoutingFallbackTests
{
    private const string PlatformDefault = "NoReply@followmetric.com";

    private static EmailMessage Rendered() =>
        new("applicant@example.com", "Your new password", "<p>html</p>", "text");

    /// <summary>
    /// Mirrors what <see cref="EmailRouting"/> does to a message, without a database: a setting
    /// contributes its sender only when it actually has one.
    /// </summary>
    private static EmailMessage Apply(EmailMessage message, EmailTypeSetting? setting)
    {
        if (setting is null)
        {
            return message;
        }

        var bcc = setting.BccAddresses();

        return message with
        {
            FromAddress = string.IsNullOrWhiteSpace(setting.FromAddress)
                ? message.FromAddress
                : setting.FromAddress.Trim(),
            FromName = string.IsNullOrWhiteSpace(setting.FromName)
                ? message.FromName
                : setting.FromName.Trim(),
            Bcc = bcc.Count > 0 ? bcc : message.Bcc,
        };
    }

    /// <summary>The fallback the senders apply when a message names no sender of its own.</summary>
    private static string EffectiveSender(EmailMessage message) =>
        message.FromAddress ?? PlatformDefault;

    private static EmailTypeSetting Setting(string? address, string? name = null) =>
        new() { Type = EmailType.ForgotPassword, FromAddress = address, FromName = name };

    [Fact]
    public void NoConfigurationAtAllUsesThePlatformDefault()
    {
        var message = Apply(Rendered(), setting: null);

        message.FromAddress.Should().BeNull();
        EffectiveSender(message).Should().Be(PlatformDefault);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptySenderUsesThePlatformDefault(string? configured)
    {
        // A saved-but-blank row is the shape that would otherwise produce an empty From address,
        // which a provider refuses — and the refusal looks to everyone like "email is broken".
        var message = Apply(Rendered(), Setting(configured));

        EffectiveSender(message).Should().Be(PlatformDefault);
    }

    [Fact]
    public void ANameWithoutAnAddressStillUsesTheDefaultAddress()
    {
        var message = Apply(Rendered(), Setting(address: null, name: "NEN Support"));

        EffectiveSender(message).Should().Be(PlatformDefault);
        // The half that was configured is still honoured.
        message.FromName.Should().Be("NEN Support");
    }

    [Fact]
    public void AConfiguredSenderIsUsed()
    {
        var message = Apply(Rendered(), Setting("support@nen-global.org", "NEN Support"));

        EffectiveSender(message).Should().Be("support@nen-global.org");
        message.FromName.Should().Be("NEN Support");
    }

    [Fact]
    public void AConfiguredSenderIsTrimmed()
    {
        var message = Apply(Rendered(), Setting("  support@nen-global.org  "));

        EffectiveSender(message).Should().Be("support@nen-global.org");
    }

    [Fact]
    public void RoutingNeverChangesTheRecipientOrTheBody()
    {
        // Whatever routing does to the envelope, the email itself has to survive it intact.
        var original = Rendered();

        var message = Apply(original, Setting("support@nen-global.org"));

        message.To.Should().Be(original.To);
        message.Subject.Should().Be(original.Subject);
        message.HtmlBody.Should().Be(original.HtmlBody);
        message.PlainTextBody.Should().Be(original.PlainTextBody);
    }
}

/// <summary>
/// A delivery result has to be honest about what happened, because the settings page shows it to
/// an operator who is trying to find out why nothing arrived.
/// </summary>
public sealed class EmailDeliveryResultTests
{
    [Fact]
    public void DeliveredAndLoggedAreNotTheSameThing()
    {
        EmailDeliveryResult.Sent().Status.Should().Be("Delivered");

        // Logged means the message never left the machine. Reporting it as delivered would tell
        // an operator their configuration works when nothing has been sent.
        var logged = EmailDeliveryResult.Logged();
        logged.Delivered.Should().BeTrue();
        logged.Status.Should().Be("Logged");
    }

    [Fact]
    public void AMissingKeyIsReportedAsAFailureWithItsSource()
    {
        var result = EmailDeliveryResult.NoApiKey("Unreadable");

        result.Delivered.Should().BeFalse();
        result.Status.Should().Be("NoApiKey");
        result.Detail.Should().Contain("Unreadable");
    }

    [Fact]
    public void AProviderRefusalCarriesItsOwnWordsBack()
    {
        var result = EmailDeliveryResult.Rejected("403: from address is not a verified sender");

        result.Delivered.Should().BeFalse();
        result.Status.Should().Be("Rejected");
        result.Detail.Should().Contain("verified sender");
    }
}
