using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// An administrator driving an application through steps that normally belong to the applicant.
/// The rules that matter: an incomplete draft is still refused, marking something paid stamps it
/// as paid, and Refunded can never be reached this way — that would keep the applicant's money.
/// </summary>
public sealed class AdminStatusOverrideTests : ApiTestBase
{
    private const int Draft = 0;
    private const int PendingPayment = 1;
    private const int Pending = 2;
    private const int InProgress = 3;
    private const int Refunded = 7;

    public AdminStatusOverrideTests(ApiFactory factory) : base(factory) { }

    [Fact]
    public async Task AnAdminCanSubmitACompleteDraftForTheApplicant()
    {
        var applicant = await RegisterApplicantAsync();
        var draft = await CreateDraftAsync(applicant.Token);
        var id = draft.GetProperty("id").GetString();

        await SatisfyRequiredFilesAsync(applicant.Token, draft);

        var admin = CreateClient(await AdminTokenAsync());
        var response = await admin.PostAsJsonAsync(
            Url($"/admin/applications/{id}/status"),
            new { toStatus = PendingPayment, userComment = (string?)null, internalComment = (string?)null });

        await EnsureSuccessAsync(response);
        (await StatusOfAsync(applicant.Token, id!)).Should().Be("PendingPayment");
    }

