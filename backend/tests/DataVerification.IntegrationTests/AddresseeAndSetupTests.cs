using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// Three rules about what an applicant is offered and what is kept from what they type: the
/// country list, the addressee list, and the shape a phone number is stored in.
/// </summary>
public sealed class AddresseeAndSetupTests : ApiTestBase
{
    public AddresseeAndSetupTests(ApiFactory factory) : base(factory) { }

    private static string Countries => Url("/countries");
    private static string Addressees => Url("/addressees");
    private static string AdminAddressees => Url("/admin/lookups/addressees");

    [Fact]
    public async Task ACountryWithNoCurrencyIsNotOffered()
    {
        var admin = CreateClient(await AdminTokenAsync());

        // A country nobody has mapped a currency to yet — the state a half-configured one is left
        // in, and a dead end for an applicant: choosing it fixes the wallet's currency, so the
        // next step would have nothing to show and no way forward.
        // QM–QZ: the part of the Q block ISO 3166 reserves for user assignment. QA is Qatar, so a
        // random second letter across the whole alphabet collided with a real country 1 time in 26.
        var code = $"Q{(char)Random.Shared.Next('M', 'Z' + 1)}";
        var created = await admin.PostAsJsonAsync(
            Url("/admin/lookups/countries"),
            new { code, phoneCode = "+999", nameAr = "بلا عملة", nameEn = "Currencyless", isActive = true });

        await EnsureSuccessAsync(created);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        try
        {
            var applicant = await RegisterApplicantAsync();
            var response = await CreateClient(applicant.Token).GetAsync(Countries);
            await EnsureSuccessAsync(response);

            var offered = (await response.Content.ReadFromJsonAsync<JsonElement>())
                .EnumerateArray()
                .Select(c => c.GetProperty("id").GetGuid())
                .ToList();

            offered.Should().NotContain(id);

            // Still a real list, so this is not passing by returning nothing at all.
            offered.Should().Contain(Guid.Parse(SeedIds.Egypt));
        }
        finally
        {
            await admin.DeleteAsync(Url($"/admin/lookups/countries/{id}"));
        }
    }

    [Fact]
    public async Task EveryCountryOfferedHasACurrencyBehindIt()
    {
        var applicant = await RegisterApplicantAsync();
        var admin = CreateClient(await AdminTokenAsync());

        var response = await CreateClient(applicant.Token).GetAsync(Countries);
        await EnsureSuccessAsync(response);

        var countries = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
        countries.Should().NotBeEmpty();

        foreach (var country in countries)
        {
            var id = country.GetProperty("id").GetGuid();
            var currencies = await admin.GetAsync(Url($"/admin/lookups/countries/{id}/currencies"));
            await EnsureSuccessAsync(currencies);

            (await currencies.Content.ReadFromJsonAsync<JsonElement>())
                .EnumerateArray()
                .Should().NotBeEmpty($"country {country.GetProperty("code")} is offered to applicants");
        }
    }

