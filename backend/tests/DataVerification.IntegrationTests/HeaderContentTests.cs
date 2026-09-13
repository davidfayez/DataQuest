using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// The site header: which entries it carries, in what order, and who sees each one.
///
/// Two rules are worth pinning beyond the usual save behaviour. The order the operator arranges is
/// the order the site is told to draw — nothing re-sorts it on the way out. And an entry the site
/// ships with reports its key and no label, because the web app already has that wording in ten
/// languages; sending English here would replace all of them.
/// </summary>
public sealed class HeaderContentTests : ApiTestBase
{
    public HeaderContentTests(ApiFactory factory) : base(factory) { }

    private static string Public => Url("/content/header");
    private static string AdminHeader => Url("/admin/content/header");
    private static string AdminLinks => Url("/admin/content/header/links");

    private static async Task<List<JsonElement>> LinksAsync(HttpClient client, string? language = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Public);
        if (language is not null) request.Headers.Add("Accept-Language", language);

        var response = await client.SendAsync(request);
        await EnsureSuccessAsync(response);

        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("links").EnumerateArray().ToList();
    }

    private static List<string?> Keys(IEnumerable<JsonElement> links) =>
        links.Select(l => l.GetProperty("key").GetString()).ToList();

    /// <summary>One seeded entry, read from the admin side so its stored shape is visible.</summary>
    private static async Task<JsonElement> StoredAsync(HttpClient admin, string key)
    {
        var response = await admin.GetAsync(AdminHeader);
        await EnsureSuccessAsync(response);

        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("links").EnumerateArray()
            .First(l => l.GetProperty("key").GetString() == key);
    }

    private static object Body(
        JsonElement stored,
        int? sortOrder = null,
        bool? isActive = null,
        Dictionary<string, string>? labels = null) => new
        {
            id = stored.GetProperty("id").GetGuid(),
            key = stored.GetProperty("key").GetString(),
            labels = labels ?? new Dictionary<string, string>(),
            url = stored.GetProperty("url").GetString(),
            visibility = stored.GetProperty("visibility").GetString(),
            sortOrder = sortOrder ?? stored.GetProperty("sortOrder").GetInt32(),
            isActive = isActive ?? stored.GetProperty("isActive").GetBoolean(),
        };

    [Fact]
    public async Task TheHeaderIsReadableWithoutSigningIn()
    {
        var links = await LinksAsync(CreateClient());

        // Seeded, so a fresh install has a header rather than an empty bar.
        Keys(links).Should().ContainInOrder("home", "how", "services", "coverage", "knowledge");
    }

    [Fact]
    public async Task ABuiltInEntryCarriesItsKeyAndNoLabelOfItsOwn()
    {
        var home = (await LinksAsync(CreateClient(), "tr")).First(l => l.GetProperty("key").GetString() == "home");

        // Null, not "Home": the Turkish bundle already has the word, and answering with English
        // would replace it.
        home.GetProperty("label").ValueKind.Should().Be(JsonValueKind.Null);
        home.GetProperty("url").GetString().Should().Be("/");
    }

    [Fact]
    public async Task VisibilityTravelsToTheBrowserRatherThanFilteringOnTheServer()
    {
        var links = await LinksAsync(CreateClient());

        // The response is fetched anonymously and shared by every reader, so the site does the
        // filtering. The marketing anchors ship as signed-out.
        links.First(l => l.GetProperty("key").GetString() == "how")
            .GetProperty("visibility").GetString().Should().Be("SignedOut");
    }

    [Fact]
    public async Task TheOperatorsOrderIsTheOrderTheSiteIsGiven()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var home = await StoredAsync(admin, "home");
        var contact = await StoredAsync(admin, "contact");

        try
        {
            // Swap the first and last entries.
            await EnsureSuccessAsync(await admin.PostAsJsonAsync(
                AdminLinks, Body(home, sortOrder: contact.GetProperty("sortOrder").GetInt32())));
            await EnsureSuccessAsync(await admin.PostAsJsonAsync(
                AdminLinks, Body(contact, sortOrder: home.GetProperty("sortOrder").GetInt32())));

            var keys = Keys(await LinksAsync(CreateClient()));
            keys.First().Should().Be("contact");
            keys.Last().Should().Be("home");
        }
        finally
        {
            await admin.PostAsJsonAsync(AdminLinks, Body(home));
            await admin.PostAsJsonAsync(AdminLinks, Body(contact));
        }

        Keys(await LinksAsync(CreateClient())).First().Should().Be("home");
    }

    [Fact]
    public async Task AHiddenEntryIsNotSent()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var contact = await StoredAsync(admin, "contact");

        try
        {
            await EnsureSuccessAsync(await admin.PostAsJsonAsync(
                AdminLinks, Body(contact, isActive: false)));

            Keys(await LinksAsync(CreateClient())).Should().NotContain("contact");
        }
        finally
        {
            await admin.PostAsJsonAsync(AdminLinks, Body(contact));
        }

        Keys(await LinksAsync(CreateClient())).Should().Contain("contact");
    }

    [Fact]
    public async Task AWrittenLabelOverridesTheAppsWordingInThatLanguageOnly()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var knowledge = await StoredAsync(admin, "knowledge");

        try
        {
            await EnsureSuccessAsync(await admin.PostAsJsonAsync(
                AdminLinks,
                Body(knowledge, labels: new Dictionary<string, string> { ["en"] = "Guides" })));

            var english = (await LinksAsync(CreateClient(), "en"))
                .First(l => l.GetProperty("key").GetString() == "knowledge");
            var arabic = (await LinksAsync(CreateClient(), "ar"))
                .First(l => l.GetProperty("key").GetString() == "knowledge");

            english.GetProperty("label").GetString().Should().Be("Guides");

            // Arabic was not written, so it keeps the app's own word rather than inheriting English.
            arabic.GetProperty("label").ValueKind.Should().Be(JsonValueKind.Null);
        }
        finally
        {
            await admin.PostAsJsonAsync(AdminLinks, Body(knowledge));
        }
    }

    [Fact]
    public async Task ClearingTheLabelHandsTheEntryBackToTheApp()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var services = await StoredAsync(admin, "services");

        await EnsureSuccessAsync(await admin.PostAsJsonAsync(
            AdminLinks, Body(services, labels: new Dictionary<string, string> { ["en"] = "Our services" })));

        await EnsureSuccessAsync(await admin.PostAsJsonAsync(
            AdminLinks, Body(services, labels: new Dictionary<string, string> { ["en"] = "  " })));

        (await LinksAsync(CreateClient(), "en"))
            .First(l => l.GetProperty("key").GetString() == "services")
            .GetProperty("label").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task AnEntryAnOperatorAddsMustBeLabelled()
    {
        var admin = CreateClient(await AdminTokenAsync());

        // No key means nothing to fall back to, so a blank label would render as a gap.
        var response = await admin.PostAsJsonAsync(AdminLinks, new
        {
            key = (string?)null,
            labels = new Dictionary<string, string>(),
            url = "/contact",
            visibility = "Everyone",
            sortOrder = 900,
            isActive = true,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AnEntryAnOperatorAddsReachesTheHeaderWithItsOwnLabel()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var label = $"Partners {Guid.NewGuid():N}";

        var created = await admin.PostAsJsonAsync(AdminLinks, new
        {
            key = (string?)null,
            labels = new Dictionary<string, string> { ["en"] = label },
            url = "https://example.com",
            visibility = "Everyone",
            sortOrder = 900,
            isActive = true,
        });

        await EnsureSuccessAsync(created);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        try
        {
            (await LinksAsync(CreateClient(), "en"))
                .Select(l => l.GetProperty("label").GetString())
                .Should().Contain(label);
        }
        finally
        {
            await admin.DeleteAsync($"{AdminLinks}/{id}");
        }

        (await LinksAsync(CreateClient(), "en"))
            .Select(l => l.GetProperty("label").GetString())
            .Should().NotContain(label);
    }

    [Fact]
    public async Task TwoEntriesCannotShareABuiltInKey()
    {
        var admin = CreateClient(await AdminTokenAsync());

        var response = await admin.PostAsJsonAsync(AdminLinks, new
        {
            key = "home",
            labels = new Dictionary<string, string>(),
            url = "/",
            visibility = "Everyone",
            sortOrder = 900,
            isActive = true,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("//evil.example.com")]
    [InlineData("")]
    public async Task AnUnsafeDestinationIsRefused(string url)
    {
        var admin = CreateClient(await AdminTokenAsync());

        var response = await admin.PostAsJsonAsync(AdminLinks, new
        {
            key = (string?)null,
            labels = new Dictionary<string, string> { ["en"] = "Somewhere" },
            url,
            visibility = "Everyone",
            sortOrder = 900,
            isActive = true,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EditingTheHeaderRequiresAnAdminSession()
    {
        var anonymous = CreateClient();

        (await anonymous.GetAsync(AdminHeader)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await anonymous.PostAsJsonAsync(AdminLinks, new
        {
            key = (string?)null,
            labels = new Dictionary<string, string> { ["en"] = "Anything" },
            url = "/contact",
            visibility = "Everyone",
            sortOrder = 900,
            isActive = true,
        })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
