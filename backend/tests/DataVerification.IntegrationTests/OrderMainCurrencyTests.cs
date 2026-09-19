using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// The applicant no longer chooses a currency when setting up an order: it takes the country's main
/// currency, which an administrator sets on the country.
///
/// Worth pinning: a country offering two currencies lands on its own rather than the shared one
/// (Egypt on EGP, not USD), an administrator can change which is main, the main currency has to be
/// one the country offers, and a currency sent explicitly by an older client is still honoured.
/// </summary>
public sealed class OrderMainCurrencyTests : ApiTestBase
{
    public OrderMainCurrencyTests(ApiFactory factory) : base(factory) { }

    /// <summary>A registered order that has not been set up yet, and its token.</summary>
    private async Task<string> NewOrderTokenAsync()
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

        return login.GetProperty("accessToken").GetString()!;
    }

    private Task<HttpResponseMessage> SetupAsync(string token, string countryId, string? currencyId = null) =>
        CreateClient(token).PutAsJsonAsync(Url("/orders/setup"), new
        {
            verificationCountryId = countryId,
            currencyId,
            contactPersonName = "Mona Fahmy",
            contactPersonPhoneCountry = "EG",
            contactPersonPhoneCode = "+20",
            contactPersonPhoneNumber = "1001234567",
        });

    private static async Task<List<JsonElement>> CountryCurrenciesAsync(HttpClient admin, string countryId) =>
        (await ReadJsonAsync(await EnsureSuccessAsync(
            await admin.GetAsync(Url($"/admin/lookups/countries/{countryId}/currencies")))))
        .EnumerateArray().ToList();

    [Fact]
    public async Task SetupWithoutACurrencyUsesTheCountrysMainOne()
    {
        var response = await SetupAsync(await NewOrderTokenAsync(), SeedIds.Egypt);
        await EnsureSuccessAsync(response);

        // Egypt offers EGP and USD; its own currency is the main one.
        (await ReadJsonAsync(response)).GetProperty("currencyCode").GetString().Should().Be("EGP");
    }

    [Fact]
    public async Task TheAdminListMarksTheMainCurrency()
    {
        var admin = CreateClient(await AdminTokenAsync());

        var currencies = await CountryCurrenciesAsync(admin, SeedIds.Egypt);

        currencies.Should().HaveCount(2);
        currencies.Single(c => c.GetProperty("isDefault").GetBoolean())
            .GetProperty("code").GetString().Should().Be("EGP");
    }

    [Fact]
    public async Task AnAdministratorCanChangeWhichCurrencyIsMain()
    {
        var admin = CreateClient(await AdminTokenAsync());

        // Lebanon offers USD and LBP; nothing else in the suite sets up an order there.
        var countries = await ReadJsonAsync(await EnsureSuccessAsync(
            await admin.GetAsync(Url("/admin/lookups/countries?search=Lebanon&pageSize=5"))));
        var lebanon = countries.GetProperty("items").EnumerateArray()
            .First(c => c.GetProperty("code").GetString() == "LB")
            .GetProperty("id").GetString()!;

        var currencies = await CountryCurrenciesAsync(admin, lebanon);
        var ids = currencies.Select(c => c.GetProperty("id").GetString()!).ToList();
        var usd = currencies.Single(c => c.GetProperty("code").GetString() == "USD").GetProperty("id").GetString()!;
        var original = currencies.Single(c => c.GetProperty("isDefault").GetBoolean()).GetProperty("id").GetString()!;

        try
        {
            // Out of the box, the country's own currency.
            currencies.Single(c => c.GetProperty("isDefault").GetBoolean())
                .GetProperty("code").GetString().Should().Be("LBP");

            await EnsureSuccessAsync(await admin.PutAsJsonAsync(
                Url($"/admin/lookups/countries/{lebanon}/currencies"),
                new { currencyIds = ids, defaultCurrencyId = usd }));

            var afterChange = await CountryCurrenciesAsync(admin, lebanon);
            afterChange.Single(c => c.GetProperty("isDefault").GetBoolean())
                .GetProperty("code").GetString().Should().Be("USD");

            var response = await SetupAsync(await NewOrderTokenAsync(), lebanon);
            await EnsureSuccessAsync(response);
            (await ReadJsonAsync(response)).GetProperty("currencyCode").GetString().Should().Be("USD");

            // Saving the list without naming one keeps the current main currency.
            await EnsureSuccessAsync(await admin.PutAsJsonAsync(
                Url($"/admin/lookups/countries/{lebanon}/currencies"),
                new { currencyIds = ids }));
            (await CountryCurrenciesAsync(admin, lebanon))
                .Single(c => c.GetProperty("isDefault").GetBoolean())
                .GetProperty("code").GetString().Should().Be("USD");
        }
        finally
        {
            await admin.PutAsJsonAsync(
                Url($"/admin/lookups/countries/{lebanon}/currencies"),
                new { currencyIds = ids, defaultCurrencyId = original });
        }
    }

    [Fact]
    public async Task TheMainCurrencyHasToBeOneTheCountryOffers()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var ids = (await CountryCurrenciesAsync(admin, SeedIds.Egypt))
            .Select(c => c.GetProperty("id").GetString()!).ToList();

        var response = await admin.PutAsJsonAsync(
            Url($"/admin/lookups/countries/{SeedIds.Egypt}/currencies"),
            new { currencyIds = ids, defaultCurrencyId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Nothing changed.
        (await CountryCurrenciesAsync(admin, SeedIds.Egypt))
            .Single(c => c.GetProperty("isDefault").GetBoolean())
            .GetProperty("code").GetString().Should().Be("EGP");
    }

    [Fact]
    public async Task ACurrencySentExplicitlyIsStillHonoured()
    {
        var response = await SetupAsync(await NewOrderTokenAsync(), SeedIds.Egypt, SeedIds.Usd);
        await EnsureSuccessAsync(response);

        (await ReadJsonAsync(response)).GetProperty("currencyCode").GetString().Should().Be("USD");
    }
}
