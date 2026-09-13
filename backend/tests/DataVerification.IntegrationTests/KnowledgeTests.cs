using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// The search box above the tools and guides: the database search behind "DQ Knowledge", and the
/// assistant behind "AI Mode".
///
/// The rule worth pinning on the AI side is that it stays silent rather than failing when no key
/// has been configured. A visitor cannot fix a missing API key, so the endpoint reports the state
/// and the site explains it — 500s and provider errors would say nothing anyone could act on.
/// </summary>
public sealed class KnowledgeTests : ApiTestBase
{
    public KnowledgeTests(ApiFactory factory) : base(factory) { }

    private static string Search(string q) => Url($"/content/knowledge/search?q={Uri.EscapeDataString(q)}");
    private static string Ask => Url("/content/knowledge/ask");
    private static string AdminTools => Url("/admin/content/tools");

    private static async Task<JsonElement> SearchAsync(HttpClient client, string q, string? language = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Search(q));
        if (language is not null) request.Headers.Add("Accept-Language", language);

        var response = await client.SendAsync(request);
        await EnsureSuccessAsync(response);

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>A published video guide, which is the cheapest showable entry to make.</summary>
    private static async Task<Guid> AddGuideAsync(
        HttpClient admin,
        string name,
        string description,
        string? nameAr = null)
    {
        var names = new Dictionary<string, string> { ["en"] = name };
        var descriptions = new Dictionary<string, string> { ["en"] = description };
        if (nameAr is not null) names["ar"] = nameAr;

        var response = await admin.PostAsJsonAsync(AdminTools, new
        {
            kind = "Video",
            videoUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            names,
            descriptions,
            sortOrder = 900,
            isPublished = true,
        });

        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static List<string?> Names(JsonElement results) =>
        results.EnumerateArray().Select(r => r.GetProperty("name").GetString()).ToList();

    [Fact]
    public async Task SearchingIsPossibleWithoutSigningIn()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var id = await AddGuideAsync(admin, "Uploading your certificate", "Drag the file onto the box.");

        try
        {
            Names(await SearchAsync(CreateClient(), "certificate"))
                .Should().Contain("Uploading your certificate");
        }
        finally
        {
            await admin.DeleteAsync($"{AdminTools}/{id}");
        }
    }

    [Fact]
    public async Task ASearchMatchesTheDescriptionAndReturnsTheWordsAroundIt()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var id = await AddGuideAsync(
            admin,
            "Paying for an application",
            "Top up your wallet first, then pay for as many applications as you like in one go.");