    [Fact]
    public async Task TheAddresseeListIsOfferedToApplicants()
    {
        var applicant = await RegisterApplicantAsync();

        var response = await CreateClient(applicant.Token).GetAsync(Addressees);
        await EnsureSuccessAsync(response);

        var items = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();

        // The seeder puts a starting set in, so a fresh install offers something.
        items.Should().NotBeEmpty();
        items[0].GetProperty("name").GetString().Should().NotBeNullOrWhiteSpace();

        // Ordered by the operator's own position first, which is the point of having the column.
        var orders = items.Select(i => i.GetProperty("sortOrder").GetInt32()).ToList();
        orders.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task AnApplicationMayBeAddressedToSomethingNotOnTheList()
    {
        var applicant = await RegisterApplicantAsync();
        var draft = await CreateDraftAsync(applicant.Token);

        const string custom = "A body that is deliberately not in the lookup";

        var updated = await CreateClient(applicant.Token).PutAsJsonAsync(
            Url($"/applications/{draft.GetProperty("id").GetString()}"),
            BuildUpdate(draft, custom));

        await EnsureSuccessAsync(updated);

        // The list saves typing; it does not decide what an application may say.
        (await updated.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("addressedTo").GetString().Should().Be(custom);
    }

    /// <summary>The draft resent unchanged apart from who it is addressed to.</summary>
    private static object BuildUpdate(JsonElement draft, string addressedTo) => new
    {
        addressedTo,
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
        services = new[]
        {
            new
            {
                serviceTypeId = SeedIds.ServiceStandardVerification,
                quantity = 1,
                languageCode = "en",
                isExpress = false,
            },
        },
    };

    [Fact]
    public async Task OnlyActiveAddresseesAreOffered()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var name = $"Retired body {Guid.NewGuid():N}";

        var created = await admin.PostAsJsonAsync(
            AdminAddressees,
            new { nameAr = name, nameEn = name, sortOrder = 900, isActive = false });

        await EnsureSuccessAsync(created);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        try
        {
            var applicant = await RegisterApplicantAsync();
            var response = await CreateClient(applicant.Token).GetAsync(Addressees);
            await EnsureSuccessAsync(response);

            (await response.Content.ReadFromJsonAsync<JsonElement>())
                .EnumerateArray()
                .Select(i => i.GetProperty("nameEn").GetString())
                .Should().NotContain(name);
        }
        finally
        {
            await admin.DeleteAsync($"{AdminAddressees}/{id}");
        }
    }

    [Fact]
    public async Task TwoAddresseesCannotShareAName()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var name = $"Duplicate body {Guid.NewGuid():N}";

        var first = await admin.PostAsJsonAsync(
            AdminAddressees,
            new { nameAr = name, nameEn = name, sortOrder = 0, isActive = true });

        await EnsureSuccessAsync(first);
        var id = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        try
        {
            // Two entries reading the same would be indistinguishable in the applicant's list.
            var second = await admin.PostAsJsonAsync(
                AdminAddressees,
                new { nameAr = name, nameEn = name, sortOrder = 0, isActive = true });

            second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
        finally
        {
            await admin.DeleteAsync($"{AdminAddressees}/{id}");
        }
    }

    [Fact]
    public async Task TheAddresseeListNeedsAnAdminSessionToEdit()
    {
        var anonymous = CreateClient();

        (await anonymous.GetAsync(AdminAddressees)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync(
                AdminAddressees,
                new { nameAr = "x", nameEn = "x", sortOrder = 0, isActive = true }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ThePhoneNumberIsStoredWithoutItsTrunkZero()
    {
        // A fresh order, because setup runs once and the shared fixture has already used it.
        var token = await RegisterUnconfiguredOrderAsync();

        var response = await CreateClient(token).PutAsJsonAsync(
            Url("/orders/setup"),
            new
            {
                verificationCountryId = SeedIds.Egypt,
                currencyId = SeedIds.Egp,
                contactPersonName = "Phone Test",
                contactPersonPhoneCountry = "EG",
                contactPersonPhoneCode = "+20",
                contactPersonPhoneNumber = "01208691253",
            });

        await EnsureSuccessAsync(response);

        // The zero is a trunk prefix. Kept, the stored number would read +2001208691253 and could
        // not be dialled from outside Egypt.
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("contactPersonPhone").GetString().Should().Be("+201208691253");
    }

    /// <summary>Registers and signs in an order without completing its setup.</summary>
    private async Task<string> RegisterUnconfiguredOrderAsync()
    {
        var client = CreateClient();
        var email = $"phone-{Guid.NewGuid():N}@example.com";

        var registration = await client.PostAsJsonAsync(
            Url("/orders/register"),
            new { email, fullName = "Phone Test", phone = "+201001234567", languageCode = "en", countryCode = "EG" });

        await EnsureSuccessAsync(registration);
        var registered = await registration.Content.ReadFromJsonAsync<JsonElement>();

        var login = await client.PostAsJsonAsync(
            Url("/orders/login"),
            new
            {
                orderNumber = registered.GetProperty("orderNumber").GetString(),
                password = registered.GetProperty("password").GetString(),
            });

        await EnsureSuccessAsync(login);

        return (await login.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accessToken").GetString()!;
    }
}
