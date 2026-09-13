using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// The landing page's coverage section: its heading, and the countries marked on the map.
///
/// The rule worth pinning beyond the usual publish/fallback behaviour is that a coverage row is a
/// pointer at a country, not a copy of one. Its name comes from the lookup in whatever language was
/// asked for, its ISO code is what places the spot, and a country the platform has switched off
/// cannot go on claiming coverage.
/// </summary>
public sealed class CoverageContentTests : ApiTestBase
{
    public CoverageContentTests(ApiFactory factory) : base(factory) { }

    private static string Public => Url("/content/coverage");
    private static string AdminCountries => Url("/admin/content/coverage/countries");
    private static string AdminHeading => Url("/admin/content/coverage/heading");

    private static async Task<JsonElement> CoverageAsync(HttpClient client, string? language = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Public);
        if (language is not null) request.Headers.Add("Accept-Language", language);

        var response = await client.SendAsync(request);
        await EnsureSuccessAsync(response);

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>A country the seed does not already put on the map, so tests do not collide.</summary>
    private async Task<(Guid Id, string Code)> SpareCountryAsync(HttpClient admin)
    {
        var onMap = (await CoverageAsync(CreateClient())).GetProperty("countries").EnumerateArray()
            .Select(c => c.GetProperty("code").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var response = await admin.GetAsync(
            Url("/admin/lookups/countries?page=1&pageSize=300&isActive=true"));
        await EnsureSuccessAsync(response);

        var country = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray()
            .First(c => !onMap.Contains(c.GetProperty("code").GetString()!));

        return (country.GetProperty("id").GetGuid(), country.GetProperty("code").GetString()!);
    }

    private static async Task<Guid> AddAsync(
        HttpClient admin,
        Guid countryId,
        bool published = true,
        int sortOrder = 900,
        string marker = "Spot")
    {
        var response = await admin.PostAsJsonAsync(AdminCountries, new
        {
            countryId,
            marker,
            sortOrder,
            isPublished = published,
        });

        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static List<string?> Codes(JsonElement coverage) =>
        coverage.GetProperty("countries").EnumerateArray()
            .Select(c => c.GetProperty("code").GetString())
            .ToList();

    [Fact]
    public async Task TheSectionIsReadableWithoutSigningIn()
    {
        var coverage = await CoverageAsync(CreateClient());

        coverage.GetProperty("title").GetString().Should().NotBeNullOrWhiteSpace();
        coverage.GetProperty("subtitle").GetString().Should().NotBeNullOrWhiteSpace();

        // Seeded, so the map is not empty on a fresh database.
        coverage.GetProperty("countries").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task EveryCountryCarriesTheIsoCodeThatPlacesItsSpot()
    {
        var countries = (await CoverageAsync(CreateClient())).GetProperty("countries");

        foreach (var country in countries.EnumerateArray())
        {
            // The site holds the world as one SVG path per alpha-2 code; anything else cannot be
            // drawn, so the shape of this field is the contract.
            country.GetProperty("code").GetString().Should().MatchRegex("^[A-Z]{2}$");
            country.GetProperty("name").GetString().Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task ACountryReachesTheMapAndCanBeTakenOffAgain()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var (countryId, code) = await SpareCountryAsync(admin);
        var id = await AddAsync(admin, countryId);

        try
        {
            Codes(await CoverageAsync(CreateClient())).Should().Contain(code);
        }
        finally
        {
            await admin.DeleteAsync($"{AdminCountries}/{id}");
        }

        Codes(await CoverageAsync(CreateClient())).Should().NotContain(code);
    }

    [Fact]
    public async Task AnUnpublishedCountryIsNotShown()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var (countryId, code) = await SpareCountryAsync(admin);
        var id = await AddAsync(admin, countryId, published: false);

        try
        {
            Codes(await CoverageAsync(CreateClient())).Should().NotContain(code);
        }
        finally
        {
            await admin.DeleteAsync($"{AdminCountries}/{id}");
        }
    }

    [Fact]
    public async Task TheSameCountryCannotBeAddedTwice()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var (countryId, _) = await SpareCountryAsync(admin);
        var id = await AddAsync(admin, countryId);

        try
        {
            // Two rows for one country would draw the same spot twice and read as a duplicate in
            // the list beside the map.
            var second = await admin.PostAsJsonAsync(AdminCountries, new
            {
                countryId,
                marker = "Spot",
                sortOrder = 901,
                isPublished = true,
            });

            second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
        finally
        {
            await admin.DeleteAsync($"{AdminCountries}/{id}");
        }
    }

    [Theory]
    [InlineData("Spot")]
    [InlineData("Flag")]
    public async Task EachCountryCarriesTheMarkerItWasGiven(string marker)
    {
        var admin = CreateClient(await AdminTokenAsync());
        var (countryId, code) = await SpareCountryAsync(admin);
        var id = await AddAsync(admin, countryId, marker: marker);

        try
        {
            var country = (await CoverageAsync(CreateClient())).GetProperty("countries")
                .EnumerateArray()
                .First(c => c.GetProperty("code").GetString() == code);

            // A name, not a number: the site switches on it to draw a pin or the country's flag.
            country.GetProperty("marker").GetString().Should().Be(marker);
        }
        finally
        {
            await admin.DeleteAsync($"{AdminCountries}/{id}");
        }
    }

    [Fact]
    public async Task AMarkerTheMapCannotDrawIsRefused()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var (countryId, _) = await SpareCountryAsync(admin);

        var response = await admin.PostAsJsonAsync(AdminCountries, new
        {
            countryId,
            marker = "Sparkles",
            sortOrder = 0,
            isPublished = true,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ACountryNameFollowsTheRequestedLanguage()
    {
        var english = await CoverageAsync(CreateClient(), "en");
        var arabic = await CoverageAsync(CreateClient(), "ar");

        var egyptEn = english.GetProperty("countries").EnumerateArray()
            .FirstOrDefault(c => c.GetProperty("code").GetString() == "EG");
        var egyptAr = arabic.GetProperty("countries").EnumerateArray()
            .FirstOrDefault(c => c.GetProperty("code").GetString() == "EG");

        egyptEn.ValueKind.Should().NotBe(JsonValueKind.Undefined, "Egypt is seeded onto the map");

        // The name is the lookup's own, so it is already translated — nothing is copied here.
        egyptEn.GetProperty("name").GetString().Should().Be("Egypt");
        egyptAr.GetProperty("name").GetString().Should().Be("مصر");
    }

    [Fact]
    public async Task TheHeadingFollowsTheRequestedLanguage()
    {
        var english = await CoverageAsync(CreateClient(), "en");
        var arabic = await CoverageAsync(CreateClient(), "ar");

        english.GetProperty("title").GetString()
            .Should().NotBe(arabic.GetProperty("title").GetString());
    }

    [Fact]
    public async Task ALocaleWithNoTranslationFallsBackToEnglish()
    {
        var english = await CoverageAsync(CreateClient(), "en");
        var turkish = await CoverageAsync(CreateClient(), "tr");

        turkish.GetProperty("title").GetString()
            .Should().Be(english.GetProperty("title").GetString());
    }

    [Fact]
    public async Task SavingTheTitleLeavesTheSubtitleAlone()
    {
        var admin = CreateClient(await AdminTokenAsync());

        var before = await CoverageAsync(CreateClient());
        var title = before.GetProperty("title").GetString();
        var subtitle = before.GetProperty("subtitle").GetString();

        try
        {
            await EnsureSuccessAsync(await admin.PutAsJsonAsync(AdminHeading, new
            {
                title = new Dictionary<string, string> { ["en"] = "Coverage, worldwide" },
            }));

            var after = await CoverageAsync(CreateClient());
            after.GetProperty("title").GetString().Should().Be("Coverage, worldwide");
            after.GetProperty("subtitle").GetString().Should().Be(subtitle);
        }
        finally
        {
            await admin.PutAsJsonAsync(AdminHeading, new
            {
                title = new Dictionary<string, string> { ["en"] = title! },
            });
        }
    }

    [Fact]
    public async Task AnUnknownCountryIsRefused()
    {
        var admin = CreateClient(await AdminTokenAsync());

        var response = await admin.PostAsJsonAsync(AdminCountries, new
        {
            countryId = Guid.NewGuid(),
            marker = "Spot",
            sortOrder = 0,
            isPublished = true,
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task EditingCoverageRequiresAnAdminSession()
    {
        var anonymous = CreateClient();

        (await anonymous.PostAsJsonAsync(AdminCountries, new
        {
            countryId = SeedIds.Egypt,
            marker = "Spot",
            sortOrder = 0,
            isPublished = true,
        })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await anonymous.PutAsJsonAsync(AdminHeading, new
        {
            title = new Dictionary<string, string> { ["en"] = "Anything" },
        })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
