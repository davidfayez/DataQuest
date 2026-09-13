using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// The deposit and payout flows end to end. What is worth pinning here is the money: a withdrawal
/// holds funds the instant it is raised, and every terminal path either keeps the hold or gives it
/// back exactly once.
/// </summary>
public sealed class WalletRequestTests : ApiTestBase
{
    private const int Deposit = 0;
    private const int Withdrawal = 1;

    public WalletRequestTests(ApiFactory factory) : base(factory) { }

    /// <summary>
    /// A confirmed reference is unique for the lifetime of the database, and this suite runs
    /// against one that survives between runs — so every test mints its own.
    /// </summary>
    private static string Reference(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..16];

    [Fact]
    public async Task ApprovedDeposit_CreditsTheWallet()
    {
        var applicant = await RegisterApplicantAsync();
        var client = CreateClient(applicant.Token);

        var created = await ReadJsonAsync(await EnsureSuccessAsync(
            await client.PostAsJsonAsync(
                Url("/orders/me/wallet/requests"),
                new { type = Deposit, amount = 500m, note = "Bank transfer 8842" })));

        created.GetProperty("statusName").GetString().Should().Be("Pending");
        created.GetProperty("canCancel").GetBoolean().Should().BeTrue();

        // A deposit moves nothing until an operator confirms the money arrived.
        (await BalanceAsync(client)).Should().Be(0m);

        var requestId = created.GetProperty("id").GetString();
        var adminToken = await AdminTokenAsync();

        var adminClient = CreateClient(adminToken);

        // Approving without the figures it moves on is refused: the reviewer has to say what
        // actually arrived and which reference they matched.
        var incomplete = await adminClient.PostAsJsonAsync(
            Url($"/admin/wallet-requests/{requestId}/approve"),
            new { note = "Confirmed against statement" });

        incomplete.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await BalanceAsync(client)).Should().Be(0m, "a refused decision moves nothing");

        // Only 480 of the claimed 500 arrived; the wallet follows the reviewer, not the claim.
        var approved = await ReadJsonAsync(await EnsureSuccessAsync(
            await adminClient.PostAsJsonAsync(
                Url($"/admin/wallet-requests/{requestId}/approve"),
                new
                {
                    confirmedAmount = 480m,
                    confirmedReference = Reference("STMT"),
                    note = "Confirmed against statement",
                })));

        approved.GetProperty("statusName").GetString().Should().Be("Approved");
        approved.GetProperty("reviewerNote").GetString().Should().Be("Confirmed against statement");
        approved.GetProperty("amount").GetDecimal().Should().Be(500m, "the claim is still readable");
        approved.GetProperty("confirmedAmount").GetDecimal().Should().Be(480m);
        approved.GetProperty("confirmedReference").GetString().Should().StartWith("STMT-");

