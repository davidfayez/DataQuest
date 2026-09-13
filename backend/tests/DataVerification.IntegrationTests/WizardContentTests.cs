using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// The headings above the New application wizard's steps, which an administrator can rewrite.
///
/// The rule worth pinning beyond the usual save behaviour is the fallback. The marketing pages fall
/// back to English because there is nothing else to show; the wizard ships translated in every
/// locale the platform speaks, so a language nobody has written here answers with nothing at all and
/// the app keeps its own translation.
/// </summary>
public sealed class WizardContentTests : ApiTestBase
{
    public WizardContentTests(ApiFactory factory) : base(factory) { }

    private static string Public => Url("/content/wizard");
    private static string AdminWizard => Url("/admin/content/wizard");
    private static string AdminHeading => Url("/admin/content/wizard/heading");

    private static async Task<JsonElement> WizardAsync(HttpClient client, string? language = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Public);
        if (language is not null) request.Headers.Add("Accept-Language", language);

        var response = await client.SendAsync(request);
        await EnsureSuccessAsync(response);

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static JsonElement Step(JsonElement wizard, string step) =>
        wizard.GetProperty("steps").GetProperty(step);

    private static string? Title(JsonElement wizard, string step) =>
        Step(wizard, step).GetProperty("title").GetString();

    private static string? Subtitle(JsonElement wizard, string step) =>
        Step(wizard, step).GetProperty("subtitle").GetString();

    /// <summary>What is stored for a step in one language, straight from the admin read.</summary>
    private static async Task<(string? Title, string? Subtitle)> StoredAsync(
        HttpClient admin,
        string step,
        string language)
    {
        var response = await admin.GetAsync(AdminWizard);
        await EnsureSuccessAsync(response);

        var row = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("steps").EnumerateArray()
            .First(s => s.GetProperty("step").GetString() == step);

        return (Read(row, "title"), Read(row, "subtitle"));

        string? Read(JsonElement source, string field) =>
            source.GetProperty(field).TryGetProperty(language, out var value) ? value.GetString() : null;
    }

    /// <summary>Puts a step's copy back exactly as it was, including "there was none".</summary>
    private static Task RestoreAsync(HttpClient admin, string step, string language, (string? Title, string? Subtitle) copy) =>
        admin.PutAsJsonAsync(AdminHeading, new
        {
            step,
            title = new Dictionary<string, string> { [language] = copy.Title ?? string.Empty },
            subtitle = new Dictionary<string, string> { [language] = copy.Subtitle ?? string.Empty },
        });

    [Fact]
    public async Task EveryEditableStepIsReadableWithoutSigningIn()
    {
        var wizard = await WizardAsync(CreateClient(), "en");

        foreach (var step in new[] { "addressee", "personal", "details", "summary" })
        {
            // Seeded with the wording the wizard shipped with, so a fresh install is not blank.
            Title(wizard, step).Should().NotBeNullOrWhiteSpace();
            Subtitle(wizard, step).Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task TheHeadingFollowsTheRequestedLanguage()
    {
        var english = await WizardAsync(CreateClient(), "en");
        var arabic = await WizardAsync(CreateClient(), "ar");

        Title(english, "addressee").Should().NotBe(Title(arabic, "addressee"));
    }

    [Fact]
    public async Task ALanguageNobodyHasWrittenIsLeftToTheAppsOwnTranslation()
    {
        var turkish = await WizardAsync(CreateClient(), "tr");

        // Null, not the English copy: the wizard has a Turkish translation of its own, and handing
        // back English here would replace it.
        Title(turkish, "addressee").Should().BeNull();
        Subtitle(turkish, "addressee").Should().BeNull();
    }

    [Fact]
    public async Task SavingTheTitleLeavesTheSubtitleAlone()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var before = await StoredAsync(admin, "summary", "en");

        try
        {
            await EnsureSuccessAsync(await admin.PutAsJsonAsync(AdminHeading, new
            {
                step = "summary",
                title = new Dictionary<string, string> { ["en"] = "What this will cost" },
            }));

            var after = await WizardAsync(CreateClient(), "en");
            Title(after, "summary").Should().Be("What this will cost");
            Subtitle(after, "summary").Should().Be(before.Subtitle);
        }
        finally
        {
            await RestoreAsync(admin, "summary", "en", before);
        }
    }

    [Fact]
    public async Task ABlankHeadingPutsTheStepBackToItsBuiltInWording()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var before = await StoredAsync(admin, "personal", "en");

        try
        {
            await EnsureSuccessAsync(await admin.PutAsJsonAsync(AdminHeading, new
            {
                step = "personal",
                title = new Dictionary<string, string> { ["en"] = "   " },
            }));

            // Blank is "use the app's own words again", not a heading made of spaces.
            Title(await WizardAsync(CreateClient(), "en"), "personal").Should().BeNull();
        }
        finally
        {
            await RestoreAsync(admin, "personal", "en", before);
        }

        Title(await WizardAsync(CreateClient(), "en"), "personal").Should().Be(before.Title);
    }

    [Fact]
    public async Task TheCopyIsKeptTrimmed()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var before = await StoredAsync(admin, "details", "en");

        try
        {
            await EnsureSuccessAsync(await admin.PutAsJsonAsync(AdminHeading, new
            {
                step = "details",
                title = new Dictionary<string, string> { ["en"] = "  What do you need?  " },
            }));

            Title(await WizardAsync(CreateClient(), "en"), "details").Should().Be("What do you need?");
        }
        finally
        {
            await RestoreAsync(admin, "details", "en", before);
        }
    }

    [Theory]
    [InlineData("files")]
    [InlineData("review")]
    [InlineData("nonsense")]
    public async Task AStepThatIsNotEditableIsRefused(string step)
    {
        var admin = CreateClient(await AdminTokenAsync());

        // Documents and review carry a placeholder for the upload limit, so their copy is not free
        // text; anything else is simply not a step.
        var response = await admin.PutAsJsonAsync(AdminHeading, new
        {
            step,
            title = new Dictionary<string, string> { ["en"] = "Anything" },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CopyLongerThanTheColumnIsRefused()
    {
        var admin = CreateClient(await AdminTokenAsync());

        var response = await admin.PutAsJsonAsync(AdminHeading, new
        {
            step = "addressee",
            title = new Dictionary<string, string> { ["en"] = new string('x', 201) },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AnUnsupportedLanguageIsRefused()
    {
        var admin = CreateClient(await AdminTokenAsync());

        var response = await admin.PutAsJsonAsync(AdminHeading, new
        {
            step = "addressee",
            title = new Dictionary<string, string> { ["kl"] = "Anything" },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EditingTheWizardRequiresAnAdminSession()
    {
        var anonymous = CreateClient();

        (await anonymous.GetAsync(AdminWizard)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await anonymous.PutAsJsonAsync(AdminHeading, new
        {
            step = "addressee",
            title = new Dictionary<string, string> { ["en"] = "Anything" },
        })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