    [Fact]
    public async Task AnIncompleteDraftIsStillRefused()
    {
        var applicant = await RegisterApplicantAsync();

        // Created but with no documents uploaded. The applicant's own submit refuses this, and
        // an administrator acting for them must be refused on exactly the same grounds.
        var draft = await CreateDraftAsync(applicant.Token);
        var id = draft.GetProperty("id").GetString();

        var admin = CreateClient(await AdminTokenAsync());
        var response = await admin.PostAsJsonAsync(
            Url($"/admin/applications/{id}/status"),
            new { toStatus = PendingPayment, userComment = (string?)null, internalComment = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemCodeAsync(response)).Should().Be("application.mandatory_files_missing");
        (await StatusOfAsync(applicant.Token, id!)).Should().Be("Draft");
    }

    [Fact]
    public async Task MarkingAnApplicationPaidStampsItAsPaidWithoutChargingTheWallet()
    {
        var applicant = await RegisterApplicantAsync();
        var draft = await CreateDraftAsync(applicant.Token);
        var id = draft.GetProperty("id").GetString();

        await SatisfyRequiredFilesAsync(applicant.Token, draft);
        await EnsureSuccessAsync(await CreateClient(applicant.Token)
            .PostAsync(Url($"/applications/{id}/submit"), null));

        var balanceBefore = await BalanceAsync(applicant.Token);

        var admin = CreateClient(await AdminTokenAsync());
        await EnsureSuccessAsync(await admin.PostAsJsonAsync(
            Url($"/admin/applications/{id}/status"),
            new { toStatus = Pending, userComment = (string?)null, internalComment = (string?)null }));

        var row = await RowAsync(applicant.Token, id!);
        row.GetProperty("statusName").GetString().Should().Be("Pending");

        // The point of the override: the queue entry exists, and no money moved.
        row.GetProperty("isPaid").GetBoolean().Should().BeTrue();
        row.GetProperty("paidAtUtc").GetString().Should().NotBeNull();
        (await BalanceAsync(applicant.Token)).Should().Be(balanceBefore);
    }

    [Fact]
    public async Task RefundedCanNeverBeSetThroughTheStatusEndpoint()
    {
        var applicant = await RegisterApplicantAsync();
        var id = await CreatePaidApplicationAsync(applicant);

        var admin = CreateClient(await AdminTokenAsync());
        var response = await admin.PostAsJsonAsync(
            Url($"/admin/applications/{id}/status"),
            new { toStatus = Refunded, userComment = (string?)null, internalComment = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await StatusOfAsync(applicant.Token, id.ToString())).Should().Be("Pending");
    }

    [Fact]
    public async Task TheRefundActionReturnsTheMoneyAndMovesTheApplication()
    {
        var applicant = await RegisterApplicantAsync();
        var id = await CreatePaidApplicationAsync(applicant);

        var before = await BalanceAsync(applicant.Token);
        var admin = CreateClient(await AdminTokenAsync());

        await EnsureSuccessAsync(await admin.PostAsJsonAsync(
            Url($"/admin/orders/{applicant.OrderId}/applications/{id}/refund"),
            new { note = (string?)null }));

        (await StatusOfAsync(applicant.Token, id.ToString())).Should().Be("Refunded");
        (await BalanceAsync(applicant.Token)).Should().BeGreaterThan(before);
    }

    [Fact]
    public async Task TheOrdinaryReviewFlowIsUnchanged()
    {
        var applicant = await RegisterApplicantAsync();
        var id = await CreatePaidApplicationAsync(applicant);

        var admin = CreateClient(await AdminTokenAsync());
        await EnsureSuccessAsync(await admin.PostAsJsonAsync(
            Url($"/admin/applications/{id}/status"),
            new { toStatus = InProgress, userComment = "We have started.", internalComment = "Called registrar." }));

        (await StatusOfAsync(applicant.Token, id.ToString())).Should().Be("InProgress");
    }

    [Fact]
    public async Task AnOverrideIsMarkedAsSuchInTheChangeLog()
    {
        var applicant = await RegisterApplicantAsync();
        var draft = await CreateDraftAsync(applicant.Token);
        var id = draft.GetProperty("id").GetString();

        await SatisfyRequiredFilesAsync(applicant.Token, draft);

        var admin = CreateClient(await AdminTokenAsync());
        await EnsureSuccessAsync(await admin.PostAsJsonAsync(
            Url($"/admin/applications/{id}/status"),
            new { toStatus = PendingPayment, userComment = (string?)null, internalComment = (string?)null }));

        var log = await ReadJsonAsync(await EnsureSuccessAsync(
            await CreateClient(applicant.Token).GetAsync(Url($"/applications/{id}/change-log"))));

        var entry = log.GetProperty("items").EnumerateArray()
            .First(e => e.GetProperty("action").GetString() == "Application.StatusChanged");

        entry.GetProperty("details").GetProperty("adminOverride").GetString().Should().Be("true");
    }

    [Fact]
    public async Task AnAdminCanSendAnUnpaidApplicationBackToDraft()
    {
        var applicant = await RegisterApplicantAsync();
        var draft = await CreateDraftAsync(applicant.Token);
        var id = draft.GetProperty("id").GetString();

        await SatisfyRequiredFilesAsync(applicant.Token, draft);
        await EnsureSuccessAsync(await CreateClient(applicant.Token)
            .PostAsync(Url($"/applications/{id}/submit"), null));

        var admin = CreateClient(await AdminTokenAsync());
        await EnsureSuccessAsync(await admin.PostAsJsonAsync(
            Url($"/admin/applications/{id}/status"),
            new { toStatus = Draft, userComment = "Please correct your date of birth.", internalComment = (string?)null }));

        (await StatusOfAsync(applicant.Token, id!)).Should().Be("Draft");
    }

    // --- helpers -------------------------------------------------------------

    private async Task<System.Text.Json.JsonElement> RowAsync(string token, string applicationId)
    {
        var page = await ReadJsonAsync(await EnsureSuccessAsync(
            await CreateClient(token).GetAsync(Url("/applications?pageSize=100"))));

        return page.GetProperty("items").EnumerateArray()
            .First(item => item.GetProperty("id").GetString() == applicationId);
    }

    private async Task<string?> StatusOfAsync(string token, string applicationId) =>
        (await RowAsync(token, applicationId)).GetProperty("statusName").GetString();

    private async Task<decimal> BalanceAsync(string token)
    {
        var statement = await ReadJsonAsync(await EnsureSuccessAsync(
            await CreateClient(token).GetAsync(Url("/orders/me/wallet"))));

        return statement.GetProperty("wallet").GetProperty("balance").GetDecimal();
    }
}
