using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// The statistics strip on the landing page, managed from the admin panel.
///
/// The public read is anonymous — the strip is on the marketing page — and it must return only
/// published figures, resolved to the caller's language with English as the fallback so an
/// untranslated locale never renders a blank card.
/// </summary>
public sealed class LandingStatTests : ApiTestBase
{
    public LandingStatTests(ApiFactory factory) : base(factory) { }

    private static string AdminStats => Url("/admin/content/landing/stats");

    private static async Task<Guid> CreateStatAsync(
        HttpClient admin,
        string valueEn,
        string labelEn,
        bool published = true,
        Dictionary<string, string>? extraValues = null,
        Dictionary<string, string>? extraLabels = null,
        string? icon = null)
    {
        var values = new Dictionary<string, string> { ["en"] = valueEn };
        var labels = new Dictionary<string, string> { ["en"] = labelEn };

        foreach (var (k, v) in extraValues ?? []) values[k] = v;
        foreach (var (k, v) in extraLabels ?? []) labels[k] = v;

        var response = await admin.PostAsJsonAsync(AdminStats, new
        {
            icon,
            values,
            labels,
            sortOrder = 900,
            isPublished = published,
        });

        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> PublicStatsAsync(HttpClient client, string? language = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Url("/content/landing"));
        if (language is not null) request.Headers.Add("Accept-Language", language);

        var response = await client.SendAsync(request);
        await EnsureSuccessAsync(response);

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("stats");
    }

    [Fact]
    public async Task TheStripIsReadableWithoutSigningIn()
    {
        var stats = await PublicStatsAsync(CreateClient());

        // Seeded, so a fresh database renders the strip rather than nothing.
        stats.GetArrayLength().Should().BeGreaterThan(0);

        var first = stats.EnumerateArray().First();
        first.GetProperty("value").GetString().Should().NotBeNullOrWhiteSpace();
        first.GetProperty("label").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task APublishedFigureReachesThePublicPage()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var id = await CreateStatAsync(admin, "99,999+", "widgets counted");

        try
        {
            var stats = await PublicStatsAsync(CreateClient());
            var values = stats.EnumerateArray().Select(s => s.GetProperty("value").GetString()).ToList();

            values.Should().Contain("99,999+");
        }
        finally
        {
            await admin.DeleteAsync($"{AdminStats}/{id}");
        }
    }

    [Fact]
    public async Task AnUnpublishedFigureIsNotShown()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var id = await CreateStatAsync(admin, "00,001+", "hidden figure", published: false);

        try
        {
            var stats = await PublicStatsAsync(CreateClient());
            var values = stats.EnumerateArray().Select(s => s.GetProperty("value").GetString()).ToList();

            values.Should().NotContain("00,001+");
        }
        finally
        {
            await admin.DeleteAsync($"{AdminStats}/{id}");
        }
    }

    [Fact]
    public async Task ALocaleWithNoTranslationFallsBackToEnglish()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var id = await CreateStatAsync(admin, "12,345+", "english only");

        try
        {
            // No Turkish translation was written, so English is what must render.
            var stats = await PublicStatsAsync(CreateClient(), "tr");
            var match = stats.EnumerateArray()
                .FirstOrDefault(s => s.GetProperty("value").GetString() == "12,345+");

            match.ValueKind.Should().NotBe(JsonValueKind.Undefined);
            match.GetProperty("label").GetString().Should().Be("english only");
        }
        finally
        {
            await admin.DeleteAsync($"{AdminStats}/{id}");
        }
    }

    [Fact]
    public async Task ATranslatedFigureRendersInThatLanguage()
    {
        var admin = CreateClient(await AdminTokenAsync());

        var id = await CreateStatAsync(
            admin,
            "5,000+",
            "in english",
            extraValues: new Dictionary<string, string> { ["ar"] = "٥٬٠٠٠+" },
            extraLabels: new Dictionary<string, string> { ["ar"] = "بالعربية" });

        try
        {
            var stats = await PublicStatsAsync(CreateClient(), "ar");
            var match = stats.EnumerateArray()
                .FirstOrDefault(s => s.GetProperty("label").GetString() == "بالعربية");

            match.ValueKind.Should().NotBe(JsonValueKind.Undefined);
            match.GetProperty("value").GetString().Should().Be("٥٬٠٠٠+");
        }
        finally
        {
            await admin.DeleteAsync($"{AdminStats}/{id}");
        }
    }

    [Fact]
    public async Task AChosenIconReachesThePublicPage()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var id = await CreateStatAsync(admin, "31,415+", "with an icon", icon: "authority");

        try
        {
            var stats = await PublicStatsAsync(CreateClient());
            var match = stats.EnumerateArray()
                .FirstOrDefault(s => s.GetProperty("value").GetString() == "31,415+");

            match.ValueKind.Should().NotBe(JsonValueKind.Undefined);
            match.GetProperty("icon").GetString().Should().Be("authority");
        }
        finally
        {
            await admin.DeleteAsync($"{AdminStats}/{id}");
        }
    }

    [Fact]
    public async Task AFigureWithNoIconReportsNullRatherThanBlank()
    {
        var admin = CreateClient(await AdminTokenAsync());

        // Blank and absent must arrive at the site as the same thing, or "no icon" would render
        // as an empty circle in one case and nothing in the other.
        var id = await CreateStatAsync(admin, "27,182+", "no icon", icon: "   ");

        try
        {
            var stats = await PublicStatsAsync(CreateClient());
            var match = stats.EnumerateArray()
                .FirstOrDefault(s => s.GetProperty("value").GetString() == "27,182+");

            match.ValueKind.Should().NotBe(JsonValueKind.Undefined);
            match.GetProperty("icon").ValueKind.Should().Be(JsonValueKind.Null);
        }
        finally
        {
            await admin.DeleteAsync($"{AdminStats}/{id}");
        }
    }

    [Fact]
    public async Task AFigureWithNoEnglishIsRefused()
    {
        var admin = CreateClient(await AdminTokenAsync());

        // English is the guaranteed fallback, so without it a locale could render blank.
        var response = await admin.PostAsJsonAsync(AdminStats, new
        {
            values = new Dictionary<string, string> { ["ar"] = "١٠٠+" },
            labels = new Dictionary<string, string> { ["ar"] = "بالعربية" },
            sortOrder = 0,
            isPublished = true,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EditingTheStripRequiresAnAdminSession()
    {
        var response = await CreateClient().PostAsJsonAsync(AdminStats, new
        {
            values = new Dictionary<string, string> { ["en"] = "1" },
            labels = new Dictionary<string, string> { ["en"] = "anything" },
            sortOrder = 0,
            isPublished = true,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
