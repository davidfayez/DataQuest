using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// The provider keys saved in Settings, shown back to the operator who manages them.
///
/// What matters: the full key comes only from its own endpoint — never from the settings payload a
/// view-only caller reads — it is never cached on the way, and nobody without a session gets it.
/// Storing a key needs the platform's encryption key; without one there is nothing to show, so the
/// tests stop at the point the panel would.
/// </summary>
public sealed class StoredApiKeyTests : ApiTestBase
{
    public StoredApiKeyTests(ApiFactory factory) : base(factory) { }

    private static string EmailSettings => Url("/admin/settings/email");
    private static string EmailApiKey => Url("/admin/settings/email/api-key");
    private static string AiSettings => Url("/admin/settings/ai");
    private static string AiApiKey => Url("/admin/settings/ai/api-key");

    [Fact]
    public async Task ASavedSendGridKeyCanBeShownAgain()
    {
        var admin = CreateClient(await AdminTokenAsync());

        var before = await ReadJsonAsync(await admin.GetAsync(EmailSettings));
        if (!before.GetProperty("canEdit").GetBoolean()) return;

        const string Key = "SG.integration-test-key-that-is-not-real";

        try
        {
            await EnsureSuccessAsync(await admin.PutAsJsonAsync(EmailSettings, new { apiKey = Key }));

            var shown = await admin.GetAsync(EmailApiKey);
            await EnsureSuccessAsync(shown);
            shown.Headers.CacheControl!.NoStore.Should().BeTrue();
            (await shown.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("apiKey").GetString().Should().Be(Key);

            // The settings payload itself still carries only the masked form.
            var settings = await admin.GetAsync(EmailSettings);
            (await settings.Content.ReadAsStringAsync()).Should().NotContain(Key);
        }
        finally
        {
            await admin.PutAsJsonAsync(EmailSettings, new { apiKey = (string?)null });
        }
    }

    [Fact]
    public async Task ASavedGeminiKeyCanBeShownAgain()
    {
        var admin = CreateClient(await AdminTokenAsync());

        var before = await ReadJsonAsync(await admin.GetAsync(AiSettings));
        if (!before.GetProperty("canStore").GetBoolean()) return;

        const string Key = "AIzaSyStoredKeyTest-not-a-real-key";

        await EnsureSuccessAsync(await admin.PutAsJsonAsync(AiSettings, new
        {
            isEnabled = false,
            model = "gemini-3.6-flash",
            apiKey = Key,
        }));

        var shown = await admin.GetAsync(AiApiKey);
        await EnsureSuccessAsync(shown);
        shown.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await shown.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("apiKey").GetString().Should().Be(Key);

        (await (await admin.GetAsync(AiSettings)).Content.ReadAsStringAsync())
            .Should().NotContain(Key);
    }

    [Fact]
    public async Task StoredKeysAreNotShownWithoutAnAdminSession()
    {
        var anonymous = CreateClient();

        (await anonymous.GetAsync(EmailApiKey)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync(AiApiKey)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
