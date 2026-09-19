using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// Two things a service type now carries: labelled reference files on each required document, and
/// a switch that keeps its description away from applicants.
///
/// The rules worth pinning: a reference file needs both labels and a real file, both realms can
/// read it back, saving the service type leaves it in place, and a hidden description reaches no
/// applicant-facing response while the panel still sees it.
/// </summary>
public sealed class ServiceTypeReferenceTests : ApiTestBase
{
    public ServiceTypeReferenceTests(ApiFactory factory) : base(factory) { }

    private static string ServiceTypes => Url("/admin/lookups/service-types");

    /// <summary>A real 1x1 PNG, so the signature check sees genuine PNG bytes.</summary>
    private static byte[] Png() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient admin,
        Guid requiredFileId,
        byte[] bytes,
        string fileName = "example.png",
        string? labelAr = "نموذج",
        string? labelEn = "Example")
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "file", fileName);
        if (labelAr is not null) content.Add(new StringContent(labelAr), "labelAr");
        if (labelEn is not null) content.Add(new StringContent(labelEn), "labelEn");

        return await admin.PostAsync(
            $"{ServiceTypes}/required-files/{requiredFileId}/samples", content);
    }

    /// <summary>An active service type that has at least one required document.</summary>
    private static async Task<JsonObject> AnyServiceTypeAsync(HttpClient admin)
    {
        var page = await ReadJsonAsync(await admin.GetAsync($"{ServiceTypes}?pageSize=200"));

        var match = page.GetProperty("items").EnumerateArray()
            .First(item => item.GetProperty("isActive").GetBoolean()
                           && item.GetProperty("requiredFiles").GetArrayLength() > 0);

        return JsonNode.Parse(match.GetRawText())!.AsObject();
    }

    private static async Task<JsonObject> ReloadAsync(HttpClient admin, Guid id)
    {
        var page = await ReadJsonAsync(await admin.GetAsync($"{ServiceTypes}?pageSize=200"));
        var match = page.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == id);

        return JsonNode.Parse(match.GetRawText())!.AsObject();
    }

    /// <summary>
    /// The list row is a superset of the save body — the API ignores the fields it does not bind —
    /// so saving a row back unchanged is a faithful "edit and save".
    /// </summary>
    private static async Task SaveAsync(HttpClient admin, JsonObject serviceType) =>
        await EnsureSuccessAsync(await admin.PostAsJsonAsync(ServiceTypes, serviceType));

    [Fact]
    public async Task AReferenceFileIsStoredListedAndReadableByBothRealms()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var serviceType = await AnyServiceTypeAsync(admin);
        var requiredFileId = serviceType["requiredFiles"]![0]!["id"]!.GetValue<Guid>();

        var created = await ReadJsonAsync(await EnsureSuccessAsync(
            await UploadAsync(admin, requiredFileId, Png(), labelAr: "الوجه الأمامي", labelEn: "Front side")));
        var sampleId = created.GetProperty("id").GetGuid();

        try
        {
            created.GetProperty("labelEn").GetString().Should().Be("Front side");
            created.GetProperty("labelAr").GetString().Should().Be("الوجه الأمامي");
            created.GetProperty("contentType").GetString().Should().Be("image/png");

            // Listed on its document.
            var reloaded = await ReloadAsync(admin, serviceType["id"]!.GetValue<Guid>());
            var samples = reloaded["requiredFiles"]!.AsArray()
                .Single(file => file!["id"]!.GetValue<Guid>() == requiredFileId)!["samples"]!.AsArray();
            samples.Select(sample => sample!["id"]!.GetValue<Guid>()).Should().Contain(sampleId);

            // The panel's preview.
            var adminFile = await admin.GetAsync($"{ServiceTypes}/samples/{sampleId}/file");
            await EnsureSuccessAsync(adminFile);
            (await adminFile.Content.ReadAsByteArrayAsync()).Should().Equal(Png());

            // The wizard's preview, for any signed-in applicant.
            var applicant = CreateClient((await RegisterApplicantAsync()).Token);
            var applicantFile = await applicant.GetAsync(Url($"/required-files/samples/{sampleId}/file"));
            await EnsureSuccessAsync(applicantFile);
            (await applicantFile.Content.ReadAsByteArrayAsync()).Should().Equal(Png());
        }
        finally
        {
            await admin.DeleteAsync($"{ServiceTypes}/samples/{sampleId}");
        }

        // Gone from both realms once removed.
        (await admin.GetAsync($"{ServiceTypes}/samples/{sampleId}/file"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SavingTheServiceTypeKeepsItsReferenceFiles()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var serviceType = await AnyServiceTypeAsync(admin);
        var id = serviceType["id"]!.GetValue<Guid>();
        var requiredFileId = serviceType["requiredFiles"]![0]!["id"]!.GetValue<Guid>();

        var sampleId = (await ReadJsonAsync(await EnsureSuccessAsync(
            await UploadAsync(admin, requiredFileId, Png())))).GetProperty("id").GetGuid();

        try
        {
            await SaveAsync(admin, await ReloadAsync(admin, id));

            var reloaded = await ReloadAsync(admin, id);
            reloaded["requiredFiles"]!.AsArray()
                .Single(file => file!["id"]!.GetValue<Guid>() == requiredFileId)!["samples"]!.AsArray()
                .Select(sample => sample!["id"]!.GetValue<Guid>())
                .Should().Contain(sampleId);
        }
        finally
        {
            await admin.DeleteAsync($"{ServiceTypes}/samples/{sampleId}");
        }
    }

    [Theory]
    [InlineData(null, "Example")]
    [InlineData("نموذج", null)]
    [InlineData("   ", "Example")]
    public async Task AReferenceFileNeedsBothLabels(string? labelAr, string? labelEn)
    {
        var admin = CreateClient(await AdminTokenAsync());
        var serviceType = await AnyServiceTypeAsync(admin);
        var requiredFileId = serviceType["requiredFiles"]![0]!["id"]!.GetValue<Guid>();

        var response = await UploadAsync(admin, requiredFileId, Png(), labelAr: labelAr, labelEn: labelEn);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AFileThatIsNotWhatItClaimsIsRefused()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var serviceType = await AnyServiceTypeAsync(admin);
        var requiredFileId = serviceType["requiredFiles"]![0]!["id"]!.GetValue<Guid>();

        // Named a PNG, but the bytes are not one. The signature decides; the upload gate may turn it
        // away before the handler does, so either refusal counts.
        var response = await UploadAsync(
            admin, requiredFileId, "<script>alert(1)</script>"u8.ToArray(), "evil.png");

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest, HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task AnUnknownDocumentCannotTakeAReferenceFile()
    {
        var admin = CreateClient(await AdminTokenAsync());

        (await UploadAsync(admin, Guid.NewGuid(), Png()))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ReferenceFilesAreClosedToAnyoneNotSignedIn()
    {
        var anonymous = CreateClient();

        (await UploadAsync(anonymous, Guid.NewGuid(), Png()))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"{ServiceTypes}/samples/{Guid.NewGuid()}/file"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync(Url($"/required-files/samples/{Guid.NewGuid()}/file")))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AHiddenDescriptionStaysInThePanelButNotOnTheLandingPage()
    {
        var admin = CreateClient(await AdminTokenAsync());

        var page = await ReadJsonAsync(await admin.GetAsync($"{ServiceTypes}?pageSize=200"));
        var candidate = page.GetProperty("items").EnumerateArray()
            .FirstOrDefault(item => item.GetProperty("isActive").GetBoolean()
                                    && item.GetProperty("showOnLanding").GetBoolean());

        // Nothing on the landing page in this database means nothing to check there.
        if (candidate.ValueKind == JsonValueKind.Undefined) return;

        var original = JsonNode.Parse(candidate.GetRawText())!.AsObject();
        var id = original["id"]!.GetValue<Guid>();
        const string Text = "A description only the panel should see";

        try
        {
            var hidden = original.DeepClone().AsObject();
            hidden["descriptionEn"] = Text;
            hidden["hideDescription"] = true;
            await SaveAsync(admin, hidden);

            var reloaded = await ReloadAsync(admin, id);
            reloaded["hideDescription"]!.GetValue<bool>().Should().BeTrue();
            reloaded["descriptionEn"]!.GetValue<string>().Should().Be(Text);

            var landing = await CreateClient().GetAsync(Url("/content/services"));
            await EnsureSuccessAsync(landing);
            var row = (await landing.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()
                .Single(item => item.GetProperty("id").GetGuid() == id);
            row.GetProperty("description").ValueKind.Should().Be(JsonValueKind.Null);

            // Switched back, the same text shows again.
            var shown = hidden.DeepClone().AsObject();
            shown["hideDescription"] = false;
            await SaveAsync(admin, shown);

            var landingAgain = await CreateClient().GetAsync(Url("/content/services"));
            (await landingAgain.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()
                .Single(item => item.GetProperty("id").GetGuid() == id)
                .GetProperty("description").GetString().Should().Be(Text);
        }
        finally
        {
            await SaveAsync(admin, original);
        }
    }
}
