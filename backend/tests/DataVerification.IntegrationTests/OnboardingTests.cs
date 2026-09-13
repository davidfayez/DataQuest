using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// Registration issues an order number and a generated password rather than asking the applicant to
/// choose credentials, and the order is inert until a country and a currency valid for it are set.
/// </summary>
public sealed class OnboardingTests : ApiTestBase
{
    public OnboardingTests(ApiFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Registration_issues_an_order_number_and_a_generated_password()
    {
        var response = await CreateClient().PostAsJsonAsync(
            Url("/orders/register"),
            new { email = $"it-{Guid.NewGuid():N}@example.com", languageCode = "en" });

        response.EnsureSuccessStatusCode();
        var body = await ReadJsonAsync(response);

        body.GetProperty("orderNumber").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("password").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_malformed_email_is_rejected_before_an_order_is_created()
    {
        var response = await CreateClient().PostAsJsonAsync(
            Url("/orders/register"),
            new { email = "not-an-email", languageCode = "en" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_unsupported_language_is_rejected()
    {
        var response = await CreateClient().PostAsJsonAsync(
            Url("/orders/register"),
            new { email = $"it-{Guid.NewGuid():N}@example.com", languageCode = "xx" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_currency_that_the_country_does_not_offer_is_refused()
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

        var token = login.GetProperty("accessToken").GetString();

        // Contact details are valid, so the only thing left to reject is the currency.
        var response = await CreateClient(token).PutAsJsonAsync(Url("/orders/setup"), new
        {
            verificationCountryId = SeedIds.Egypt,
            currencyId = Guid.NewGuid(),
            contactPersonName = "Mona Fahmy",
            contactPersonPhoneCountry = "EG",
            contactPersonPhoneCode = "+20",
            contactPersonPhoneNumber = "1001234567",
        });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Setup_records_the_contact_person_and_returns_the_joined_phone()
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

        var response = await CreateClient(login.GetProperty("accessToken").GetString())
            .PutAsJsonAsync(Url("/orders/setup"), new
            {
                verificationCountryId = SeedIds.Egypt,
                currencyId = SeedIds.Egp,
                contactPersonName = "  Mona Fahmy  ",
                contactPersonPhoneCountry = "eg",
                contactPersonPhoneCode = "+20",
                contactPersonPhoneNumber = "1001234567",
            });

        response.EnsureSuccessStatusCode();

        var result = await ReadJsonAsync(response);
        result.GetProperty("contactPersonName").GetString().Should().Be("Mona Fahmy");
        result.GetProperty("contactPersonPhone").GetString().Should().Be("+201001234567");
    }

    [Theory]
    // A name is mandatory, the dial code has to look like one, and the national number is digits
    // only — the prefix travels separately, so a number carrying it again is a mistake.
    [InlineData("", "EG", "+20", "1001234567")]
    [InlineData("Mona Fahmy", "EGY", "+20", "1001234567")]
    [InlineData("Mona Fahmy", "EG", "0020", "1001234567")]
    [InlineData("Mona Fahmy", "EG", "+20", "+201001234567")]
    [InlineData("Mona Fahmy", "EG", "+20", "123")]
    public async Task Setup_refuses_malformed_contact_details(
        string name,
        string phoneCountry,
        string phoneCode,
        string phoneNumber)
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

        var response = await CreateClient(login.GetProperty("accessToken").GetString())
            .PutAsJsonAsync(Url("/orders/setup"), new
            {
                verificationCountryId = SeedIds.Egypt,
                currencyId = SeedIds.Egp,
                contactPersonName = name,
                contactPersonPhoneCountry = phoneCountry,
                contactPersonPhoneCode = phoneCode,
                contactPersonPhoneNumber = phoneNumber,
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_lookup_cascade_is_scoped_to_the_orders_country()
    {
        var applicant = await RegisterApplicantAsync();
        var client = CreateClient(applicant.Token);

        var transactionTypes = await ReadJsonAsync(
            await client.GetAsync(Url("/transaction-types")));

        transactionTypes.EnumerateArray().Should().NotBeEmpty();

        var subTypes = await ReadJsonAsync(
            await client.GetAsync(Url($"/transaction-types/{SeedIds.TxEducational}/sub-types")));

        subTypes.EnumerateArray().Should().NotBeEmpty();

        // An id from outside the cascade is a 404, not an empty list — a silently empty response
        // would let a tampered id look like a legitimately empty branch.
        var unknown = await client.GetAsync(
            Url($"/transaction-types/{Guid.NewGuid()}/sub-types"));

        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
