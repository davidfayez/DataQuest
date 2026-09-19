using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// A wallet holding a balance in every currency the order's country offers.
///
/// Worth pinning: an Egypt order sees EGP and USD balances; money credited, paid, refunded and held
/// stays in its own currency; paying from another balance prices the application again from the
/// services' prices in that currency — never by converting; and a currency the country does not
/// offer cannot be held at all.
/// </summary>
public sealed class MultiCurrencyWalletTests : ApiTestBase
{
    public MultiCurrencyWalletTests(ApiFactory factory) : base(factory) { }

    private static string Wallet => Url("/orders/me/wallet");

    private async Task CreditAsync(Guid orderId, decimal amount, string? currencyId)
    {
        var admin = CreateClient(await AdminTokenAsync());
        await EnsureSuccessAsync(await admin.PostAsJsonAsync(
            Url($"/admin/orders/{orderId}/wallet/credit"),
            new { amount, note = "integration test", currencyId }));
    }

    private static decimal BalanceIn(JsonElement statement, string code) =>
        statement.GetProperty("balances").EnumerateArray()
            .Single(b => b.GetProperty("currencyCode").GetString() == code)
            .GetProperty("balance").GetDecimal();

    /// <summary>An application submitted and awaiting payment, priced in the order's main currency.</summary>
    private async Task<Guid> PendingPaymentAsync(Applicant applicant)
    {
        var draft = await CreateDraftAsync(applicant.Token);
        var id = Guid.Parse(draft.GetProperty("id").GetString()!);
        await SatisfyRequiredFilesAsync(applicant.Token, draft);
        await EnsureSuccessAsync(await CreateClient(applicant.Token).PostAsync(Url($"/applications/{id}/submit"), null));
        return id;
    }

    [Fact]
    public async Task AnEgyptOrderHoldsAnEgpAndAUsdBalance()
    {
        var applicant = await RegisterApplicantAsync();
        var statement = await ReadJsonAsync(await EnsureSuccessAsync(await CreateClient(applicant.Token).GetAsync(Wallet)));

        var balances = statement.GetProperty("balances").EnumerateArray().ToList();
        balances.Select(b => b.GetProperty("currencyCode").GetString()).Should().Equal("EGP", "USD");
        balances[0].GetProperty("isMain").GetBoolean().Should().BeTrue();
        // No money has moved in USD yet, so there is no wallet behind it — just a zero.
        balances[1].GetProperty("balance").GetDecimal().Should().Be(0m);
        balances[1].GetProperty("walletId").ValueKind.Should().Be(JsonValueKind.Null);

        statement.GetProperty("wallet").GetProperty("currencyCode").GetString().Should().Be("EGP");
    }

    [Fact]
    public async Task MoneyStaysInTheCurrencyItArrivedIn()
    {
        var applicant = await RegisterApplicantAsync();
        await CreditAsync(applicant.OrderId, 40m, SeedIds.Usd);
        await CreditAsync(applicant.OrderId, 500m, null);

        var client = CreateClient(applicant.Token);
        var statement = await ReadJsonAsync(await EnsureSuccessAsync(await client.GetAsync(Wallet)));
        BalanceIn(statement, "USD").Should().Be(40m);
        BalanceIn(statement, "EGP").Should().Be(500m);

        // Each currency has its own ledger.
        var usd = await ReadJsonAsync(await EnsureSuccessAsync(await client.GetAsync($"{Wallet}?currencyId={SeedIds.Usd}")));
        usd.GetProperty("wallet").GetProperty("currencyCode").GetString().Should().Be("USD");
        usd.GetProperty("ledger").GetProperty("items").EnumerateArray()
            .Select(t => t.GetProperty("amount").GetDecimal()).Should().Equal(40m);

        // A withdrawal holds money in the balance it is asked from, and nowhere else.
        await EnsureSuccessAsync(await client.PostAsJsonAsync(
            Url("/orders/me/wallet/requests"),
            new { type = 1, amount = 25m, note = "payout", currencyId = SeedIds.Usd }));

        var after = await ReadJsonAsync(await EnsureSuccessAsync(await client.GetAsync(Wallet)));
        BalanceIn(after, "USD").Should().Be(15m);
        BalanceIn(after, "EGP").Should().Be(500m);
        after.GetProperty("pendingRequests").EnumerateArray().Single()
            .GetProperty("currencyCode").GetString().Should().Be("USD");
    }

