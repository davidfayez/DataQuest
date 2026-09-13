using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// The site footer's columns, headings, subtitle and marks, managed from the admin panel.
///
/// The rule worth pinning beyond the usual publish/fallback behaviour: every row carries who it is
/// shown to, and the response is anonymous — the visibility travels to the browser and is filtered
/// there, because a per-session footer could not be cached.
/// </summary>
public sealed class FooterContentTests : ApiTestBase
{
    private const string Explore = "Explore";
    private const string Account = "Account";
    private const string Organisation = "Organisation";

    public FooterContentTests(ApiFactory factory) : base(factory) { }

    private static string PublicFooter => Url("/content/footer");
    private static string AdminLinks => Url("/admin/content/footer/links");
    private static string AdminLogos => Url("/admin/content/footer/logos");
    private static string AdminHeadings => Url("/admin/content/footer/headings");

    /// <summary>A real 1x1 PNG, so the signature check sees genuine PNG bytes.</summary>
    private static byte[] Png() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static async Task<JsonElement> FooterAsync(HttpClient client, string? language = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, PublicFooter);
        if (language is not null) request.Headers.Add("Accept-Language", language);

        var response = await client.SendAsync(request);
        await EnsureSuccessAsync(response);

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<Guid> CreateLinkAsync(
        HttpClient admin,
        string column,
        string labelEn,
        string url,
        string visibility = "Everyone",
        bool active = true,
        Dictionary<string, string>? extraLabels = null)
    {
        var labels = new Dictionary<string, string> { ["en"] = labelEn };
        foreach (var (k, v) in extraLabels ?? []) labels[k] = v;

        var response = await admin.PostAsJsonAsync(AdminLinks, new
        {
            column,
            visibility,
            labels,
            url,
            sortOrder = 900,
            isActive = active,
        });

        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task TheFooterIsReadableWithoutSigningIn()
    {
        var footer = await FooterAsync(CreateClient());

        footer.GetProperty("subtitle").GetString().Should().NotBeNullOrWhiteSpace();
        footer.GetProperty("exploreHeading").GetString().Should().NotBeNullOrWhiteSpace();

        // Seeded, so the footer is not empty on a fresh database.
        footer.GetProperty("explore").GetArrayLength().Should().BeGreaterThan(0);
        footer.GetProperty("account").GetArrayLength().Should().BeGreaterThan(0);
        footer.GetProperty("organisation").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ALinkReachesItsOwnColumnOnly()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var id = await CreateLinkAsync(admin, Organisation, "Partner site", "https://example.com/partner");

        try
        {
            var footer = await FooterAsync(CreateClient());

            Labels(footer, "organisation").Should().Contain("Partner site");
            Labels(footer, "explore").Should().NotContain("Partner site");
        }
        finally
        {
            await admin.DeleteAsync($"{AdminLinks}/{id}");
        }
    }

    [Fact]
    public async Task AnInactiveLinkIsNotShown()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var id = await CreateLinkAsync(admin, Explore, "Hidden link", "/hidden", active: false);

        try
        {
            Labels(await FooterAsync(CreateClient()), "explore").Should().NotContain("Hidden link");
        }
        finally
        {
            await admin.DeleteAsync($"{AdminLinks}/{id}");
        }
    }

    [Fact]
    public async Task VisibilityTravelsToTheBrowserRatherThanFilteringOnTheServer()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var id = await CreateLinkAsync(
            admin, Account, "Only for members", "/wallet", visibility: "SignedIn");

