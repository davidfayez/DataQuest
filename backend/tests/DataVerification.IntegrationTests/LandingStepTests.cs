using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// The "how it works" steps on the landing page, managed from the admin panel.
///
/// The rule worth pinning beyond the usual publish/fallback behaviour: the number on each card is
/// never stored. It comes from the step's place in the published order, so reordering renumbers
/// the set without an operator editing any copy.
/// </summary>
public sealed class LandingStepTests : ApiTestBase
{
    public LandingStepTests(ApiFactory factory) : base(factory) { }

    private static string AdminSteps => Url("/admin/content/landing/steps");
    private static string AdminHeading => Url("/admin/content/landing/heading");

    private static async Task<Guid> CreateStepAsync(
        HttpClient admin,
        string titleEn,
        string bodyEn,
        int sortOrder = 900,
        bool published = true,
        string icon = "mail",
        Dictionary<string, string>? extraTitles = null,
        Dictionary<string, string>? extraBodies = null)
    {
        var titles = new Dictionary<string, string> { ["en"] = titleEn };
        var bodies = new Dictionary<string, string> { ["en"] = bodyEn };

        foreach (var (k, v) in extraTitles ?? []) titles[k] = v;
        foreach (var (k, v) in extraBodies ?? []) bodies[k] = v;

        var response = await admin.PostAsJsonAsync(AdminSteps, new
        {
            icon,
            titles,
            bodies,
            sortOrder,
            isPublished = published,
        });

        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> LandingAsync(HttpClient client, string? language = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Url("/content/landing"));
        if (language is not null) request.Headers.Add("Accept-Language", language);

        var response = await client.SendAsync(request);
        await EnsureSuccessAsync(response);

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task TheStepsAreReadableWithoutSigningIn()
    {
        var landing = await LandingAsync(CreateClient());

        landing.GetProperty("howEyebrow").GetString().Should().NotBeNullOrWhiteSpace();
        landing.GetProperty("howTitle").GetString().Should().NotBeNullOrWhiteSpace();
        landing.GetProperty("steps").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task APublishedStepReachesThePublicPage()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var id = await CreateStepAsync(admin, "Sign the register", "Then wait.", icon: "file");

        try
        {
            var steps = (await LandingAsync(CreateClient())).GetProperty("steps");
            var match = steps.EnumerateArray()
                .FirstOrDefault(s => s.GetProperty("title").GetString() == "Sign the register");

            match.ValueKind.Should().NotBe(JsonValueKind.Undefined);
            match.GetProperty("body").GetString().Should().Be("Then wait.");
            match.GetProperty("icon").GetString().Should().Be("file");
        }
        finally
        {
            await admin.DeleteAsync($"{AdminSteps}/{id}");
        }
    }

    [Fact]
    public async Task AnUnpublishedStepIsNotShown()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var id = await CreateStepAsync(admin, "Hidden step", "Not for visitors.", published: false);

        try
        {
            var titles = (await LandingAsync(CreateClient())).GetProperty("steps").EnumerateArray()
                .Select(s => s.GetProperty("title").GetString()).ToList();

            titles.Should().NotContain("Hidden step");
        }
        finally
        {
            await admin.DeleteAsync($"{AdminSteps}/{id}");
        }
    }

    [Fact]
    public async Task StepsComeBackInTheOrderTheCardsAreNumberedBy()
    {
        var admin = CreateClient(await AdminTokenAsync());

        // Created out of order on purpose: the response must be sorted, because the site numbers
        // the cards by their position in it.
        var second = await CreateStepAsync(admin, "Second step", "b", sortOrder: 902);
        var first = await CreateStepAsync(admin, "First step", "a", sortOrder: 901);

        try
        {
            var ordered = (await LandingAsync(CreateClient())).GetProperty("steps").EnumerateArray()
                .Select(s => s.GetProperty("title").GetString())
                .Where(title => title is "First step" or "Second step")
                .ToList();

            ordered.Should().Equal("First step", "Second step");
        }
        finally
        {
            await admin.DeleteAsync($"{AdminSteps}/{first}");
            await admin.DeleteAsync($"{AdminSteps}/{second}");
        }
    }

    [Fact]
    public async Task ALocaleWithNoTranslationFallsBackToEnglish()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var id = await CreateStepAsync(admin, "English only step", "Body in English.");

