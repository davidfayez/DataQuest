using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// The "trusted for verification with" strip, managed from the admin panel.
///
/// It is marketing copy rather than the operational authority lookup, so what matters here is that
/// it reads anonymously, honours publishing and the English fallback, and that its heading is
/// editable without an older client blanking it by omission.
/// </summary>
public sealed class LandingTrustStripTests : ApiTestBase
{
    public LandingTrustStripTests(ApiFactory factory) : base(factory) { }

    private static string AdminTrust => Url("/admin/content/landing/trusted-by");
    private static string AdminHeading => Url("/admin/content/landing/heading");

    private static async Task<Guid> CreateEntryAsync(
        HttpClient admin,
        string nameEn,
        bool published = true,
        string? icon = null,
        Dictionary<string, string>? extraNames = null)
    {
        var names = new Dictionary<string, string> { ["en"] = nameEn };
        foreach (var (k, v) in extraNames ?? []) names[k] = v;

        var response = await admin.PostAsJsonAsync(AdminTrust, new
        {
            icon,
            names,
            sortOrder = 900,
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
    public async Task TheStripIsReadableWithoutSigningIn()
    {
        var landing = await LandingAsync(CreateClient());

        landing.GetProperty("trustTitle").GetString().Should().NotBeNullOrWhiteSpace();
        landing.GetProperty("trustedBy").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task APublishedBodyReachesThePublicPage()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var id = await CreateEntryAsync(admin, "Bureau of Testing", icon: "authority");

        try
        {
            var trusted = (await LandingAsync(CreateClient())).GetProperty("trustedBy");
            var match = trusted.EnumerateArray()
                .FirstOrDefault(e => e.GetProperty("name").GetString() == "Bureau of Testing");

            match.ValueKind.Should().NotBe(JsonValueKind.Undefined);
            match.GetProperty("icon").GetString().Should().Be("authority");
        }
        finally
        {
            await admin.DeleteAsync($"{AdminTrust}/{id}");
        }
    }

    [Fact]
    public async Task AnUnpublishedBodyIsNotShown()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var id = await CreateEntryAsync(admin, "Hidden Bureau", published: false);

        try
        {
            var trusted = (await LandingAsync(CreateClient())).GetProperty("trustedBy");
            var names = trusted.EnumerateArray().Select(e => e.GetProperty("name").GetString()).ToList();

            names.Should().NotContain("Hidden Bureau");
        }
        finally
        {
            await admin.DeleteAsync($"{AdminTrust}/{id}");
        }
    }

    [Fact]
    public async Task ALocaleWithNoTranslationFallsBackToEnglish()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var id = await CreateEntryAsync(admin, "English Only Bureau");

        try
        {
            var trusted = (await LandingAsync(CreateClient(), "tr")).GetProperty("trustedBy");
            var names = trusted.EnumerateArray().Select(e => e.GetProperty("name").GetString()).ToList();

            names.Should().Contain("English Only Bureau");
        }
        finally
        {
            await admin.DeleteAsync($"{AdminTrust}/{id}");
        }
    }

    [Fact]
    public async Task ATranslatedBodyRendersInThatLanguage()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var id = await CreateEntryAsync(
            admin,
            "Translated Bureau",
            extraNames: new Dictionary<string, string> { ["ar"] = "مكتب مترجم" });

        try
        {
            var trusted = (await LandingAsync(CreateClient(), "ar")).GetProperty("trustedBy");
            var names = trusted.EnumerateArray().Select(e => e.GetProperty("name").GetString()).ToList();

            names.Should().Contain("مكتب مترجم");
            names.Should().NotContain("Translated Bureau");
        }
        finally
        {
            await admin.DeleteAsync($"{AdminTrust}/{id}");
        }
    }

    [Fact]
    public async Task ABodyWithNoEnglishIsRefused()
    {
        var admin = CreateClient(await AdminTokenAsync());

        var response = await admin.PostAsJsonAsync(AdminTrust, new
        {
            icon = (string?)null,
            names = new Dictionary<string, string> { ["ar"] = "بلا إنجليزية" },
            sortOrder = 0,
            isPublished = true,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TheHeadingIsEditable()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var original = (await LandingAsync(CreateClient())).GetProperty("trustTitle").GetString();

        try
        {
            await EnsureSuccessAsync(await admin.PutAsJsonAsync(AdminHeading, new
            {
                eyebrow = new Dictionary<string, string> { ["en"] = "Built for peace of mind" },
                title = new Dictionary<string, string> { ["en"] = "Everything about your verification" },
                trustTitle = new Dictionary<string, string> { ["en"] = "Verified alongside" },
            }));

            (await LandingAsync(CreateClient())).GetProperty("trustTitle").GetString()
                .Should().Be("Verified alongside");
        }
        finally
        {
            await admin.PutAsJsonAsync(AdminHeading, new
            {
                eyebrow = new Dictionary<string, string> { ["en"] = "Built for peace of mind" },
                title = new Dictionary<string, string> { ["en"] = "Everything about your verification" },
                trustTitle = new Dictionary<string, string> { ["en"] = original },
            });
        }
    }

    [Fact]
    public async Task SavingOneHeadingLeavesTheOthersAlone()
    {
        var admin = CreateClient(await AdminTokenAsync());

        // Each heading is edited on the page of the section it labels, so a page sends only its
        // own copy. Sending the trust heading alone must not blank the features eyebrow beside it.
        var before = await LandingAsync(CreateClient());
        var eyebrow = before.GetProperty("eyebrow").GetString();
        var trust = before.GetProperty("trustTitle").GetString();

        try
        {
            await EnsureSuccessAsync(await admin.PutAsJsonAsync(AdminHeading, new
            {
                trustTitle = new Dictionary<string, string> { ["en"] = "Verified alongside" },
            }));

            var after = await LandingAsync(CreateClient());
            after.GetProperty("trustTitle").GetString().Should().Be("Verified alongside");
            after.GetProperty("eyebrow").GetString().Should().Be(eyebrow);
        }
        finally
        {
            await admin.PutAsJsonAsync(AdminHeading, new
            {
                trustTitle = new Dictionary<string, string> { ["en"] = trust },
            });
        }
    }

    [Fact]
    public async Task OmittingTheTrustHeadingLeavesItAlone()
    {
        var admin = CreateClient(await AdminTokenAsync());

        // An older client that knows nothing of this heading must not blank it by not sending it.
        var before = (await LandingAsync(CreateClient())).GetProperty("trustTitle").GetString();

        await EnsureSuccessAsync(await admin.PutAsJsonAsync(AdminHeading, new
        {
            eyebrow = new Dictionary<string, string> { ["en"] = "Built for peace of mind" },
            title = new Dictionary<string, string> { ["en"] = "Everything about your verification" },
        }));

        (await LandingAsync(CreateClient())).GetProperty("trustTitle").GetString().Should().Be(before);
    }

    [Fact]
    public async Task EditingTheStripRequiresAnAdminSession()
    {
        var response = await CreateClient().PostAsJsonAsync(AdminTrust, new
        {
            icon = (string?)null,
            names = new Dictionary<string, string> { ["en"] = "Anyone" },
            sortOrder = 0,
            isPublished = true,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
