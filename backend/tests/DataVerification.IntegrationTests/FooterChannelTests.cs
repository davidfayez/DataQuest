using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// The "Follow us" and "Message us" rows in the site footer, which an administrator configures.
///
/// What matters here: the public endpoint is reachable without a session (the footer is on every
/// public page), an administrator's edit reaches it, and the two rules that keep the footer honest
/// — one platform per row, and addresses that are actually http(s).
/// </summary>
public sealed class FooterChannelTests : ApiTestBase
{
    private const int FollowUs = 0;
    private const int MessageUs = 1;

    private const int Facebook = 0;
    private const int Telegram = 4;
    private const int TikTok = 7;

    public FooterChannelTests(ApiFactory factory) : base(factory) { }

    [Fact]
    public async Task TheFooterChannelsAreReadableWithoutSigningIn()
    {
        var response = await CreateClient().GetAsync(Url("/content/social-links"));

        await EnsureSuccessAsync(response);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("followUs", out var follow).Should().BeTrue();
        body.TryGetProperty("messageUs", out var message).Should().BeTrue();

        // Seeded, so the footer is not empty on a fresh database.
        follow.GetArrayLength().Should().BeGreaterThan(0);
        message.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AnAdminsNewChannelReachesThePublicFooter()
    {
        var admin = CreateClient(await AdminTokenAsync());

        var created = await admin.PostAsJsonAsync(Url("/admin/social-links"), new
        {
            placement = FollowUs,
            platform = TikTok,
            url = "https://www.tiktok.com/@nenglobal",
            isActive = true,
            sortOrder = 90,
        });

        await EnsureSuccessAsync(created);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        try
        {
            var published = await CreateClient().GetFromJsonAsync<JsonElement>(Url("/content/social-links"));
            var platforms = published.GetProperty("followUs").EnumerateArray()
                .Select(link => link.GetProperty("platform").GetString())
                .ToList();

            platforms.Should().Contain("TikTok");
        }
        finally
        {
            await admin.DeleteAsync(Url($"/admin/social-links/{id}"));
        }
    }

    [Fact]
    public async Task AnInactiveChannelIsNotPublished()
    {
        var admin = CreateClient(await AdminTokenAsync());

        var created = await admin.PostAsJsonAsync(Url("/admin/social-links"), new
        {
            placement = MessageUs,
            platform = TikTok,
            url = "https://www.tiktok.com/@hidden",
            isActive = false,
            sortOrder = 91,
        });

        await EnsureSuccessAsync(created);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        try
        {
            var published = await CreateClient().GetFromJsonAsync<JsonElement>(Url("/content/social-links"));
            var platforms = published.GetProperty("messageUs").EnumerateArray()
                .Select(link => link.GetProperty("platform").GetString())
                .ToList();

            platforms.Should().NotContain("TikTok");
        }
        finally
        {
            await admin.DeleteAsync(Url($"/admin/social-links/{id}"));
        }
    }

    [Fact]
    public async Task TheSamePlatformCannotBeListedTwiceInOneRow()
    {
        var admin = CreateClient(await AdminTokenAsync());

        // Facebook is seeded under Follow us, so a second one is the duplicate under test.
        var response = await admin.PostAsJsonAsync(Url("/admin/social-links"), new
        {
            placement = FollowUs,
            platform = Facebook,
            url = "https://www.facebook.com/another",
            isActive = true,
            sortOrder = 92,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task TheSamePlatformMayAppearInBothRows()
    {
        // Telegram is seeded in both rows already — a channel to follow, and an address to message.
        var published = await CreateClient().GetFromJsonAsync<JsonElement>(Url("/content/social-links"));

        Platforms(published, "followUs").Should().Contain("Telegram");
        Platforms(published, "messageUs").Should().Contain("Telegram");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("not-a-url")]
    [InlineData("")]
    public async Task AnAddressThatIsNotAnHttpUrlIsRefused(string url)
    {
        var admin = CreateClient(await AdminTokenAsync());

        var response = await admin.PostAsJsonAsync(Url("/admin/social-links"), new
        {
            placement = FollowUs,
            platform = TikTok,
            url,
            isActive = true,
            sortOrder = 93,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ConfiguringTheFooterRequiresAnAdminSession()
    {
        var response = await CreateClient().PostAsJsonAsync(Url("/admin/social-links"), new
        {
            placement = FollowUs,
            platform = Telegram,
            url = "https://t.me/anyone",
            isActive = true,
            sortOrder = 94,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static List<string?> Platforms(JsonElement body, string row) =>
        body.GetProperty(row).EnumerateArray()
            .Select(link => link.GetProperty("platform").GetString())
            .ToList();
}
