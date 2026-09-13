using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// "Forgot password", which no longer takes the old password away.
///
/// The rule the whole feature turns on: the emailed password is an offer. Until somebody signs in
/// with it the password already in the applicant's hands still works, so a reset requested by
/// someone else locks nobody out. Signing in with it is what commits it.
/// </summary>
public sealed class PasswordResetTests : ApiTestBase
{
    public PasswordResetTests(ApiFactory factory) : base(factory) { }

    private static string Forgot => Url("/orders/forgot-password");
    private static string Login => Url("/orders/login");
    private static string ResetLog => Url("/admin/settings/password-resets");
    private static string Validity => Url("/admin/settings/password-reset-validity");

    /// <summary>
    /// Asks for a new password and returns it.
    ///
    /// Development echoes it in the response — the same escape hatch registration has — because a
    /// test host has no encryption key, so the stored secret cannot be read back the way support
    /// reads it in production.
    /// </summary>
    private async Task<string> RequestResetAsync(string orderNumber)
    {
        var response = await CreateClient().PostAsJsonAsync(Forgot, new { orderNumber });
        await EnsureSuccessAsync(response);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("password").GetString()!;
    }

    private async Task<HttpStatusCode> TryLoginAsync(string orderNumber, string password)
    {
        var response = await CreateClient().PostAsJsonAsync(Login, new { orderNumber, password });
        return response.StatusCode;
    }

