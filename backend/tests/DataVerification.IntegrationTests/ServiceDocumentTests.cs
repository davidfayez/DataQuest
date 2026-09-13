using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// What a service asks the applicant to upload: at least one document, and the formats each
/// document takes.
/// </summary>
public sealed class ServiceDocumentTests : ApiTestBase
{
    public ServiceDocumentTests(ApiFactory factory) : base(factory) { }

    private static string ServiceTypes => Url("/admin/lookups/service-types");

    private static object Service(string name, object[] requiredFiles) => new
    {
        code = "S" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
        verificationAuthorityId = SeedIds.AuthoritySupremeCouncil,
        subTransactionTypeId = SeedIds.SubBachelor,
        nameAr = name,
        nameEn = name,
        descriptionAr = (string?)null,
        descriptionEn = (string?)null,
        executionTimeDays = 5,
        enableExpress = false,
        expressNoteAr = (string?)null,
        expressNoteEn = (string?)null,
        isActive = true,
        showOnLanding = false,
        costs = new[]
        {
            new { currencyId = SeedIds.Egp, cost = 750m, expressCost = 0m },
            new { currencyId = SeedIds.Usd, cost = 25m, expressCost = 0m },
        },
        requiredFiles,
        outputLanguages = new[] { "en" },
    };

    private static object Document(string[]? allowedFileTypes = null) => new
    {
        nameAr = "مستند",
        nameEn = "Certificate scan",
        isMandatory = true,
        maxSizeBytes = (long?)null,
        maxFiles = 1,
        fields = Array.Empty<object>(),
        allowedFileTypes,
    };

    private async Task DeleteAsync(HttpClient admin, JsonElement created) =>
        await admin.DeleteAsync($"{ServiceTypes}/{created.GetProperty("id").GetGuid()}");

    [Fact]
    public async Task AServiceWithNoDocumentsIsRefused()
    {
        var admin = CreateClient(await AdminTokenAsync());

        // A service that asks for nothing leaves the review queue with nothing to verify.
        var response = await admin.PostAsJsonAsync(
            ServiceTypes,
            Service($"No documents {Guid.NewGuid():N}", []));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errors").GetProperty("RequiredFiles")
            .EnumerateArray().First().GetString()
            .Should().Contain("at least one required document");
    }

    [Fact]
    public async Task ADocumentKeepsTheFormatsItWasGiven()
    {
        var admin = CreateClient(await AdminTokenAsync());

        var response = await admin.PostAsJsonAsync(
            ServiceTypes,
            Service($"Word only {Guid.NewGuid():N}", [Document(["word", "excel"])]));

        await EnsureSuccessAsync(response);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();

        try
        {
            var document = created.GetProperty("requiredFiles").EnumerateArray().First();

            document.GetProperty("allowedFileTypes").EnumerateArray()
                .Select(t => t.GetString()).Should().BeEquivalentTo("word", "excel");

            // The extensions come back too, so an upload control can offer exactly what the
            // server will accept rather than reimplementing the mapping.
            document.GetProperty("allowedExtensions").EnumerateArray()
                .Select(t => t.GetString()).Should().BeEquivalentTo(".docx", ".xlsx");
        }
        finally
        {
            await DeleteAsync(admin, created);
        }
    }

    [Fact]
    public async Task ADocumentGivenNoFormatsFallsBackToThePlatformDefault()
    {
        var admin = CreateClient(await AdminTokenAsync());

        // The state every document created before this existed is in. It has to keep behaving the
        // way it did rather than suddenly accepting nothing.
        var response = await admin.PostAsJsonAsync(
            ServiceTypes,
            Service($"Default formats {Guid.NewGuid():N}", [Document()]));

        await EnsureSuccessAsync(response);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();

        try
        {
            created.GetProperty("requiredFiles").EnumerateArray().First()
                .GetProperty("allowedFileTypes").EnumerateArray()
                .Select(t => t.GetString()).Should().BeEquivalentTo("pdf", "jpg", "png");
        }
        finally
        {
            await DeleteAsync(admin, created);
        }
    }

    [Fact]
    public async Task AFormatThePlatformDoesNotKnowIsRefused()
    {
        var admin = CreateClient(await AdminTokenAsync());

        var response = await admin.PostAsJsonAsync(
            ServiceTypes,
            Service($"Bad format {Guid.NewGuid():N}", [Document(["powerpoint"])]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TheFormatsCanBeChangedOnAnExistingService()
    {
        var admin = CreateClient(await AdminTokenAsync());

        var response = await admin.PostAsJsonAsync(
            ServiceTypes,
            Service($"Changing formats {Guid.NewGuid():N}", [Document(["pdf"])]));

        await EnsureSuccessAsync(response);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();
        var documentId = created.GetProperty("requiredFiles").EnumerateArray().First()
            .GetProperty("id").GetGuid();

        try
        {
            var updated = await admin.PostAsJsonAsync(ServiceTypes, new
            {
                id,
                code = created.GetProperty("code").GetString(),
                verificationAuthorityId = SeedIds.AuthoritySupremeCouncil,
                subTransactionTypeId = SeedIds.SubBachelor,
                nameAr = "Changing formats",
                nameEn = "Changing formats",
                descriptionAr = (string?)null,
                descriptionEn = (string?)null,
                executionTimeDays = 5,
                enableExpress = false,
                expressNoteAr = (string?)null,
                expressNoteEn = (string?)null,
                isActive = true,
                showOnLanding = false,
                costs = new[]
                {
                    new { currencyId = SeedIds.Egp, cost = 750m, expressCost = 0m },
                    new { currencyId = SeedIds.Usd, cost = 25m, expressCost = 0m },
                },
                requiredFiles = new[]
                {
                    new
                    {
                        id = documentId,
                        nameAr = "مستند",
                        nameEn = "Certificate scan",
                        isMandatory = true,
                        maxSizeBytes = (long?)null,
                        maxFiles = 1,
                        fields = Array.Empty<object>(),
                        allowedFileTypes = new[] { "jpg", "png" },
                    },
                },
                outputLanguages = new[] { "en" },
            });

            await EnsureSuccessAsync(updated);

            // Replaced wholesale, not merged: PDF is gone rather than kept alongside.
            (await updated.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("requiredFiles").EnumerateArray().First()
                .GetProperty("allowedFileTypes").EnumerateArray()
                .Select(t => t.GetString()).Should().BeEquivalentTo("jpg", "png");
        }
        finally
        {
            await DeleteAsync(admin, created);
        }
    }

    [Fact]
    public async Task TheApplicantIsToldWhatEachDocumentAccepts()
    {
        var applicant = await RegisterApplicantAsync();
        var draft = await CreateDraftAsync(applicant.Token);

        var required = draft.GetProperty("requiredFiles").EnumerateArray().ToList();
        required.Should().NotBeEmpty();

        foreach (var document in required)
        {
            // The wizard restricts its file picker to this, so it must always say something.
            document.GetProperty("allowedExtensions").EnumerateArray()
                .Should().NotBeEmpty();
        }
    }
}
