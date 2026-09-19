using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// The payment-type form in the admin panel.
///
/// The kind is no longer chosen there: a new type is a transfer, an existing type keeps the kind it
/// has, and a description is required in both languages, as on the other described lookups.
/// </summary>
public sealed class PaymentTypeTests : ApiTestBase
{
    public PaymentTypeTests(ApiFactory factory) : base(factory) { }

    private static string Types => Url("/admin/payment-methods/types");

    private static string UniqueName() => $"Test type {Guid.NewGuid():N}";

    private static Dictionary<string, object?> Body(
        string name,
        Guid? id = null,
        string? descriptionAr = "وصف نوع الدفع",
        string? descriptionEn = "What this payment type is for",
        bool accounts = true,
        bool link = false) => new()
        {
            ["id"] = id,
            ["nameAr"] = $"{name} ar",
            ["nameEn"] = name,
            ["descriptionAr"] = descriptionAr,
            ["descriptionEn"] = descriptionEn,
            ["requiresAccountNumber"] = accounts,
            ["requiresBarcode"] = false,
            ["requiresBank"] = false,
            ["requiresExternalUrl"] = link,
            ["requiresProofDocument"] = true,
            ["requiresReferenceNumber"] = true,
            ["sortOrder"] = 0,
            ["isActive"] = true,
        };

    [Fact]
    public async Task ANewTypeIsATransferAndKeepsItsDescriptions()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var name = UniqueName();

        var saved = await ReadJsonAsync(await EnsureSuccessAsync(
            await admin.PostAsJsonAsync(Types, Body(name))));
        var id = saved.GetProperty("id").GetGuid();

        try
        {
            saved.GetProperty("kindName").GetString().Should().Be("Transfer");
            saved.GetProperty("needsApproval").GetBoolean().Should().BeTrue();
            saved.GetProperty("descriptionAr").GetString().Should().Be("وصف نوع الدفع");
            saved.GetProperty("descriptionEn").GetString().Should().Be("What this payment type is for");

            // The list is what the table and the edit form read.
            var list = await ReadJsonAsync(await admin.GetAsync(
                $"{Types}?search={Uri.EscapeDataString(name)}"));
            var row = list.GetProperty("items").EnumerateArray()
                .Single(item => item.GetProperty("id").GetGuid() == id);
            row.GetProperty("description").GetString().Should().Be("What this payment type is for");
        }
        finally
        {
            await admin.DeleteAsync($"{Types}/{id}");
        }
    }

    [Fact]
    public async Task AKindSentByAnOlderClientIsIgnored()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var body = Body(UniqueName());
        body["kind"] = 4; // PayPal

        var saved = await ReadJsonAsync(await EnsureSuccessAsync(
            await admin.PostAsJsonAsync(Types, body)));

        try
        {
            saved.GetProperty("kindName").GetString().Should().Be("Transfer");
        }
        finally
        {
            await admin.DeleteAsync($"{Types}/{saved.GetProperty("id").GetGuid()}");
        }
    }

    [Theory]
    [InlineData(null, "English")]
    [InlineData("وصف", null)]
    [InlineData("   ", "English")]
    public async Task ATypeWithoutBothDescriptionsIsRefused(string? arabic, string? english)
    {
        var admin = CreateClient(await AdminTokenAsync());

        var response = await admin.PostAsJsonAsync(
            Types, Body(UniqueName(), descriptionAr: arabic, descriptionEn: english));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ADescriptionLongerThanTheColumnIsRefused()
    {
        var admin = CreateClient(await AdminTokenAsync());

        var response = await admin.PostAsJsonAsync(
            Types, Body(UniqueName(), descriptionEn: new string('a', 2001)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ATransferTypeWithNoWayToPayIsRefused()
    {
        var admin = CreateClient(await AdminTokenAsync());

        var response = await admin.PostAsJsonAsync(
            Types, Body(UniqueName(), accounts: false, link: false));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EditingAPayPalTypeKeepsItPayPal()
    {
        var admin = CreateClient(await AdminTokenAsync());

        var list = await ReadJsonAsync(await admin.GetAsync($"{Types}?pageSize=200"));
        var payPal = list.GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("kindName").GetString() == "PayPal")
            .Select(item => (JsonElement?)item)
            .FirstOrDefault();

        // Seeded in Development; nothing to check on a database that has none.
        if (payPal is not { } row) return;

        // A PayPal type asks for neither numbers nor a link, which a transfer type may not do —
        // so this save only succeeds if the stored kind is the one being checked.
        var body = Body(
            row.GetProperty("nameEn").GetString()!,
            id: row.GetProperty("id").GetGuid(),
            accounts: false,
            link: false);
        body["nameAr"] = row.GetProperty("nameAr").GetString();
        body["requiresProofDocument"] = row.GetProperty("requiresProofDocument").GetBoolean();
        body["requiresReferenceNumber"] = row.GetProperty("requiresReferenceNumber").GetBoolean();
        body["sortOrder"] = row.GetProperty("sortOrder").GetInt32();
        body["isActive"] = row.GetProperty("isActive").GetBoolean();

        var saved = await ReadJsonAsync(await EnsureSuccessAsync(
            await admin.PostAsJsonAsync(Types, body)));

        saved.GetProperty("kindName").GetString().Should().Be("PayPal");
        saved.GetProperty("needsApproval").GetBoolean().Should().BeFalse();
    }
}
