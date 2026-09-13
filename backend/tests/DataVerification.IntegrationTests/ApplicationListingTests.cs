using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// The applicant's grid: search, filters, sorting and paging. These run against a shared database
/// that other suites also write to, so every assertion is scoped to an order this test registered.
/// </summary>
public sealed class ApplicationListingTests : ApiTestBase
{
    public ApplicationListingTests(ApiFactory factory) : base(factory) { }

    [Fact]
    public async Task SearchMatchesTheAddressee()
    {
        var applicant = await RegisterApplicantAsync();
        var client = CreateClient(applicant.Token);

        await CreateDraftAsync(applicant.Token);
        var target = await CreateDraftForAsync(applicant.Token, "Directorate of Passports");

        var results = await ItemsAsync(client, "?search=Passports");

        results.Should().ContainSingle()
            .Which.GetProperty("id").GetString()
            .Should().Be(target.GetProperty("id").GetString());
    }

    [Theory]
    // The English name, then the Arabic one — the grid's search has to find either script.
    [InlineData("Layla")]
    [InlineData("Hassan")]
    [InlineData("ليلى")]
    [InlineData("حسن")]
    public async Task SearchMatchesTheApplicantNameInEitherScript(string term)
    {
        var applicant = await RegisterApplicantAsync();
        var client = CreateClient(applicant.Token);

        await CreateDraftAsync(applicant.Token);

        var results = await ItemsAsync(client, $"?search={Uri.EscapeDataString(term)}");

        results.Should().ContainSingle();
    }

    [Fact]
    public async Task SearchMatchesTheApplicationNumber()
    {
        var applicant = await RegisterApplicantAsync();
        var client = CreateClient(applicant.Token);

        var draft = await CreateDraftAsync(applicant.Token);
        var number = draft.GetProperty("applicationNumber").GetString()!;

        var results = await ItemsAsync(client, $"?search={number}");

        results.Should().ContainSingle()
            .Which.GetProperty("applicationNumber").GetString().Should().Be(number);
    }

