using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// Verification authorities, transaction types and sub-transaction types each carry a description
/// in Arabic and English, and every save requires both.
/// </summary>
public sealed class LookupDescriptionTests : ApiTestBase
{
    public LookupDescriptionTests(ApiFactory factory) : base(factory) { }

    private const string Arabic = "وصف تجريبي";
    private const string English = "A description written for the test.";

    /// <summary>Every save needs a unique code now; these tests are about descriptions.</summary>
    private static string NewCode() => "D" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

    private static string TransactionTypes => Url("/admin/lookups/transaction-types");
    private static string SubTypes => Url("/admin/lookups/sub-transaction-types");
    private static string Authorities => Url("/admin/lookups/authorities");

    private static object TransactionType(string? ar, string? en) => new
    {
        code = NewCode(),
        countryIds = new[] { SeedIds.Egypt },
        nameAr = "نوع اختبار",
        nameEn = $"Described type {Guid.NewGuid():N}",
        isActive = true,
        descriptionAr = ar,
        descriptionEn = en,
    };

    private static object SubType(string? ar, string? en) => new
    {
        code = NewCode(),
        transactionTypeId = SeedIds.TxEducational,
        nameAr = "نوع فرعي اختبار",
        nameEn = $"Described sub-type {Guid.NewGuid():N}",
        isActive = true,
        countryIds = new[] { SeedIds.Egypt },
        descriptionAr = ar,
        descriptionEn = en,
    };

    private static object Authority(string? ar, string? en) => new
    {
        code = NewCode(),
        countryId = SeedIds.Egypt,
        nameAr = "جهة اختبار",
        nameEn = $"Described authority {Guid.NewGuid():N}",
        isActive = true,
        subTransactionTypeIds = new[] { SeedIds.SubBachelor },
        descriptionAr = ar,
        descriptionEn = en,
    };

    public static TheoryData<string> Endpoints => new() { "transaction-types", "sub-transaction-types", "authorities" };

    private static (string Url, Func<string?, string?, object> Body) For(string endpoint) => endpoint switch
    {
        "transaction-types" => (TransactionTypes, TransactionType),
        "sub-transaction-types" => (SubTypes, SubType),
        "authorities" => (Authorities, Authority),
        _ => throw new ArgumentOutOfRangeException(nameof(endpoint)),
    };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task ASaveWithBothDescriptionsKeepsThem(string endpoint)
    {
        var admin = CreateClient(await AdminTokenAsync());
        var (url, body) = For(endpoint);

        var response = await admin.PostAsJsonAsync(url, body($"  {Arabic}  ", English));
        await EnsureSuccessAsync(response);

        var saved = await response.Content.ReadFromJsonAsync<JsonElement>();

        try
        {
            // Trimmed on the way in, like the names.
            saved.GetProperty("descriptionAr").GetString().Should().Be(Arabic);
            saved.GetProperty("descriptionEn").GetString().Should().Be(English);
            saved.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            await admin.DeleteAsync($"{url}/{saved.GetProperty("id").GetGuid()}");
        }
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task ASaveMissingTheArabicDescriptionIsRefused(string endpoint)
    {
        var admin = CreateClient(await AdminTokenAsync());
        var (url, body) = For(endpoint);

        var response = await admin.PostAsJsonAsync(url, body("   ", English));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("DescriptionAr");
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task ASaveMissingTheEnglishDescriptionIsRefused(string endpoint)
    {
        var admin = CreateClient(await AdminTokenAsync());
        var (url, body) = For(endpoint);

        // Omitted entirely rather than blank — what an older client would send.
        var response = await admin.PostAsJsonAsync(url, body(Arabic, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("DescriptionEn");
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task ADescriptionLongerThanTheColumnIsRefused(string endpoint)
    {
        var admin = CreateClient(await AdminTokenAsync());
        var (url, body) = For(endpoint);

        var response = await admin.PostAsJsonAsync(url, body(Arabic, new string('x', 2001)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
