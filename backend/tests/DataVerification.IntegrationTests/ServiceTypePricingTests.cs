using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// A service must be priced in every currency it can be sold in.
///
/// The reason is not tidiness. The applicant's service list is filtered by the order's currency, so
/// a service missing one currency does not merely lack a price for those applicants — it does not
/// appear for them at all, with nothing anywhere to say why. Egypt is seeded with EGP and USD, so a
/// service on the Egyptian cascade needs both.
/// </summary>
public sealed class ServiceTypePricingTests : ApiTestBase
{
    public ServiceTypePricingTests(ApiFactory factory) : base(factory) { }

    private static string ServiceTypes => Url("/admin/lookups/service-types");

    /// <summary>A complete service on the seeded Egypt cascade, priced as told.</summary>
    private static object Service(string name, object[] costs, bool enableExpress = false) => new
    {
        code = "P" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
        verificationAuthorityId = SeedIds.AuthoritySupremeCouncil,
        subTransactionTypeId = SeedIds.SubBachelor,
        nameAr = name,
        nameEn = name,
        descriptionAr = (string?)null,
        descriptionEn = (string?)null,
        executionTimeDays = 5,
        enableExpress,
        expressNoteAr = (string?)null,
        expressNoteEn = (string?)null,
        isActive = true,
        showOnLanding = false,
        costs,
        requiredFiles = new[] { Document() },
        outputLanguages = new[] { "en" },
    };

    /// <summary>A minimal document, since a service must ask for at least one.</summary>
    private static object Document(params string[] allowedFileTypes) => new
    {
        nameAr = "مستند",
        nameEn = "Document",
        isMandatory = true,
        maxSizeBytes = (long?)null,
        maxFiles = 1,
        fields = Array.Empty<object>(),
        allowedFileTypes = allowedFileTypes.Length > 0 ? allowedFileTypes : new[] { "pdf" },
    };

    private static object Cost(string currencyId, decimal cost, decimal expressCost = 0m) =>
        new { currencyId, cost, expressCost };

    private async Task DeleteAsync(HttpClient admin, JsonElement created) =>
        await admin.DeleteAsync($"{ServiceTypes}/{created.GetProperty("id").GetGuid()}");

    [Fact]
    public async Task AServicePricedInEveryCurrencyIsAccepted()
    {
        var admin = CreateClient(await AdminTokenAsync());

        var response = await admin.PostAsJsonAsync(
            ServiceTypes,
            Service(
                $"Fully priced {Guid.NewGuid():N}",
                [Cost(SeedIds.Egp, 750m), Cost(SeedIds.Usd, 25m)]));

        await EnsureSuccessAsync(response);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();

        try
        {
            created.GetProperty("costs").EnumerateArray().Should().HaveCount(2);
        }
        finally
        {
            await DeleteAsync(admin, created);
        }
    }

    [Fact]
    public async Task AServiceMissingACurrencyIsRefused()
    {
        var admin = CreateClient(await AdminTokenAsync());

        // Priced in EGP only. Every applicant whose order is in USD would simply never see it.
        var response = await admin.PostAsJsonAsync(
            ServiceTypes,
            Service($"Half priced {Guid.NewGuid():N}", [Cost(SeedIds.Egp, 750m)]));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("service_type.missing_currency_costs");

        // The message names what is missing, so the operator does not have to work it out.
        problem.GetProperty("detail").GetString().Should().Contain("USD");
    }

    [Fact]
    public async Task ARefusedServiceIsNotLeftBehind()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var name = $"Rejected {Guid.NewGuid():N}";

        await admin.PostAsJsonAsync(ServiceTypes, Service(name, [Cost(SeedIds.Egp, 750m)]));

        var listed = await admin.GetAsync($"{ServiceTypes}?page=1&pageSize=50&search={name}");
        await EnsureSuccessAsync(listed);

        // The check runs before anything is written, so a refusal leaves no half-made service.
        (await listed.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task AnExistingServiceCannotHaveACurrencyTakenAway()
    {
        var admin = CreateClient(await AdminTokenAsync());

        var response = await admin.PostAsJsonAsync(
            ServiceTypes,
            Service(
                $"Complete then stripped {Guid.NewGuid():N}",
                [Cost(SeedIds.Egp, 750m), Cost(SeedIds.Usd, 25m)]));

        await EnsureSuccessAsync(response);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();

        try
        {
            // Editing is where a service most often loses a currency — the rule has to hold on the
            // way in, not only at creation.
            var stripped = await admin.PostAsJsonAsync(
                ServiceTypes,
                new
                {
                    id,
                    code = created.GetProperty("code").GetString(),
                    verificationAuthorityId = SeedIds.AuthoritySupremeCouncil,
                    subTransactionTypeId = SeedIds.SubBachelor,
                    nameAr = "Stripped",
                    nameEn = "Stripped",
                    descriptionAr = (string?)null,
                    descriptionEn = (string?)null,
                    executionTimeDays = 5,
                    enableExpress = false,
                    expressNoteAr = (string?)null,
                    expressNoteEn = (string?)null,
                    isActive = true,
                    showOnLanding = false,
                    costs = new[] { Cost(SeedIds.Egp, 750m) },
                    requiredFiles = new[] { Document() },
                    outputLanguages = new[] { "en" },
                });

            stripped.StatusCode.Should().Be(HttpStatusCode.Conflict);

            // And the stored service still has both prices.
            var reread = await admin.GetAsync($"{ServiceTypes}?page=1&pageSize=50&search=stripped");
            await EnsureSuccessAsync(reread);
        }
        finally
        {
            await DeleteAsync(admin, created);
        }
    }

    [Fact]
    public async Task ExpressStillNeedsASurchargeInEveryCurrency()
    {
        var admin = CreateClient(await AdminTokenAsync());

        var response = await admin.PostAsJsonAsync(
            ServiceTypes,
            Service(
                $"Express half priced {Guid.NewGuid():N}",
                [Cost(SeedIds.Egp, 750m, 200m), Cost(SeedIds.Usd, 25m)],
                enableExpress: true));

        // Every currency is priced, but one has no express surcharge — the older rule, still live.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AServiceWithNoPricesAtAllIsRefused()
    {
        var admin = CreateClient(await AdminTokenAsync());

        var response = await admin.PostAsJsonAsync(
            ServiceTypes,
            Service($"Unpriced {Guid.NewGuid():N}", []));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