        try
        {
            var titles = (await LandingAsync(CreateClient(), "tr")).GetProperty("steps").EnumerateArray()
                .Select(s => s.GetProperty("title").GetString()).ToList();

            titles.Should().Contain("English only step");
        }
        finally
        {
            await admin.DeleteAsync($"{AdminSteps}/{id}");
        }
    }

    [Fact]
    public async Task ATranslatedStepRendersInThatLanguage()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var id = await CreateStepAsync(
            admin,
            "Translated step",
            "English body.",
            extraTitles: new Dictionary<string, string> { ["ar"] = "خطوة مترجمة" },
            extraBodies: new Dictionary<string, string> { ["ar"] = "نص بالعربية." });

        try
        {
            var titles = (await LandingAsync(CreateClient(), "ar")).GetProperty("steps").EnumerateArray()
                .Select(s => s.GetProperty("title").GetString()).ToList();

            titles.Should().Contain("خطوة مترجمة");
            titles.Should().NotContain("Translated step");
        }
        finally
        {
            await admin.DeleteAsync($"{AdminSteps}/{id}");
        }
    }

    [Fact]
    public async Task AStepWithNoEnglishIsRefused()
    {
        var admin = CreateClient(await AdminTokenAsync());

        var response = await admin.PostAsJsonAsync(AdminSteps, new
        {
            icon = "mail",
            titles = new Dictionary<string, string> { ["ar"] = "بلا إنجليزية" },
            bodies = new Dictionary<string, string> { ["ar"] = "نص" },
            sortOrder = 0,
            isPublished = true,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SavingTheHowHeadingLeavesTheOtherHeadingsAlone()
    {
        var admin = CreateClient(await AdminTokenAsync());

        var before = await LandingAsync(CreateClient());
        var trust = before.GetProperty("trustTitle").GetString();
        var howTitle = before.GetProperty("howTitle").GetString();

        try
        {
            await EnsureSuccessAsync(await admin.PutAsJsonAsync(AdminHeading, new
            {
                howTitle = new Dictionary<string, string> { ["en"] = "Three simple steps" },
            }));

            var after = await LandingAsync(CreateClient());
            after.GetProperty("howTitle").GetString().Should().Be("Three simple steps");
            after.GetProperty("trustTitle").GetString().Should().Be(trust);
        }
        finally
        {
            await admin.PutAsJsonAsync(AdminHeading, new
            {
                howTitle = new Dictionary<string, string> { ["en"] = howTitle },
            });
        }
    }

    [Fact]
    public async Task TheArrangementDefaultsToWhatTheSectionLookedLikeBefore()
    {
        var landing = await LandingAsync(CreateClient());

        landing.GetProperty("howLayout").GetString().Should().BeOneOf("Grid", "Carousel");
        landing.GetProperty("howColumns").GetInt32().Should().BeInRange(1, 4);
    }

    [Fact]
    public async Task TheArrangementIsSavedAndPublished()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var layoutUrl = Url("/admin/content/landing/steps/layout");

        try
        {
            await EnsureSuccessAsync(
                await admin.PutAsJsonAsync(layoutUrl, new { layout = "Carousel", columns = 2 }));

            var landing = await LandingAsync(CreateClient());
            landing.GetProperty("howLayout").GetString().Should().Be("Carousel");
            landing.GetProperty("howColumns").GetInt32().Should().Be(2);
        }
        finally
        {
            await admin.PutAsJsonAsync(layoutUrl, new { layout = "Grid", columns = 3 });
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task AnImpossibleColumnCountIsRefused(int columns)
    {
        var admin = CreateClient(await AdminTokenAsync());

        // Past four there is no room for the body text under each card, and zero is not a layout.
        var response = await admin.PutAsJsonAsync(
            Url("/admin/content/landing/steps/layout"),
            new { layout = "Grid", columns });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ChangingTheArrangementRequiresAnAdminSession()
    {
        var response = await CreateClient().PutAsJsonAsync(
            Url("/admin/content/landing/steps/layout"),
            new { layout = "Carousel", columns = 3 });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EditingTheStepsRequiresAnAdminSession()
    {
        var response = await CreateClient().PostAsJsonAsync(AdminSteps, new
        {
            icon = "mail",
            titles = new Dictionary<string, string> { ["en"] = "Anyone" },
            bodies = new Dictionary<string, string> { ["en"] = "Anything" },
            sortOrder = 0,
            isPublished = true,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
