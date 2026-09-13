using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// The band that closes the landing page: its heading, its copy, its button and where that button
/// goes.
///
/// The destination is the part worth pinning. It is one setting for every language rather than one
/// per language, and it is validated like a footer link — a button nobody reads before clicking is
/// exactly where a javascript: address does damage.
/// </summary>
public sealed class LandingCtaTests : ApiTestBase
{
    public LandingCtaTests(ApiFactory factory) : base(factory) { }

    private static string Landing => Url("/content/landing");
    private static string AdminHeading => Url("/admin/content/landing/heading");

    private static async Task<JsonElement> LandingAsync(HttpClient client, string? language = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Landing);
        if (language is not null) request.Headers.Add("Accept-Language", language);

        var response = await client.SendAsync(request);
        await EnsureSuccessAsync(response);

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task TheSectionIsReadableWithoutSigningIn()
    {
        var landing = await LandingAsync(CreateClient());

        landing.GetProperty("ctaTitle").GetString().Should().NotBeNullOrWhiteSpace();
        landing.GetProperty("ctaBody").GetString().Should().NotBeNullOrWhiteSpace();
        landing.GetProperty("ctaButton").GetString().Should().NotBeNullOrWhiteSpace();
        landing.GetProperty("ctaLink").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task TheCopyFollowsTheRequestedLanguage()
    {
        var english = await LandingAsync(CreateClient(), "en");
        var arabic = await LandingAsync(CreateClient(), "ar");

        english.GetProperty("ctaTitle").GetString()
            .Should().NotBe(arabic.GetProperty("ctaTitle").GetString());

        // The destination is not per language, so both see the same one.
        english.GetProperty("ctaLink").GetString()
            .Should().Be(arabic.GetProperty("ctaLink").GetString());
    }

    [Fact]
    public async Task ALocaleWithNoTranslationFallsBackToEnglish()
    {
        var english = await LandingAsync(CreateClient(), "en");
        var turkish = await LandingAsync(CreateClient(), "tr");

        turkish.GetProperty("ctaTitle").GetString()
            .Should().Be(english.GetProperty("ctaTitle").GetString());
    }

    [Fact]
    public async Task SavingTheCallToActionLeavesTheOtherHeadingsAlone()
    {
        var admin = CreateClient(await AdminTokenAsync());

        var before = await LandingAsync(CreateClient());
        var title = before.GetProperty("ctaTitle").GetString()!;
        var body = before.GetProperty("ctaBody").GetString()!;
        var button = before.GetProperty("ctaButton").GetString()!;
        var link = before.GetProperty("ctaLink").GetString()!;
        var howTitle = before.GetProperty("howTitle").GetString();

        try
        {
            await EnsureSuccessAsync(await admin.PutAsJsonAsync(AdminHeading, new
            {
                ctaTitle = new Dictionary<string, string> { ["en"] = "Verified at the source" },
                ctaBody = new Dictionary<string, string> { ["en"] = "One email is all it takes." },
                ctaButton = new Dictionary<string, string> { ["en"] = "Begin" },
                ctaLink = "/contact",
            }));

            var after = await LandingAsync(CreateClient());
            after.GetProperty("ctaTitle").GetString().Should().Be("Verified at the source");
            after.GetProperty("ctaBody").GetString().Should().Be("One email is all it takes.");
            after.GetProperty("ctaButton").GetString().Should().Be("Begin");
            after.GetProperty("ctaLink").GetString().Should().Be("/contact");

            // A page saves only the copy it owns; the steps heading is edited elsewhere.
            after.GetProperty("howTitle").GetString().Should().Be(howTitle);
        }
        finally
        {
            await admin.PutAsJsonAsync(AdminHeading, new
            {
                ctaTitle = new Dictionary<string, string> { ["en"] = title },
                ctaBody = new Dictionary<string, string> { ["en"] = body },
                ctaButton = new Dictionary<string, string> { ["en"] = button },
                ctaLink = link,
            });
        }
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("//evil.example.com")]
    [InlineData("")]
    public async Task AnUnsafeDestinationIsRefused(string link)
    {
        var admin = CreateClient(await AdminTokenAsync());

        var response = await admin.PutAsJsonAsync(AdminHeading, new { ctaLink = link });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EditingTheCallToActionRequiresAnAdminSession()
    {
        var response = await CreateClient().PutAsJsonAsync(AdminHeading, new
        {
            ctaTitle = new Dictionary<string, string> { ["en"] = "Anything" },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
