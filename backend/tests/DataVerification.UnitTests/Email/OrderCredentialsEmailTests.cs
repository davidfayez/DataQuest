using DataVerification.Infrastructure.Email;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DataVerification.UnitTests.Email;

/// <summary>
/// The registration email is the only copy of an applicant's credentials, so the things that make
/// it findable and usable are pinned here.
/// </summary>
public sealed class OrderCredentialsEmailTests
{
    private const string OrderNumber = "NEN987654321";
    private const string Password = "Rt7wQ1zB";

    private static EmailTemplateRenderer Renderer() =>
        new(Options.Create(new AppOptions { WebUrl = "https://verify.example.com" }));

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    [InlineData("ru")]
    [InlineData("ja")]
    public void TheSubjectEndsWithTheOrderNumberInEveryLanguage(string language)
    {
        var message = Renderer().RenderOrderCredentials(
            "applicant@example.com", OrderNumber, Password, language);

        // A mailbox full of platform mail is searched by the order number, so it has to be in the
        // subject whatever language the rest of it is in.
        message.Subject.Should().EndWith($" - {OrderNumber}");
    }

    [Fact]
    public void TheSubjectKeepsItsLocalisedWording()
    {
        var english = Renderer().RenderOrderCredentials(
            "applicant@example.com", OrderNumber, Password, "en");
        var arabic = Renderer().RenderOrderCredentials(
            "applicant@example.com", OrderNumber, Password, "ar");

        // Appending the number must not have flattened the translations into one string.
        english.Subject.Should().Be($"Your NEN Verification credentials - {OrderNumber}");
        arabic.Subject.Should().NotBe(english.Subject);
        arabic.Subject.Should().EndWith($" - {OrderNumber}");
    }

    [Fact]
    public void BothBodiesStillCarryTheCredentials()
    {
        var message = Renderer().RenderOrderCredentials(
            "applicant@example.com", OrderNumber, Password, "en");

        foreach (var body in new[] { message.HtmlBody, message.PlainTextBody })
        {
            body.Should().Contain(OrderNumber);
            body.Should().Contain(Password);
            body.Should().Contain($"https://verify.example.com/en/login?order={OrderNumber}");
        }
    }
}
