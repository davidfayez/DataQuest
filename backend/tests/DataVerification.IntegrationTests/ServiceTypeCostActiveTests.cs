using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// The Active switch on each currency price of a service type.
///
/// Worth pinning: a switched-off currency keeps its price but the service disappears for orders in
/// it and cannot be bought in it; the other currencies are untouched; a switched-off currency needs
/// no express price; and a service cannot have every currency switched off.
/// </summary>
public sealed class ServiceTypeCostActiveTests : ApiTestBase
{
    public ServiceTypeCostActiveTests(ApiFactory factory) : base(factory) { }

    private static string ServiceTypes => Url("/admin/lookups/service-types");

    private static object Service(string name, object[] costs, bool enableExpress = false) => new
    {
        code = "A" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
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
        requiredFiles = new[]
        {
            new
            {
                nameAr = "مستند",
                nameEn = "Document",
                isMandatory = true,
                maxSizeBytes = (long?)null,
                maxFiles = 1,
                fields = Array.Empty<object>(),
                allowedFileTypes = new[] { "pdf" },
            },
        },
        outputLanguages = new[] { "en" },
    };

    private static object Cost(string currencyId, decimal cost, bool isActive = true, decimal expressCost = 0m) =>
        new { currencyId, cost, expressCost, isActive };

    /// <summary>An applicant whose order is held in the given currency.</summary>
    private async Task<string> ApplicantInAsync(string currencyId)
    {
        var client = CreateClient();
        var registration = await ReadJsonAsync(await client.PostAsJsonAsync(
            Url("/orders/register"),
            new { email = $"it-{Guid.NewGuid():N}@example.com", languageCode = "en" }));
        var login = await ReadJsonAsync(await client.PostAsJsonAsync(Url("/orders/login"), new
        {
            orderNumber = registration.GetProperty("orderNumber").GetString(),
            password = registration.GetProperty("password").GetString(),
        }));
        var token = login.GetProperty("accessToken").GetString()!;

        await EnsureSuccessAsync(await CreateClient(token).PutAsJsonAsync(Url("/orders/setup"), new
        {
            verificationCountryId = SeedIds.Egypt,
            currencyId,
            contactPersonName = "Mona Fahmy",
            contactPersonPhoneCountry = "EG",
            contactPersonPhoneCode = "+20",
            contactPersonPhoneNumber = "1001234567",
        }));

        return token;
    }

    private async Task<List<string>> OfferedServiceIdsAsync(string token) =>
        (await ReadJsonAsync(await EnsureSuccessAsync(await CreateClient(token).GetAsync(Url(
            $"/authorities/{SeedIds.AuthoritySupremeCouncil}/service-types?subTransactionTypeId={SeedIds.SubBachelor}")))))
        .EnumerateArray()
        .Select(s => s.GetProperty("id").GetString()!)
        .ToList();

    [Fact]
    public async Task ASwitchedOffCurrencyHidesTheServiceOnlyForOrdersInIt()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var created = await ReadJsonAsync(await EnsureSuccessAsync(await admin.PostAsJsonAsync(
            ServiceTypes,
            Service($"USD only {Guid.NewGuid():N}", [Cost(SeedIds.Egp, 750m, isActive: false), Cost(SeedIds.Usd, 25m)]))));
        var id = created.GetProperty("id").GetString()!;