    [Fact]
    public async Task SearchThatMatchesNothingReturnsAnEmptyPageRatherThanEverything()
    {
        var applicant = await RegisterApplicantAsync();
        var client = CreateClient(applicant.Token);

        await CreateDraftAsync(applicant.Token);

        var results = await ItemsAsync(client, "?search=zzz-no-such-application");

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task RowsCarryTheApplicantNameAndTheCascadeLabels()
    {
        var applicant = await RegisterApplicantAsync();
        var client = CreateClient(applicant.Token);

        await CreateDraftAsync(applicant.Token);

        var row = (await ItemsAsync(client, string.Empty)).Single();

        row.GetProperty("applicantNameEn").GetString().Should().Be("Layla Hassan");
        row.GetProperty("applicantNameAr").GetString().Should().Be("ليلى حسن");
        row.GetProperty("transactionTypeName").GetString().Should().NotBeNullOrWhiteSpace();
        row.GetProperty("subTransactionTypeName").GetString().Should().NotBeNullOrWhiteSpace();
        row.GetProperty("verificationAuthorityName").GetString().Should().NotBeNullOrWhiteSpace();
        row.GetProperty("serviceTypeNames").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task FiltersNarrowTheGridAndAnUnrelatedValueEmptiesIt()
    {
        var applicant = await RegisterApplicantAsync();
        var client = CreateClient(applicant.Token);

        await CreateDraftAsync(applicant.Token);

        (await ItemsAsync(client, $"?transactionTypeId={SeedIds.TxEducational}")).Should().ContainSingle();
        (await ItemsAsync(client, $"?subTransactionTypeId={SeedIds.SubBachelor}")).Should().ContainSingle();
        (await ItemsAsync(client, $"?verificationAuthorityId={SeedIds.AuthoritySupremeCouncil}"))
            .Should().ContainSingle();
        (await ItemsAsync(client, $"?serviceTypeId={SeedIds.ServiceStandardVerification}"))
            .Should().ContainSingle();

        // A different branch of the cascade must not match.
        (await ItemsAsync(client, $"?transactionTypeId={SeedIds.TxSecurity}")).Should().BeEmpty();
    }

    [Fact]
    public async Task FiltersCombineAsAnIntersection()
    {
        var applicant = await RegisterApplicantAsync();
        var client = CreateClient(applicant.Token);

        await CreateDraftAsync(applicant.Token);

        var both = await ItemsAsync(
            client,
            $"?transactionTypeId={SeedIds.TxEducational}&status=0");
        both.Should().ContainSingle();

        // Same transaction type, a status the row is not in: the AND has to win.
        var contradiction = await ItemsAsync(
            client,
            $"?transactionTypeId={SeedIds.TxEducational}&status=5");
        contradiction.Should().BeEmpty();
    }

    [Fact]
    public async Task SortingIsAppliedAcrossThePageAndCanBeReversed()
    {
        var applicant = await RegisterApplicantAsync();
        var client = CreateClient(applicant.Token);

        await CreateDraftForAsync(applicant.Token, "Alpha Authority");
        await CreateDraftForAsync(applicant.Token, "Zulu Authority");
        await CreateDraftForAsync(applicant.Token, "Mike Authority");

        var ascending = await ItemsAsync(client, "?sortBy=addressedTo&sortDescending=false");
        ascending.Select(row => row.GetProperty("addressedTo").GetString())
            .Should().ContainInOrder("Alpha Authority", "Mike Authority", "Zulu Authority");

        var descending = await ItemsAsync(client, "?sortBy=addressedTo&sortDescending=true");
        descending.Select(row => row.GetProperty("addressedTo").GetString())
            .Should().ContainInOrder("Zulu Authority", "Mike Authority", "Alpha Authority");
    }

    [Theory]
    [InlineData("transactionType")]
    [InlineData("subTransactionType")]
    [InlineData("authority")]
    [InlineData("serviceCount")]
    [InlineData("isPaid")]
    public async Task TheAddedColumnsAreSortableInBothDirections(string sortBy)
    {
        var applicant = await RegisterApplicantAsync();
        var client = CreateClient(applicant.Token);

        await CreateDraftForAsync(applicant.Token, "Alpha Authority");
        await CreateDraftForAsync(applicant.Token, "Zulu Authority");

        // Each key must be accepted and return the full set — a sort must never drop rows, which
        // is what an inner join on a null lookup would silently do.
        var ascending = await ItemsAsync(client, $"?sortBy={sortBy}&sortDescending=false");
        var descending = await ItemsAsync(client, $"?sortBy={sortBy}&sortDescending=true");

        ascending.Should().HaveCount(2);
        descending.Should().HaveCount(2);
    }

    [Fact]
    public async Task SortingByLookupNameFollowsTheRequestedLanguage()
    {
        var applicant = await RegisterApplicantAsync();
        var client = CreateClient(applicant.Token);

        await CreateDraftForAsync(applicant.Token, "Only One");

        // The Arabic path is a different expression tree; exercising it proves it translates.
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            Url("/applications?sortBy=transactionType&sortDescending=false"));
        request.Headers.Add("Accept-Language", "ar");

        var page = await ReadJsonAsync(await EnsureSuccessAsync(await client.SendAsync(request)));

        page.GetProperty("items").GetArrayLength().Should().Be(1);
        page.GetProperty("items")[0].GetProperty("transactionTypeName").GetString()
            .Should().NotBe("Educational Certificate Verification");
    }

    [Fact]
    public async Task AnUnknownSortKeyFallsBackToNewestFirstInsteadOfFailing()
    {
        var applicant = await RegisterApplicantAsync();
        var client = CreateClient(applicant.Token);

        await CreateDraftForAsync(applicant.Token, "First");
        await CreateDraftForAsync(applicant.Token, "Second");

        var results = await ItemsAsync(client, "?sortBy=--nonsense--");

        results.Select(row => row.GetProperty("addressedTo").GetString())
            .Should().ContainInOrder("Second", "First");
    }

    [Fact]
    public async Task PagingSplitsTheResultsAndReportsTheTotals()
    {
        var applicant = await RegisterApplicantAsync();
        var client = CreateClient(applicant.Token);

        for (var index = 0; index < 3; index++)
        {
            await CreateDraftForAsync(applicant.Token, $"Body {index}");
        }

        var first = await ReadJsonAsync(await EnsureSuccessAsync(
            await client.GetAsync(Url("/applications?page=1&pageSize=2"))));

        first.GetProperty("totalCount").GetInt32().Should().Be(3);
        first.GetProperty("totalPages").GetInt32().Should().Be(2);
        first.GetProperty("hasNext").GetBoolean().Should().BeTrue();
        first.GetProperty("items").GetArrayLength().Should().Be(2);

        var second = await ReadJsonAsync(await EnsureSuccessAsync(
            await client.GetAsync(Url("/applications?page=2&pageSize=2"))));

        second.GetProperty("items").GetArrayLength().Should().Be(1);
        second.GetProperty("hasPrevious").GetBoolean().Should().BeTrue();
        second.GetProperty("hasNext").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task FilterOptionsDescribeOnlyTheCallersOwnApplications()
    {
        var applicant = await RegisterApplicantAsync();
        var client = CreateClient(applicant.Token);

        await CreateDraftAsync(applicant.Token);
        await CreateDraftAsync(applicant.Token);

        var options = await ReadJsonAsync(await EnsureSuccessAsync(
            await client.GetAsync(Url("/applications/filters"))));

        var transactionTypes = options.GetProperty("transactionTypes").EnumerateArray().ToList();
        transactionTypes.Should().ContainSingle();
        transactionTypes[0].GetProperty("count").GetInt32().Should().Be(2);
        transactionTypes[0].GetProperty("name").GetString().Should().NotBeNullOrWhiteSpace();

        var statuses = options.GetProperty("statuses").EnumerateArray().ToList();
        statuses.Should().ContainSingle();
        statuses[0].GetProperty("statusName").GetString().Should().Be("Draft");
        statuses[0].GetProperty("count").GetInt32().Should().Be(2);

        // A brand-new order has nothing yet, which is what proves the options are order-scoped.
        var newcomer = await RegisterApplicantAsync();
        var empty = await ReadJsonAsync(await EnsureSuccessAsync(
            await CreateClient(newcomer.Token).GetAsync(Url("/applications/filters"))));

        empty.GetProperty("transactionTypes").GetArrayLength().Should().Be(0);
        empty.GetProperty("statuses").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task SearchNeverReachesAnotherOrdersApplications()
    {
        var owner = await RegisterApplicantAsync();
        var stranger = await RegisterApplicantAsync();

        var theirs = await CreateDraftForAsync(owner.Token, "Confidential Body");
        var number = theirs.GetProperty("applicationNumber").GetString()!;

        // Searching by an exact reference belonging to someone else still returns nothing.
        var results = await ItemsAsync(CreateClient(stranger.Token), $"?search={number}");

        results.Should().BeEmpty();
    }

    private async Task<List<JsonElement>> ItemsAsync(HttpClient client, string queryString)
    {
        var page = await ReadJsonAsync(await EnsureSuccessAsync(
            await client.GetAsync(Url($"/applications{queryString}"))));

        return page.GetProperty("items").EnumerateArray().ToList();
    }

    /// <summary>A draft identical to the shared fixture's, but addressed to a chosen body.</summary>
    private async Task<JsonElement> CreateDraftForAsync(string token, string addressedTo)
    {
        var response = await CreateClient(token).PostAsJsonAsync(Url("/applications"), new
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
        });

        await EnsureSuccessAsync(response);
        return await ReadJsonAsync(response);
    }
}
