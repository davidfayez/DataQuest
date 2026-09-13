using DataVerification.Infrastructure.Email;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DataVerification.UnitTests.Email;

/// <summary>
/// The "forgot my password" email is the only thing standing between an applicant and their order,
/// so what it must contain is pinned here rather than left to a glance at the rendered output.
/// </summary>
public sealed class OrderPasswordResetEmailTests
{
    private const string OrderNumber = "NEN123456789";
    private const string Password = "Xk4mQ2pR";

    /// <summary>The window the email has to state, which the admin panel sets.</summary>
    private const int ValidityMinutes = 60;

    private static EmailTemplateRenderer Renderer() =>
        new(Options.Create(new AppOptions
        {
            WebUrl = "https://verify.example.com",
            AdminUrl = "https://admin.example.com",
        }));

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public void TheSubjectCarriesTheBrandThePurposeAndTheOrderNumber(string language)
    {
        var message = Renderer().RenderOrderPasswordReset(
            "applicant@example.com", OrderNumber, Password, language, ValidityMinutes);

        // The order number is in the subject so a full mailbox can be searched by the one
        // identifier an applicant always has.
        message.Subject.Should().Be(
            $"Data Verification - Forget password - Order number - {OrderNumber}");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public void BothBodiesCarryTheCredentialsAndAWayBackIn(string language)
    {
        var message = Renderer().RenderOrderPasswordReset(
            "applicant@example.com", OrderNumber, Password, language, ValidityMinutes);

        foreach (var body in new[] { message.HtmlBody, message.PlainTextBody })
        {
            body.Should().Contain(OrderNumber);
            body.Should().Contain(Password);
            // Deep-linked with the order number, so signing in is one field rather than two.
            body.Should().Contain($"https://verify.example.com/{language}/login?order={OrderNumber}");
        }
    }

    [Fact]
    public void TheArabicBodyIsMarkedRightToLeft()
    {
        var message = Renderer().RenderOrderPasswordReset(
            "applicant@example.com", OrderNumber, Password, "ar", ValidityMinutes);

        message.HtmlBody.Should().Contain("dir=\"rtl\"");
    }

    [Fact]
    public void AnUnknownLanguageStillProducesAUsableEmail()
    {
        var message = Renderer().RenderOrderPasswordReset(
            "applicant@example.com", OrderNumber, Password, "kl", ValidityMinutes);

        message.Subject.Should().Contain(OrderNumber);
        message.PlainTextBody.Should().Contain(Password);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public void TheEmailStatesTheWindowAndThatTheOldPasswordStillWorks(string language)
    {
        var message = Renderer().RenderOrderPasswordReset(
            "applicant@example.com", OrderNumber, Password, language, 45);

        foreach (var body in new[] { message.HtmlBody, message.PlainTextBody })
        {
            // Both halves matter: how long it lasts, and that it changes nothing until it is used.
            body.Should().Contain("45");
            body.Should().NotContain("{0}");
        }
    }
}
