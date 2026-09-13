using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace DataVerification.UnitTests.Domain;

/// <summary>
/// The rules on a payment method's provider integration.
///
/// The flags here decide what the admin page warns about, and one of them — an unsigned callback —
/// is the difference between a webhook that proves a payment landed and a public URL that accepts
/// "this order is paid" from anyone who finds it.
/// </summary>
public sealed class PaymentIntegrationTests
{
    private static PaymentMethodIntegration Integration() =>
        new() { PaymentMethodId = Guid.NewGuid() };

    [Fact]
    public void ANewIntegrationStartsInSandbox()
    {
        // Never Live by default: a half-configured method must not be able to move real money
        // before somebody has deliberately said it should.
        Integration().Mode.Should().Be(PaymentIntegrationMode.Sandbox);
    }

    [Fact]
    public void EachSecretReportsWhetherItIsSetWithoutExposingIt()
    {
        var integration = Integration();

        integration.HasApiKey.Should().BeFalse();
        integration.HasPassword.Should().BeFalse();
        integration.HasWebhookSecret.Should().BeFalse();

        integration.ApiKeySecret = "protected-blob";
        integration.PasswordSecret = "protected-blob";
        integration.WebhookSecret = "protected-blob";

        integration.HasApiKey.Should().BeTrue();
        integration.HasPassword.Should().BeTrue();
        integration.HasWebhookSecret.Should().BeTrue();
    }

    [Fact]
    public void WhitespaceIsNotACredential()
    {
        var integration = Integration();
        integration.ApiKeySecret = "   ";

        integration.HasApiKey.Should().BeFalse();
    }

    [Fact]
    public void ACallbackWithoutASigningSecretIsFlagged()
    {
        var integration = Integration();
        integration.CallbackUrl = "https://api.example.com/hooks/provider";

        integration.CallbackIsUnverified.Should().BeTrue();
    }

    [Fact]
    public void ACallbackWithASigningSecretIsNotFlagged()
    {
        var integration = Integration();
        integration.CallbackUrl = "https://api.example.com/hooks/provider";
        integration.WebhookSecret = "protected-blob";

        integration.CallbackIsUnverified.Should().BeFalse();
    }

    [Fact]
    public void NoCallbackMeansNothingToWarnAbout()
    {
        // The warning is about an endpoint that exists and cannot be trusted, not about the
        // absence of one.
        var integration = Integration();

        integration.CallbackIsUnverified.Should().BeFalse();
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("protected-blob", null, false)]
    [InlineData(null, "https://example.com/done", false)]
    [InlineData("protected-blob", "https://example.com/done", true)]
    public void UsableMeansSomethingToAuthenticateWithAndSomewhereToReturnTo(
        string? apiKey,
        string? redirectUrl,
        bool expected)
    {
        var integration = Integration();
        integration.ApiKeySecret = apiKey;
        integration.RedirectUrl = redirectUrl;

        integration.IsUsable.Should().Be(expected);
    }
}