        try
        {
            var hit = (await SearchAsync(CreateClient(), "wallet")).EnumerateArray()
                .First(r => r.GetProperty("name").GetString() == "Paying for an application");

            // The snippet is why this row came back, not a truncation of the description.
            hit.GetProperty("snippet").GetString().Should().Contain("wallet");
        }
        finally
        {
            await admin.DeleteAsync($"{AdminTools}/{id}");
        }
    }

    [Fact]
    public async Task ASearchIgnoresCase()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var id = await AddGuideAsync(admin, "Attestation basics", "What an attested document is.");

        try
        {
            Names(await SearchAsync(CreateClient(), "ATTESTATION")).Should().Contain("Attestation basics");
            Names(await SearchAsync(CreateClient(), "attestation")).Should().Contain("Attestation basics");
        }
        finally
        {
            await admin.DeleteAsync($"{AdminTools}/{id}");
        }
    }

    [Fact]
    public async Task AnUnpublishedGuideIsNotSearchable()
    {
        var admin = CreateClient(await AdminTokenAsync());

        var created = await admin.PostAsJsonAsync(AdminTools, new
        {
            kind = "Video",
            videoUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            names = new Dictionary<string, string> { ["en"] = "Draft guide" },
            descriptions = new Dictionary<string, string> { ["en"] = "Secret internal note." },
            sortOrder = 901,
            isPublished = false,
        });

        await EnsureSuccessAsync(created);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        try
        {
            // Search must not become a way around publishing.
            Names(await SearchAsync(CreateClient(), "Secret internal")).Should().NotContain("Draft guide");
        }
        finally
        {
            await admin.DeleteAsync($"{AdminTools}/{id}");
        }
    }

    [Fact]
    public async Task AGuideIsFoundByItsNameInTheRequestedLanguage()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var id = await AddGuideAsync(
            admin,
            "Wallet top-ups",
            "How to add funds.",
            nameAr: "شحن المحفظة");

        try
        {
            Names(await SearchAsync(CreateClient(), "المحفظة", "ar")).Should().Contain("شحن المحفظة");

            // The Arabic name is not reachable from the English page, which is the same rule the
            // rest of the site follows.
            Names(await SearchAsync(CreateClient(), "المحفظة", "en")).Should().NotContain("شحن المحفظة");
        }
        finally
        {
            await admin.DeleteAsync($"{AdminTools}/{id}");
        }
    }

    [Fact]
    public async Task AnEmptySearchIsRefused()
    {
        var response = await CreateClient().GetAsync(Search("   "));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AskingSaysSoWhenNoKeyIsConfigured()
    {
        var response = await CreateClient().PostAsJsonAsync(Ask, new { question = "How do I pay?" });
        await EnsureSuccessAsync(response);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // A visitor cannot fix a missing key, so this is a state the site explains rather than an
        // error it has to swallow.
        body.GetProperty("isConfigured").GetBoolean().Should().BeFalse();
        body.GetProperty("answer").GetString().Should().BeEmpty();
    }

    [Fact]
    public async Task AQuestionTooLongToBeOneIsRefused()
    {
        var response = await CreateClient().PostAsJsonAsync(
            Ask,
            new { question = new string('a', 501) });

        // The endpoint spends the operator's provider quota, so it does not take documents.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TheAssistantSettingsRequireAnAdminSession()
    {
        var anonymous = CreateClient();

        (await anonymous.GetAsync(Url("/admin/settings/ai")))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await anonymous.PutAsJsonAsync(Url("/admin/settings/ai"), new
        {
            isEnabled = true,
            model = "gemini-3.6-flash",
            apiKey = "stolen",
        })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TheStoredKeyIsNeverReadBack()
    {
        var admin = CreateClient(await AdminTokenAsync());

        var before = await admin.GetAsync(Url("/admin/settings/ai"));
        await EnsureSuccessAsync(before);

        // Storing a key needs the platform's own encryption key. Without it the panel is told so
        // and the save is refused — which is the behaviour, not a reason to skip the rest.
        var canStore = (await before.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("canStore").GetBoolean();

        var saved = await admin.PutAsJsonAsync(Url("/admin/settings/ai"), new
        {
            isEnabled = false,
            model = "gemini-3.6-flash",
            apiKey = "AIzaSyTest-not-a-real-key",
        });

        if (!canStore)
        {
            saved.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            return;
        }

        await EnsureSuccessAsync(saved);

        var response = await admin.GetAsync(Url("/admin/settings/ai"));
        await EnsureSuccessAsync(response);

        var body = await response.Content.ReadAsStringAsync();

        // The panel is told a key exists and nothing more; the value must not be in the payload.
        body.Should().NotContain("AIzaSyTest");
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("hasApiKey").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task AStoredKeyCanBeRemovedAgain()
    {
        var admin = CreateClient(await AdminTokenAsync());

        var before = await admin.GetAsync(Url("/admin/settings/ai"));
        await EnsureSuccessAsync(before);

        var canStore = (await before.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("canStore").GetBoolean();

        if (!canStore) return;

        await EnsureSuccessAsync(await admin.PutAsJsonAsync(Url("/admin/settings/ai"), new
        {
            isEnabled = false,
            model = "gemini-3.6-flash",
            apiKey = "AIzaSyRemovable",
        }));

        // A secret that cannot be taken back out is worse than one that can.
        var cleared = await admin.PutAsJsonAsync(Url("/admin/settings/ai"), new
        {
            isEnabled = false,
            model = "gemini-3.6-flash",
            apiKey = (string?)null,
            clearApiKey = true,
        });

        await EnsureSuccessAsync(cleared);
        (await cleared.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("hasApiKey").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task TurningTheAssistantOnWithoutAKeyIsRefused()
    {
        var admin = CreateClient(await AdminTokenAsync());

        // Deliberately not using the seeded state: this asks for "on" with no key in the request.
        var response = await admin.PutAsJsonAsync(Url("/admin/settings/ai"), new
        {
            isEnabled = true,
            model = "gemini-3.6-flash",
            apiKey = "",
        });

        // Either it is refused, or a key was already stored and it is allowed — both are correct,
        // and which one applies depends on whether an earlier test stored one.
        if (response.StatusCode == HttpStatusCode.OK)
        {
            (await response.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("hasApiKey").GetBoolean()
                .Should().BeTrue("it can only be switched on when a key is already stored");
        }
        else
        {
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }
}