    [Fact]
    public async Task PayingFromAnotherBalancePricesTheApplicationInItsCurrency()
    {
        var applicant = await RegisterApplicantAsync();
        var applicationId = await PendingPaymentAsync(applicant);
        var client = CreateClient(applicant.Token);

        // The seeded service is 750 EGP or 15 USD: the quote offers both, from their own prices.
        var quote = await ReadJsonAsync(await EnsureSuccessAsync(
            await client.GetAsync(Url($"/payments/quote?applicationIds={applicationId}"))));
        var options = quote.GetProperty("options").EnumerateArray().ToList();
        var egp = options.Single(o => o.GetProperty("currencyCode").GetString() == "EGP");
        var usd = options.Single(o => o.GetProperty("currencyCode").GetString() == "USD");
        egp.GetProperty("isCurrent").GetBoolean().Should().BeTrue();
        usd.GetProperty("isCurrent").GetBoolean().Should().BeFalse();
        usd.GetProperty("canPay").GetBoolean().Should().BeFalse();
        var usdTotal = usd.GetProperty("total").GetDecimal();
        usdTotal.Should().BeGreaterThan(0m);

        await CreditAsync(applicant.OrderId, usdTotal + 10m, SeedIds.Usd);

        var paid = await ReadJsonAsync(await EnsureSuccessAsync(await client.PostAsJsonAsync(
            Url("/payments"),
            new { applicationIds = new[] { applicationId }, currencyId = SeedIds.Usd })));
        paid.GetProperty("currencyCode").GetString().Should().Be("USD");
        paid.GetProperty("amountPaid").GetDecimal().Should().Be(usdTotal);
        paid.GetProperty("balanceAfter").GetDecimal().Should().Be(10m);

        // The application now reads in USD, at the USD price.
        var details = await ReadJsonAsync(await EnsureSuccessAsync(await client.GetAsync(Url($"/applications/{applicationId}"))));
        details.GetProperty("currencyCode").GetString().Should().Be("USD");
        details.GetProperty("totalCost").GetDecimal().Should().Be(usdTotal);

        // And a refund goes back into the balance it was paid from.
        await EnsureSuccessAsync(await client.PostAsJsonAsync(Url($"/applications/{applicationId}/refund"), new { note = "changed my mind" }));
        var statement = await ReadJsonAsync(await EnsureSuccessAsync(await client.GetAsync(Wallet)));
        BalanceIn(statement, "USD").Should().Be(usdTotal + 10m);
        BalanceIn(statement, "EGP").Should().Be(0m);
    }

    [Fact]
    public async Task AShortBalanceIsRefusedWithoutRepricingAnything()
    {
        var applicant = await RegisterApplicantAsync();
        var applicationId = await PendingPaymentAsync(applicant);
        var client = CreateClient(applicant.Token);

        var response = await client.PostAsJsonAsync(
            Url("/payments"),
            new { applicationIds = new[] { applicationId }, currencyId = SeedIds.Usd });
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // The failed attempt left the application as it was: still in EGP, still unpaid.
        var details = await ReadJsonAsync(await EnsureSuccessAsync(await client.GetAsync(Url($"/applications/{applicationId}"))));
        details.GetProperty("currencyCode").GetString().Should().Be("EGP");
        details.GetProperty("isPaid").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ACurrencyTheCountryDoesNotOfferCannotBeHeld()
    {
        var applicant = await RegisterApplicantAsync();
        var admin = CreateClient(await AdminTokenAsync());

        var currencies = await ReadJsonAsync(await EnsureSuccessAsync(
            await admin.GetAsync(Url("/admin/lookups/currencies?search=EUR&pageSize=10"))));
        var eur = currencies.GetProperty("items").EnumerateArray()
            .First(c => c.GetProperty("code").GetString() == "EUR")
            .GetProperty("id").GetString();

        var response = await admin.PostAsJsonAsync(
            Url($"/admin/orders/{applicant.OrderId}/wallet/credit"),
            new { amount = 10m, note = "wrong currency", currencyId = eur });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemCodeAsync(response)).Should().Be("wallet.currency_not_available");
    }
}