        (await BalanceAsync(client)).Should().Be(480m);
    }

    [Fact]
    public async Task AReferenceCannotBeCreditedTwice()
    {
        var adminClient = CreateClient(await AdminTokenAsync());

        async Task<string> ClaimAsync()
        {
            var applicant = await RegisterApplicantAsync();
            var created = await ReadJsonAsync(await EnsureSuccessAsync(
                await CreateClient(applicant.Token).PostAsJsonAsync(
                    Url("/orders/me/wallet/requests"),
                    new { type = Deposit, amount = 100m, note = "Same receipt" })));

            return created.GetProperty("id").GetString()!;
        }

        var first = await ClaimAsync();
        var second = await ClaimAsync();

        var shared = Reference("DUP");

        await EnsureSuccessAsync(await adminClient.PostAsJsonAsync(
            Url($"/admin/wallet-requests/{first}/approve"),
            new { confirmedAmount = 100m, confirmedReference = shared }));

        var again = await adminClient.PostAsJsonAsync(
            Url($"/admin/wallet-requests/{second}/approve"),
            new { confirmedAmount = 100m, confirmedReference = shared });

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadJsonAsync(again)).GetProperty("code").GetString()
            .Should().Be("wallet_request.reference_already_credited");
    }

    [Fact]
    public async Task ARejectedReferenceStaysFreeToUseAgain()
    {
        var adminClient = CreateClient(await AdminTokenAsync());

        async Task<(string Id, HttpClient Client)> ClaimAsync()
        {
            var applicant = await RegisterApplicantAsync();
            var client = CreateClient(applicant.Token);
            var created = await ReadJsonAsync(await EnsureSuccessAsync(
                await client.PostAsJsonAsync(
                    Url("/orders/me/wallet/requests"),
                    new { type = Deposit, amount = 100m, note = "Mistyped first time" })));

            return (created.GetProperty("id").GetString()!, client);
        }

        var refused = await ClaimAsync();
        var retry = await ClaimAsync();

        // Rejecting keeps no reference, so the applicant may quote the real one next time.
        var reused = Reference("FREE");

        await EnsureSuccessAsync(await adminClient.PostAsJsonAsync(
            Url($"/admin/wallet-requests/{refused.Id}/reject"),
            new { confirmedAmount = 100m, confirmedReference = reused, note = "Not found" }));

        await EnsureSuccessAsync(await adminClient.PostAsJsonAsync(
            Url($"/admin/wallet-requests/{retry.Id}/approve"),
            new { confirmedAmount = 100m, confirmedReference = reused }));

        (await BalanceAsync(retry.Client)).Should().Be(100m);
    }

    [Fact]
    public async Task TheHistoryShowsHowARequestWasRaisedAndDecided()
    {
        var applicant = await RegisterApplicantAsync();
        var created = await ReadJsonAsync(await EnsureSuccessAsync(
            await CreateClient(applicant.Token).PostAsJsonAsync(
                Url("/orders/me/wallet/requests"),
                new { type = Deposit, amount = 250m, note = "Bank transfer" })));

        var requestId = created.GetProperty("id").GetString();
        var adminClient = CreateClient(await AdminTokenAsync());

        await EnsureSuccessAsync(await adminClient.PostAsJsonAsync(
            Url($"/admin/wallet-requests/{requestId}/approve"),
            new { confirmedAmount = 250m, confirmedReference = Reference("HIST"), note = "Matched" }));

        var history = await ReadJsonAsync(await EnsureSuccessAsync(
            await adminClient.GetAsync(Url($"/admin/wallet-requests/{requestId}/history"))));

        var actions = history.EnumerateArray()
            .Select(entry => entry.GetProperty("action").GetString())
            .ToList();

        actions.Should().Contain("WalletRequest.Created");
        actions.Should().Contain("WalletRequest.Approved");

        // Oldest first, so the trail reads top to bottom.
        actions[0].Should().Be("WalletRequest.Created");

        var approval = history.EnumerateArray()
            .First(entry => entry.GetProperty("action").GetString() == "WalletRequest.Approved");

        approval.GetProperty("statusName").GetString().Should().Be("Approved");
        approval.GetProperty("actorName").GetString().Should().NotBeNullOrWhiteSpace();
        approval.GetProperty("details").EnumerateArray()
            .Select(detail => detail.GetProperty("key").GetString())
            .Should().Contain("ConfirmedReference");
    }

    [Fact]
    public async Task WithdrawalRequest_HoldsTheFundsImmediately()
    {
        var applicant = await RegisterApplicantAsync();
        var client = CreateClient(applicant.Token);
        await CreditAsync(applicant.OrderId, 1000m);

        await EnsureSuccessAsync(await client.PostAsJsonAsync(
            Url("/orders/me/wallet/requests"),
            new { type = Withdrawal, amount = 400m, note = "Pay to IBAN …4471" }));

        // The held amount has already left the spendable balance.
        (await BalanceAsync(client)).Should().Be(600m);

        var statement = await ReadJsonAsync(
            await client.GetAsync(Url("/orders/me/wallet?type=3")));

        var hold = statement.GetProperty("ledger").GetProperty("items").EnumerateArray().First();
        hold.GetProperty("typeName").GetString().Should().Be("Withdrawal");
        hold.GetProperty("signedAmount").GetDecimal().Should().Be(-400m);
    }

    [Fact]
    public async Task RejectedWithdrawal_ReturnsTheHeldFunds()
    {
        var applicant = await RegisterApplicantAsync();
        var client = CreateClient(applicant.Token);
        await CreditAsync(applicant.OrderId, 1000m);

        var created = await ReadJsonAsync(await EnsureSuccessAsync(
            await client.PostAsJsonAsync(
                Url("/orders/me/wallet/requests"),
                new { type = Withdrawal, amount = 400m, note = (string?)null })));

        var requestId = created.GetProperty("id").GetString();
        var adminToken = await AdminTokenAsync();

        await EnsureSuccessAsync(await CreateClient(adminToken).PostAsJsonAsync(
            Url($"/admin/wallet-requests/{requestId}/reject"),
            new { note = "Account details do not match" }));

        (await BalanceAsync(client)).Should().Be(1000m);

        // And the decision is final — a second one would release the money twice.
        var again = await CreateClient(adminToken).PostAsJsonAsync(
            Url($"/admin/wallet-requests/{requestId}/reject"),
            new { note = (string?)null });

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemCodeAsync(again)).Should().Be("wallet_request.not_pending");
        (await BalanceAsync(client)).Should().Be(1000m);
    }

    [Fact]
    public async Task CancelledWithdrawal_ReturnsTheHeldFunds()
    {
        var applicant = await RegisterApplicantAsync();
        var client = CreateClient(applicant.Token);
        await CreditAsync(applicant.OrderId, 300m);

        var created = await ReadJsonAsync(await EnsureSuccessAsync(
            await client.PostAsJsonAsync(
                Url("/orders/me/wallet/requests"),
                new { type = Withdrawal, amount = 300m, note = (string?)null })));

        (await BalanceAsync(client)).Should().Be(0m);

        var requestId = created.GetProperty("id").GetString();

        var cancelled = await ReadJsonAsync(await EnsureSuccessAsync(
            await client.PostAsync(Url($"/orders/me/wallet/requests/{requestId}/cancel"), null)));

        cancelled.GetProperty("statusName").GetString().Should().Be("Cancelled");
        cancelled.GetProperty("canCancel").GetBoolean().Should().BeFalse();
        (await BalanceAsync(client)).Should().Be(300m);
    }

    [Fact]
    public async Task Withdrawal_BeyondTheBalance_IsRejectedWithTheFigures()
    {
        var applicant = await RegisterApplicantAsync();
        var client = CreateClient(applicant.Token);
        await CreditAsync(applicant.OrderId, 100m);

        var response = await client.PostAsJsonAsync(
            Url("/orders/me/wallet/requests"),
            new { type = Withdrawal, amount = 250m, note = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadProblemCodeAsync(response)).Should().Be("wallet.insufficient_funds");
        (await BalanceAsync(client)).Should().Be(100m);
    }

    [Fact]
    public async Task ASecondPendingRequestOfTheSameKind_IsRefused()
    {
        var applicant = await RegisterApplicantAsync();
        var client = CreateClient(applicant.Token);

        await EnsureSuccessAsync(await client.PostAsJsonAsync(
            Url("/orders/me/wallet/requests"),
            new { type = Deposit, amount = 100m, note = (string?)null }));

        var second = await client.PostAsJsonAsync(
            Url("/orders/me/wallet/requests"),
            new { type = Deposit, amount = 200m, note = (string?)null });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemCodeAsync(second)).Should().Be("wallet_request.already_pending");
    }

    [Fact]
    public async Task AnApplicantCannotSeeOrCancelAnotherOrdersRequest()
    {
        var owner = await RegisterApplicantAsync();
        var stranger = await RegisterApplicantAsync();

        var created = await ReadJsonAsync(await EnsureSuccessAsync(
            await CreateClient(owner.Token).PostAsJsonAsync(
                Url("/orders/me/wallet/requests"),
                new { type = Deposit, amount = 100m, note = (string?)null })));

        var requestId = created.GetProperty("id").GetString();

        var cancel = await CreateClient(stranger.Token)
            .PostAsync(Url($"/orders/me/wallet/requests/{requestId}/cancel"), null);

        cancel.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // The details page reads a request by id, so it is a second way in and is scoped the same
        // way: not forbidden but absent, since a stranger has no business learning it exists.
        var theirDetails = await CreateClient(stranger.Token)
            .GetAsync(Url($"/orders/me/wallet/requests/{requestId}"));

        theirDetails.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var theirList = await ReadJsonAsync(await EnsureSuccessAsync(
            await CreateClient(stranger.Token).GetAsync(Url("/orders/me/wallet/requests"))));

        theirList.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    /// <summary>
    /// What the applicant's details page reads: one request in full, from its id alone, including
    /// the reviewer's figures once it has been decided.
    /// </summary>
    [Fact]
    public async Task TheDetailsOfOneRequest_CarryTheClaimAndTheDecision()
    {
        var applicant = await RegisterApplicantAsync();
        var client = CreateClient(applicant.Token);

        var created = await ReadJsonAsync(await EnsureSuccessAsync(
            await client.PostAsJsonAsync(
                Url("/orders/me/wallet/requests"),
                new { type = Deposit, amount = 125.50m, note = "Transferred this morning" })));

        var requestId = created.GetProperty("id").GetString();

        var pending = await ReadJsonAsync(await EnsureSuccessAsync(
            await client.GetAsync(Url($"/orders/me/wallet/requests/{requestId}"))));

        pending.GetProperty("id").GetString().Should().Be(requestId);
        pending.GetProperty("orderNumber").GetString().Should().Be(applicant.OrderNumber);
        pending.GetProperty("amount").GetDecimal().Should().Be(125.50m);
        pending.GetProperty("currencyCode").GetString().Should().Be("EGP");
        pending.GetProperty("statusName").GetString().Should().Be("Pending");
        pending.GetProperty("applicantNote").GetString().Should().Be("Transferred this morning");
        pending.GetProperty("canCancel").GetBoolean().Should().BeTrue();
        pending.GetProperty("files").GetArrayLength().Should().Be(0);

        var confirmed = Reference("STMT");

        await EnsureSuccessAsync(await CreateClient(await AdminTokenAsync()).PostAsJsonAsync(
            Url($"/admin/wallet-requests/{requestId}/approve"),
            new
            {
                confirmedAmount = 120m,
                confirmedReference = confirmed,
                note = "Only 120 arrived",
            }));

        // The page shows the claim and what the reviewer actually found side by side, so an
        // applicant credited a different figure can see why.
        var decided = await ReadJsonAsync(await EnsureSuccessAsync(
            await client.GetAsync(Url($"/orders/me/wallet/requests/{requestId}"))));

        decided.GetProperty("statusName").GetString().Should().Be("Approved");
        decided.GetProperty("amount").GetDecimal().Should().Be(125.50m);
        decided.GetProperty("confirmedAmount").GetDecimal().Should().Be(120m);
        decided.GetProperty("confirmedReference").GetString().Should().Be(confirmed);
        decided.GetProperty("reviewerNote").GetString().Should().Be("Only 120 arrived");
        decided.GetProperty("reviewedByName").GetString().Should().NotBeNullOrWhiteSpace();
        decided.GetProperty("reviewedAtUtc").GetDateTime().Should().BeAfter(default);
        decided.GetProperty("canCancel").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task TheAdminQueueListsPendingRequestsWithTheirOrderAndCurrency()
    {
        var applicant = await RegisterApplicantAsync();

        await EnsureSuccessAsync(await CreateClient(applicant.Token).PostAsJsonAsync(
            Url("/orders/me/wallet/requests"),
            new { type = Deposit, amount = 750m, note = "Wire 1123" }));

        var adminToken = await AdminTokenAsync();

        var queue = await ReadJsonAsync(await EnsureSuccessAsync(
            await CreateClient(adminToken).GetAsync(
                Url($"/admin/wallet-requests?status=0&search={applicant.OrderNumber}"))));

        var row = queue.GetProperty("items").EnumerateArray().Single();
        row.GetProperty("orderNumber").GetString().Should().Be(applicant.OrderNumber);
        row.GetProperty("currencyCode").GetString().Should().Be("EGP");
        row.GetProperty("amount").GetDecimal().Should().Be(750m);
        row.GetProperty("applicantNote").GetString().Should().Be("Wire 1123");
    }

    [Fact]
    public async Task PendingRequestsRideAlongWithTheStatement()
    {
        var applicant = await RegisterApplicantAsync();
        var client = CreateClient(applicant.Token);

        await EnsureSuccessAsync(await client.PostAsJsonAsync(
            Url("/orders/me/wallet/requests"),
            new { type = Deposit, amount = 60m, note = (string?)null }));

        var statement = await ReadJsonAsync(
            await EnsureSuccessAsync(await client.GetAsync(Url("/orders/me/wallet"))));

        statement.GetProperty("pendingRequests").GetArrayLength().Should().Be(1);
        statement.GetProperty("features")
            .GetProperty("depositRequestsEnabled").GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// The suite boots in Development, where <c>Wallet:AllowSimulatedDeposits</c> is on. Production
    /// leaves it off and the same call is refused — which is why the wallet page reads the flag off
    /// the statement rather than assuming the endpoint exists.
    /// </summary>
    [Fact]
    public async Task SimulatedDeposit_CreditsTheWalletWhereItIsEnabled()
    {
        var applicant = await RegisterApplicantAsync();
        var client = CreateClient(applicant.Token);

        await EnsureSuccessAsync(await client.PostAsJsonAsync(
            Url("/orders/me/wallet/simulate-deposit"),
            new { amount = 250m }));

        (await BalanceAsync(client)).Should().Be(250m);

        var statement = await ReadJsonAsync(
            await EnsureSuccessAsync(await client.GetAsync(Url("/orders/me/wallet"))));

        statement.GetProperty("features")
            .GetProperty("simulatedDepositsEnabled").GetBoolean().Should().BeTrue();

        var entry = statement.GetProperty("ledger").GetProperty("items").EnumerateArray().First();
        entry.GetProperty("note").GetString().Should().Contain("Simulated");
    }

    private async Task<decimal> BalanceAsync(HttpClient client)
    {
        var statement = await ReadJsonAsync(
            await EnsureSuccessAsync(await client.GetAsync(Url("/orders/me/wallet"))));

        return statement.GetProperty("wallet").GetProperty("balance").GetDecimal();
    }

    private async Task CreditAsync(Guid orderId, decimal amount)
    {
        var adminToken = await AdminTokenAsync();

        await EnsureSuccessAsync(await CreateClient(adminToken).PostAsJsonAsync(
            Url($"/admin/orders/{orderId}/wallet/credit"),
            new { amount, note = "integration test" }));
    }
}
