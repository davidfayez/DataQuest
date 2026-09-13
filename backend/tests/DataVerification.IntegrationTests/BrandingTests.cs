using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// The platform logo, uploaded from the admin panel.
///
/// The rules that matter: it reads anonymously (the mark is on signed-out pages), the type is
/// decided by the file's signature rather than its name, the version token changes on replacement
/// so a cached logo is never shown, and removing the upload restores the bundled mark instead of
/// leaving the sites with nothing to draw.
/// </summary>
public sealed class BrandingTests : ApiTestBase
{
    public BrandingTests(ApiFactory factory) : base(factory) { }

    private static string PublicBranding => Url("/content/branding");
    private static string PublicLogo => Url("/content/logo");
    private static string AdminLogo => Url("/admin/content/branding/logo");

    /// <summary>A real 1x1 PNG, so the signature check sees genuine PNG bytes.</summary>
    private static byte[] Png() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient admin,
        byte[] bytes,
        string fileName,
        string contentType = "image/png")
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, "file", fileName);

        return await admin.PostAsync(AdminLogo, content);
    }

    [Fact]
    public async Task BrandingIsReadableWithoutSigningIn()
    {
        var response = await CreateClient().GetAsync(PublicBranding);

        await EnsureSuccessAsync(response);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("hasLogo", out _).Should().BeTrue();
        body.TryGetProperty("version", out _).Should().BeTrue();
    }

    [Fact]
    public async Task WithNoUploadTheLogoEndpointIsNotFound()
    {
        var admin = CreateClient(await AdminTokenAsync());
        await admin.DeleteAsync(AdminLogo);

        // 404 is what tells the sites to draw the mark bundled with the build.
        (await CreateClient().GetAsync(PublicLogo)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AnUploadedLogoIsServedAndReported()
    {
        var admin = CreateClient(await AdminTokenAsync());

        try
        {
            await EnsureSuccessAsync(await UploadAsync(admin, Png(), "brand.png"));

            var branding = await CreateClient().GetFromJsonAsync<JsonElement>(PublicBranding);
            branding.GetProperty("hasLogo").GetBoolean().Should().BeTrue();
            branding.GetProperty("fileName").GetString().Should().Be("brand.png");

            var logo = await CreateClient().GetAsync(PublicLogo);
            await EnsureSuccessAsync(logo);
            logo.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
            (await logo.Content.ReadAsByteArrayAsync()).Should().Equal(Png());
        }
        finally
        {
            await admin.DeleteAsync(AdminLogo);
        }
    }

    [Fact]
    public async Task ReplacingTheLogoChangesTheVersionToken()
    {
        var admin = CreateClient(await AdminTokenAsync());

        try
        {
            await EnsureSuccessAsync(await UploadAsync(admin, Png(), "first.png"));
            var first = (await CreateClient().GetFromJsonAsync<JsonElement>(PublicBranding))
                .GetProperty("version").GetString();

            // A second apart is not needed: the token comes from the row's update stamp.
            await Task.Delay(20);
            await EnsureSuccessAsync(await UploadAsync(admin, Png(), "second.png"));

            var second = (await CreateClient().GetFromJsonAsync<JsonElement>(PublicBranding))
                .GetProperty("version").GetString();

            second.Should().NotBe(first);
        }
        finally
        {
            await admin.DeleteAsync(AdminLogo);
        }
    }

    [Fact]
    public async Task RemovingTheLogoRestoresTheBundledMark()
    {
        var admin = CreateClient(await AdminTokenAsync());

        await EnsureSuccessAsync(await UploadAsync(admin, Png(), "temporary.png"));
        (await CreateClient().GetFromJsonAsync<JsonElement>(PublicBranding))
            .GetProperty("hasLogo").GetBoolean().Should().BeTrue();

        var removed = await admin.DeleteAsync(AdminLogo);
        await EnsureSuccessAsync(removed);

        (await CreateClient().GetFromJsonAsync<JsonElement>(PublicBranding))
            .GetProperty("hasLogo").GetBoolean().Should().BeFalse();
        (await CreateClient().GetAsync(PublicLogo)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AFileThatIsNotAnImageIsRefusedWhateverItIsNamed()
    {
        var admin = CreateClient(await AdminTokenAsync());

        // Named and declared as a PNG, but the bytes are not one. The signature decides.
        //
        // 415 rather than 400: the platform's upload gate inspects the body before the request
        // reaches the handler, so it is turned away a step earlier than the handler's own check.
        // Either is a refusal; what matters is that nothing was stored.
        var response = await UploadAsync(
            admin, "<script>alert(1)</script>"u8.ToArray(), "evil.png");

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest, HttpStatusCode.UnsupportedMediaType);
        (await CreateClient().GetFromJsonAsync<JsonElement>(PublicBranding))
            .GetProperty("hasLogo").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task UploadingRequiresAnAdminSession()
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Png());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "file", "anyone.png");

        (await CreateClient().PostAsync(AdminLogo, content)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

        (await CreateClient().DeleteAsync(AdminLogo)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }
}