        try
        {
            // The response is anonymous and cached, so the row is present with its visibility and
            // the site drops it. Filtering here would make the footer uncacheable.
            var row = (await FooterAsync(CreateClient())).GetProperty("account").EnumerateArray()
                .FirstOrDefault(l => l.GetProperty("label").GetString() == "Only for members");

            row.ValueKind.Should().NotBe(JsonValueKind.Undefined);
            row.GetProperty("visibility").GetString().Should().Be("SignedIn");
        }
        finally
        {
            await admin.DeleteAsync($"{AdminLinks}/{id}");
        }
    }

    [Fact]
    public async Task ALocaleWithNoTranslationFallsBackToEnglish()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var id = await CreateLinkAsync(admin, Explore, "English only link", "/english-only");

        try
        {
            Labels(await FooterAsync(CreateClient(), "tr"), "explore").Should().Contain("English only link");
        }
        finally
        {
            await admin.DeleteAsync($"{AdminLinks}/{id}");
        }
    }

    [Fact]
    public async Task ATranslatedLinkRendersInThatLanguage()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var id = await CreateLinkAsync(
            admin,
            Explore,
            "Translated link",
            "/translated",
            extraLabels: new Dictionary<string, string> { ["ar"] = "رابط مترجم" });

        try
        {
            var labels = Labels(await FooterAsync(CreateClient(), "ar"), "explore");
            labels.Should().Contain("رابط مترجم");
            labels.Should().NotContain("Translated link");
        }
        finally
        {
            await admin.DeleteAsync($"{AdminLinks}/{id}");
        }
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("//evil.example.com")]
    [InlineData("")]
    public async Task AnUnsafeAddressIsRefused(string url)
    {
        var admin = CreateClient(await AdminTokenAsync());

        // A footer link is clicked without being read, which is exactly where these do damage.
        // "//host" is protocol-relative and leaves the site, so it is not an in-app path either.
        var response = await admin.PostAsJsonAsync(AdminLinks, new
        {
            column = Explore,
            visibility = "Everyone",
            labels = new Dictionary<string, string> { ["en"] = "Bad" },
            url,
            sortOrder = 0,
            isActive = true,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AnAppPathAnAnchorAndAnHttpsAddressAreAllAccepted()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var created = new List<Guid>();

        try
        {
            foreach (var url in new[] { "/tools", "#services", "https://example.com" })
            {
                created.Add(await CreateLinkAsync(admin, Explore, $"Link {url}", url));
            }

            var urls = (await FooterAsync(CreateClient())).GetProperty("explore").EnumerateArray()
                .Select(l => l.GetProperty("url").GetString()).ToList();

            urls.Should().Contain(["/tools", "#services", "https://example.com"]);
        }
        finally
        {
            foreach (var id in created) await admin.DeleteAsync($"{AdminLinks}/{id}");
        }
    }

    [Fact]
    public async Task AMarkWithNoImageYetIsNotPublished()
    {
        var admin = CreateClient(await AdminTokenAsync());

        var created = await admin.PostAsJsonAsync(AdminLogos, new
        {
            alts = new Dictionary<string, string> { ["en"] = "Awaiting artwork" },
            url = (string?)null,
            sortOrder = 900,
            isActive = true,
        });

        await EnsureSuccessAsync(created);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();

        try
        {
            body.GetProperty("hasImage").GetBoolean().Should().BeFalse();
            body.GetProperty("isShowable").GetBoolean().Should().BeFalse();

            // A row exists before its file does; the site must not draw a broken image.
            (await FooterAsync(CreateClient())).GetProperty("logos").EnumerateArray()
                .Select(l => l.GetProperty("alt").GetString())
                .Should().NotContain("Awaiting artwork");
        }
        finally
        {
            await admin.DeleteAsync($"{AdminLogos}/{id}");
        }
    }

    [Fact]
    public async Task AnUploadedMarkIsPublishedAndServed()
    {
        var admin = CreateClient(await AdminTokenAsync());

        var created = await admin.PostAsJsonAsync(AdminLogos, new
        {
            alts = new Dictionary<string, string> { ["en"] = "Test accreditation" },
            url = "https://example.com/partner",
            sortOrder = 901,
            isActive = true,
        });

        await EnsureSuccessAsync(created);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        try
        {
            using var content = new MultipartFormDataContent();
            var file = new ByteArrayContent(Png());
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            content.Add(file, "file", "mark.png");

            await EnsureSuccessAsync(await admin.PostAsync($"{AdminLogos}/{id}/image", content));

            var mark = (await FooterAsync(CreateClient())).GetProperty("logos").EnumerateArray()
                .FirstOrDefault(l => l.GetProperty("alt").GetString() == "Test accreditation");

            mark.ValueKind.Should().NotBe(JsonValueKind.Undefined);
            mark.GetProperty("url").GetString().Should().Be("https://example.com/partner");

            var image = await CreateClient().GetAsync(Url($"/content/footer/logos/{id}/image"));
            await EnsureSuccessAsync(image);
            image.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
            (await image.Content.ReadAsByteArrayAsync()).Should().Equal(Png());
        }
        finally
        {
            await admin.DeleteAsync($"{AdminLogos}/{id}");
        }
    }

    [Fact]
    public async Task AMarkNeedsNoAlternativeText()
    {
        var admin = CreateClient(await AdminTokenAsync());

        // The panel asks only for the file. A mark sits beside the subtitle that already says what
        // the organisation does, so it carries no meaning of its own and an empty alt is correct.
        var created = await admin.PostAsJsonAsync(AdminLogos, new
        {
            sortOrder = 902,
            isActive = true,
        });

        await EnsureSuccessAsync(created);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        try
        {
            using var content = new MultipartFormDataContent();
            var file = new ByteArrayContent(Png());
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            content.Add(file, "file", "unlabelled.png");

            await EnsureSuccessAsync(await admin.PostAsync($"{AdminLogos}/{id}/image", content));

            var mark = (await FooterAsync(CreateClient())).GetProperty("logos").EnumerateArray()
                .FirstOrDefault(l => l.GetProperty("id").GetGuid() == id);

            mark.ValueKind.Should().NotBe(JsonValueKind.Undefined);
            mark.GetProperty("alt").GetString().Should().BeEmpty();
        }
        finally
        {
            await admin.DeleteAsync($"{AdminLogos}/{id}");
        }
    }

    [Fact]
    public async Task SavingOneHeadingLeavesTheOthersAlone()
    {
        var admin = CreateClient(await AdminTokenAsync());

        var before = await FooterAsync(CreateClient());
        var subtitle = before.GetProperty("subtitle").GetString();
        var explore = before.GetProperty("exploreHeading").GetString();

        try
        {
            await EnsureSuccessAsync(await admin.PutAsJsonAsync(AdminHeadings, new
            {
                exploreHeading = new Dictionary<string, string> { ["en"] = "Discover" },
            }));

            var after = await FooterAsync(CreateClient());
            after.GetProperty("exploreHeading").GetString().Should().Be("Discover");
            after.GetProperty("subtitle").GetString().Should().Be(subtitle);
        }
        finally
        {
            await admin.PutAsJsonAsync(AdminHeadings, new
            {
                exploreHeading = new Dictionary<string, string> { ["en"] = explore },
            });
        }
    }

    [Fact]
    public async Task EditingTheFooterRequiresAnAdminSession()
    {
        var anonymous = CreateClient();

        (await anonymous.PostAsJsonAsync(AdminLinks, new
        {
            column = Explore,
            visibility = "Everyone",
            labels = new Dictionary<string, string> { ["en"] = "Anyone" },
            url = "/anywhere",
            sortOrder = 0,
            isActive = true,
        })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await anonymous.PutAsJsonAsync(AdminHeadings, new
        {
            subtitle = new Dictionary<string, string> { ["en"] = "Anything" },
        })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static List<string?> Labels(JsonElement footer, string column) =>
        footer.GetProperty(column).EnumerateArray()
            .Select(l => l.GetProperty("label").GetString())
            .ToList();
}