    [Fact]
    public async Task TheOldPasswordStillWorksWhileTheNewOneIsUnused()
    {
        var applicant = await RegisterApplicantAsync();

        await EnsureSuccessAsync(
            await CreateClient().PostAsJsonAsync(Forgot, new { orderNumber = applicant.OrderNumber }));

        // The whole point: asking for a reset does not lock anybody out of what they already have.
        (await TryLoginAsync(applicant.OrderNumber, applicant.Password))
            .Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TheResponseSaysHowLongTheNewPasswordLasts()
    {
        var applicant = await RegisterApplicantAsync();

        var response = await CreateClient().PostAsJsonAsync(
            Forgot, new { orderNumber = applicant.OrderNumber });

        await EnsureSuccessAsync(response);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // The page tells the applicant the same thing the email does.
        body.GetProperty("validityMinutes").GetInt32().Should().BeGreaterThan(0);
        body.GetProperty("maskedEmail").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task SigningInWithTheNewPasswordMakesItTheRealOne()
    {
        var applicant = await RegisterApplicantAsync();
        var issued = await RequestResetAsync(applicant.OrderNumber);

        issued.Should().NotBe(applicant.Password, "a reset issues a different password");

        // Using it commits it…
        (await TryLoginAsync(applicant.OrderNumber, issued)).Should().Be(HttpStatusCode.OK);

        // …and the old one stops working from that moment, not before.
        (await TryLoginAsync(applicant.OrderNumber, applicant.Password))
            .Should().Be(HttpStatusCode.Unauthorized);

        (await TryLoginAsync(applicant.OrderNumber, issued)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SigningInWithTheOldPasswordCancelsTheOffer()
    {
        var applicant = await RegisterApplicantAsync();
        var issued = await RequestResetAsync(applicant.OrderNumber);

        // Using the old password says the reset was not needed — by the applicant, or by whoever
        // asked for it on their behalf.
        (await TryLoginAsync(applicant.OrderNumber, applicant.Password))
            .Should().Be(HttpStatusCode.OK);

        (await TryLoginAsync(applicant.OrderNumber, issued))
            .Should().Be(HttpStatusCode.Unauthorized);

        (await TryLoginAsync(applicant.OrderNumber, applicant.Password))
            .Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AskingTwiceLeavesOnlyTheNewestPasswordWorking()
    {
        var applicant = await RegisterApplicantAsync();
        var first = await RequestResetAsync(applicant.OrderNumber);
        var second = await RequestResetAsync(applicant.OrderNumber);

        first.Should().NotBe(second);

        // One live offer at a time, or a mailbox full of old resets would each still be a way in.
        (await TryLoginAsync(applicant.OrderNumber, first)).Should().Be(HttpStatusCode.Unauthorized);
        (await TryLoginAsync(applicant.OrderNumber, second)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TheRequestIsLoggedWithWhereItCameFrom()
    {
        var applicant = await RegisterApplicantAsync();

        await EnsureSuccessAsync(
            await CreateClient().PostAsJsonAsync(Forgot, new { orderNumber = applicant.OrderNumber }));

        var admin = CreateClient(await AdminTokenAsync());
        var response = await admin.GetAsync($"{ResetLog}?page=1&pageSize=20&search={applicant.OrderNumber}");
        await EnsureSuccessAsync(response);

        var entry = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().First();

        entry.GetProperty("orderNumber").GetString().Should().Be(applicant.OrderNumber);
        entry.GetProperty("orderId").GetGuid().Should().Be(applicant.OrderId);
        entry.GetProperty("maskedEmail").GetString().Should().NotBeNullOrWhiteSpace();
        entry.GetProperty("outcome").GetString().Should().Be("Pending");
        entry.GetProperty("validityMinutes").GetInt32().Should().BeGreaterThan(0);
        entry.GetProperty("requestedAtUtc").GetDateTime().Should().BeAfter(DateTime.UtcNow.AddMinutes(-5));
    }

    [Fact]
    public async Task TheMaskedAddressHidesTheFrontOfTheDomain()
    {
        var applicant = await RegisterApplicantAsync();

        var response = await CreateClient().PostAsJsonAsync(
            Forgot, new { orderNumber = applicant.OrderNumber });

        await EnsureSuccessAsync(response);
        var masked = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("maskedEmail").GetString()!;

        // The shape the applicant sees: enough of each half to recognise, never a whole one.
        masked.Should().MatchRegex(@"^.+\*{5}.+@\*{2}[^@*]+\.[a-z]+$");
        masked.Should().NotContain(applicant.Email.Split('@')[1].Split('.')[0]);
    }

    [Fact]
    public async Task TheLogCarriesEveryFieldThePanelShows()
    {
        var applicant = await RegisterApplicantAsync();

        var request = new HttpRequestMessage(HttpMethod.Post, Forgot)
        {
            Content = JsonContent.Create(new { orderNumber = applicant.OrderNumber }),
        };
        request.Headers.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) "
            + "Chrome/141.0.0.0 Safari/537.36");
        request.Headers.TryAddWithoutValidation("Accept-Language", "en-GB,en;q=0.9");

        await EnsureSuccessAsync(await CreateClient().SendAsync(request));

        var admin = CreateClient(await AdminTokenAsync());
        var response = await admin.GetAsync($"{ResetLog}?page=1&pageSize=20&search={applicant.OrderNumber}");
        await EnsureSuccessAsync(response);

        var entry = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().First();

        // Read out of the request itself, so these hold wherever the test runs.
        entry.GetProperty("browser").GetString().Should().Be("Chrome 141");
        entry.GetProperty("operatingSystem").GetString().Should().Be("Windows 10 or 11");
        entry.GetProperty("deviceType").GetString().Should().Be("Desktop");
        // Not an exact match: HttpClient re-formats the header on its way out, so the spacing here
        // belongs to the HTTP stack rather than to anything this code decides.
        entry.GetProperty("acceptLanguage").GetString().Should().StartWith("en-GB,en;");

        // The geolocated fields are empty against a loopback address, but the panel binds to them,
        // so the contract is that they are present and null rather than missing.
        foreach (var field in new[]
                 {
                     "continent", "continentCode", "region", "regionName", "district",
                     "postalCode", "latitude", "longitude", "timeZone", "utcOffsetSeconds",
                     "currency", "isp", "organisation", "autonomousSystem", "reverseDns",
                     "isMobileNetwork", "isProxy", "isHosting",
                 })
        {
            entry.TryGetProperty(field, out _).Should().BeTrue($"the panel reads {field}");
        }
    }

    [Fact]
    public async Task TheLogSaysWhatBecameOfTheOffer()
    {
        var applicant = await RegisterApplicantAsync();
        var issued = await RequestResetAsync(applicant.OrderNumber);

        await TryLoginAsync(applicant.OrderNumber, issued);

        var admin = CreateClient(await AdminTokenAsync());
        var response = await admin.GetAsync($"{ResetLog}?page=1&pageSize=20&search={applicant.OrderNumber}");
        await EnsureSuccessAsync(response);

        var entry = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().First();

        // A row that stays "Pending" for ever would tell an operator nothing.
        entry.GetProperty("outcome").GetString().Should().Be("Used");
        entry.GetProperty("usedAtUtc").GetDateTime().Should().BeAfter(DateTime.UtcNow.AddMinutes(-5));
    }

    [Fact]
    public async Task ARequestForAnOrderThatDoesNotExistIsStillLogged()
    {
        var unknown = $"NEN{Random.Shared.Next(100_000_000, 999_999_999)}";

        await EnsureSuccessAsync(
            await CreateClient().PostAsJsonAsync(Forgot, new { orderNumber = unknown }));

        var admin = CreateClient(await AdminTokenAsync());
        var response = await admin.GetAsync($"{ResetLog}?page=1&pageSize=20&search={unknown}");
        await EnsureSuccessAsync(response);

        var entry = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().First();

        // A run of these against numbers that do not exist is the pattern most worth seeing.
        entry.GetProperty("outcome").GetString().Should().Be("UnknownOrder");
        entry.GetProperty("orderId").ValueKind.Should().Be(JsonValueKind.Null);
        entry.GetProperty("maskedEmail").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task TheWindowIsSixtyMinutesUnlessAnOperatorSaysOtherwise()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var response = await admin.GetAsync(Validity);
        await EnsureSuccessAsync(response);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("validityMinutes").GetInt32().Should().Be(60);
        body.GetProperty("minMinutes").GetInt32().Should().Be(1);
        body.GetProperty("maxMinutes").GetInt32().Should().Be(10_080);
    }

    [Fact]
    public async Task ChangingTheWindowAppliesToTheNextReset()
    {
        var admin = CreateClient(await AdminTokenAsync());

        try
        {
            await EnsureSuccessAsync(
                await admin.PutAsJsonAsync(Validity, new { validityMinutes = 15 }));

            var applicant = await RegisterApplicantAsync();
            var response = await CreateClient().PostAsJsonAsync(
                Forgot, new { orderNumber = applicant.OrderNumber });

            await EnsureSuccessAsync(response);
            (await response.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("validityMinutes").GetInt32().Should().Be(15);
        }
        finally
        {
            await admin.PutAsJsonAsync(Validity, new { validityMinutes = 60 });
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10_081)]
    public async Task AWindowOutsideTheAllowedRangeIsRefused(int minutes)
    {
        var admin = CreateClient(await AdminTokenAsync());

        var response = await admin.PutAsJsonAsync(Validity, new { validityMinutes = minutes });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TheLogAndTheWindowNeedAnAdminSession()
    {
        var anonymous = CreateClient();

        (await anonymous.GetAsync(ResetLog)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync(Validity)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PutAsJsonAsync(Validity, new { validityMinutes = 5 }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
