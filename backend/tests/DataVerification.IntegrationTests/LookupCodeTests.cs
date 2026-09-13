using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// Verification authorities, transaction types, sub-transaction types and service types each carry
/// a unique code: required, a restricted shape, stored upper-case, and never shared by two rows of
/// the same kind.
/// </summary>
public sealed class LookupCodeTests : ApiTestBase
{
    public LookupCodeTests(ApiFactory factory) : base(factory) { }

    private const string DescriptionAr = "وصف تجريبي";
    private const string DescriptionEn = "A description written for the test.";

    public static TheoryData<string> Endpoints => new()
    {
        "transaction-types", "sub-transaction-types", "authorities", "service-types",
    };

    /// <summary>A code no earlier run can have left behind.</summary>
    private static string FreshCode(string prefix) =>
        $"{prefix}-{Guid.NewGuid().ToString("N")[..10]}".ToUpperInvariant();

    /// <summary>A complete, valid body for the endpoint, with the given code and optional id.</summary>
    private static object Body(string endpoint, string? code, Guid? id = null) => endpoint switch
    {
        "transaction-types" => new
        {
            id,
            code,
            countryIds = new[] { SeedIds.Egypt },
            nameAr = "نوع اختبار",
            nameEn = "Coded type",
            isActive = true,
            descriptionAr = DescriptionAr,
            descriptionEn = DescriptionEn,
        },
        "sub-transaction-types" => new
        {
            id,
            code,
            transactionTypeId = SeedIds.TxEducational,
            nameAr = "نوع فرعي اختبار",
            nameEn = "Coded sub-type",
            isActive = true,
            countryIds = new[] { SeedIds.Egypt },
            descriptionAr = DescriptionAr,
            descriptionEn = DescriptionEn,
        },
        "authorities" => new
        {
            id,
            code,
            countryId = SeedIds.Egypt,
            nameAr = "جهة اختبار",
            nameEn = "Coded authority",
            isActive = true,
            subTransactionTypeIds = new[] { SeedIds.SubBachelor },
            descriptionAr = DescriptionAr,
            descriptionEn = DescriptionEn,
        },
        "service-types" => new
        {
            id,
            code,
            verificationAuthorityId = SeedIds.AuthoritySupremeCouncil,
            subTransactionTypeId = SeedIds.SubBachelor,
            nameAr = "خدمة اختبار",
            nameEn = "Coded service",
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
        },
        _ => throw new ArgumentOutOfRangeException(nameof(endpoint)),
    };

    private static string UrlFor(string endpoint) => Url($"/admin/lookups/{endpoint}");

    private async Task<JsonElement> CreateAsync(HttpClient admin, string endpoint, string code)
    {
        var response = await admin.PostAsJsonAsync(UrlFor(endpoint), Body(endpoint, code));
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Task DeleteAsync(HttpClient admin, string endpoint, JsonElement row) =>
        admin.DeleteAsync($"{UrlFor(endpoint)}/{row.GetProperty("id").GetGuid()}");

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task TheCodeIsKeptTrimmedAndUpperCase(string endpoint)
    {
        var admin = CreateClient(await AdminTokenAsync());
        var code = FreshCode("case");

        var response = await admin.PostAsJsonAsync(
            UrlFor(endpoint), Body(endpoint, $"  {code.ToLowerInvariant()}  "));
        await EnsureSuccessAsync(response);
        var saved = await response.Content.ReadFromJsonAsync<JsonElement>();

        try
        {
            // One spelling in storage, so "edu" and "EDU" cannot become two different codes.
            saved.GetProperty("code").GetString().Should().Be(code);
        }
        finally
        {
            await DeleteAsync(admin, endpoint, saved);
        }
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task ASecondRowCannotTakeACodeAlreadyInUse(string endpoint)
    {
        var admin = CreateClient(await AdminTokenAsync());
        var code = FreshCode("dup");
        var first = await CreateAsync(admin, endpoint, code);

        try
        {
            // Differently cased, so this proves the check is on the code and not on the spelling.
            var second = await admin.PostAsJsonAsync(
                UrlFor(endpoint), Body(endpoint, code.ToLowerInvariant()));

            second.StatusCode.Should().Be(HttpStatusCode.Conflict);

            var problem = await second.Content.ReadFromJsonAsync<JsonElement>();
            problem.GetProperty("code").GetString().Should().EndWith(".duplicate_code");
            problem.GetProperty("detail").GetString().Should().Contain(code);
        }
        finally
        {
            await DeleteAsync(admin, endpoint, first);
        }
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task ARowCanBeSavedAgainWithItsOwnCode(string endpoint)
    {
        var admin = CreateClient(await AdminTokenAsync());
        var code = FreshCode("self");
        var created = await CreateAsync(admin, endpoint, code);
        var id = created.GetProperty("id").GetGuid();

        try
        {
            // Editing a row must not trip over the code it already has.
            var updated = await admin.PostAsJsonAsync(UrlFor(endpoint), Body(endpoint, code, id));
            await EnsureSuccessAsync(updated);

            (await updated.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("code").GetString().Should().Be(code);
        }
        finally
        {
            await DeleteAsync(admin, endpoint, created);
        }
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task ASaveWithoutACodeIsRefused(string endpoint)
    {
        var admin = CreateClient(await AdminTokenAsync());

        var response = await admin.PostAsJsonAsync(UrlFor(endpoint), Body(endpoint, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Code");
    }

    [Theory]
    [InlineData("transaction-types", "A")]
    [InlineData("transaction-types", "HAS SPACE")]
    [InlineData("sub-transaction-types", "-LEADING")]
    [InlineData("authorities", "semi;colon")]
    [InlineData("service-types", "THIRTY-ONE-CHARACTERS-LONG-CODE")]
    public async Task ACodeOfTheWrongShapeIsRefused(string endpoint, string code)
    {
        var admin = CreateClient(await AdminTokenAsync());

        var response = await admin.PostAsJsonAsync(UrlFor(endpoint), Body(endpoint, code));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TheSameCodeMayBeUsedByDifferentKindsOfRecord()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var code = FreshCode("shared");

        // Unique within a kind, not across kinds: an authority and a service type are different
        // lists, and nothing looks one up by the other's code.
        var type = await CreateAsync(admin, "transaction-types", code);

        try
        {
            var authority = await CreateAsync(admin, "authorities", code);
            await DeleteAsync(admin, "authorities", authority);
        }
        finally
        {
            await DeleteAsync(admin, "transaction-types", type);
        }
    }
}