        try
        {
            // The admin still sees both prices, and which one is switched off.
            var costs = created.GetProperty("costs").EnumerateArray().ToList();
            costs.Should().HaveCount(2);
            costs.Single(c => c.GetProperty("currencyId").GetString() == SeedIds.Egp)
                .GetProperty("isActive").GetBoolean().Should().BeFalse();
            costs.Single(c => c.GetProperty("currencyId").GetString() == SeedIds.Egp)
                .GetProperty("cost").GetDecimal().Should().Be(750m);

            (await OfferedServiceIdsAsync(await ApplicantInAsync(SeedIds.Egp))).Should().NotContain(id);
            (await OfferedServiceIdsAsync(await ApplicantInAsync(SeedIds.Usd))).Should().Contain(id);
        }
        finally
        {
            await admin.DeleteAsync($"{ServiceTypes}/{id}");
        }
    }

    [Fact]
    public async Task AServiceCannotBeBoughtInASwitchedOffCurrency()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var created = await ReadJsonAsync(await EnsureSuccessAsync(await admin.PostAsJsonAsync(
            ServiceTypes,
            Service($"No EGP sales {Guid.NewGuid():N}", [Cost(SeedIds.Egp, 750m, isActive: false), Cost(SeedIds.Usd, 25m)]))));
        var id = created.GetProperty("id").GetString()!;

        try
        {
            var token = await ApplicantInAsync(SeedIds.Egp);
            var response = await CreateClient(token).PostAsJsonAsync(Url("/applications"), new
            {
                addressedTo = "Ministry of Higher Education",
                birthDate = "1990-05-14",
                applicantEmail = "layla.hassan@example.com",
                applicantPhoneCountry = "EG",
                applicantPhoneCode = "+20",
                applicantPhoneNumber = "1005550101",
                names = new[]
                {
                    new { languageType = 0, firstName = "ليلى", middleName = (string?)null, lastName = "حسن" },
                    new { languageType = 1, firstName = "Layla", middleName = (string?)null, lastName = "Hassan" },
                },
                transactionTypeId = SeedIds.TxEducational,
                subTransactionTypeId = SeedIds.SubBachelor,
                verificationAuthorityId = SeedIds.AuthoritySupremeCouncil,
                services = new[] { new { serviceTypeId = id, quantity = 1, languageCode = "en", isExpress = false } },
            });

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await ReadProblemCodeAsync(response)).Should().Be("service.currency_price_missing");
        }
        finally
        {
            await admin.DeleteAsync($"{ServiceTypes}/{id}");
        }
    }

    [Fact]
    public async Task ASwitchedOffCurrencyNeedsNoExpressPrice()
    {
        var admin = CreateClient(await AdminTokenAsync());

        // Express on, but EGP is not sold, so its missing express price does not block the save.
        var response = await admin.PostAsJsonAsync(
            ServiceTypes,
            Service(
                $"Express USD {Guid.NewGuid():N}",
                [Cost(SeedIds.Egp, 0m, isActive: false), Cost(SeedIds.Usd, 25m, expressCost: 40m)],
                enableExpress: true));

        await EnsureSuccessAsync(response);
        var created = await ReadJsonAsync(response);
        await admin.DeleteAsync($"{ServiceTypes}/{created.GetProperty("id").GetString()}");
    }

    [Fact]
    public async Task EveryCurrencySwitchedOffIsRefused()
    {
        var admin = CreateClient(await AdminTokenAsync());

        var response = await admin.PostAsJsonAsync(
            ServiceTypes,
            Service(
                $"Nothing on sale {Guid.NewGuid():N}",
                [Cost(SeedIds.Egp, 750m, isActive: false), Cost(SeedIds.Usd, 25m, isActive: false)]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task APriceSentWithoutTheSwitchStaysActive()
    {
        var admin = CreateClient(await AdminTokenAsync());

        // An older client never sends isActive; its prices must stay on sale.
        var response = await admin.PostAsJsonAsync(
            ServiceTypes,
            Service(
                $"Old client {Guid.NewGuid():N}",
                [new { currencyId = SeedIds.Egp, cost = 750m, expressCost = 0m }, new { currencyId = SeedIds.Usd, cost = 25m, expressCost = 0m }]));

        await EnsureSuccessAsync(response);
        var created = await ReadJsonAsync(response);

        try
        {
            created.GetProperty("costs").EnumerateArray()
                .Should().OnlyContain(c => c.GetProperty("isActive").GetBoolean());
        }
        finally
        {
            await admin.DeleteAsync($"{ServiceTypes}/{created.GetProperty("id").GetString()}");
        }
    }
}
