using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// The two histories behind the grid's log icon. The status log is the lifecycle; the change log is
/// everything anyone touched — and neither may leak another order's data or the back office's
/// private activity.
/// </summary>
public sealed class ApplicationLogTests : ApiTestBase
{
    public ApplicationLogTests(ApiFactory factory) : base(factory) { }

    [Fact]
    public async Task ANewDraftHasNoStatusChangesButIsAlreadyInTheChangeLog()
    {
        var applicant = await RegisterApplicantAsync();
        var client = CreateClient(applicant.Token);

        var draft = await CreateDraftAsync(applicant.Token);
        var id = draft.GetProperty("id").GetString();

        var statusLog = await ReadJsonAsync(await EnsureSuccessAsync(
            await client.GetAsync(Url($"/applications/{id}/status-log"))));
        statusLog.GetArrayLength().Should().Be(0);

        var changeLog = await ReadJsonAsync(await EnsureSuccessAsync(
            await client.GetAsync(Url($"/applications/{id}/change-log"))));

        changeLog.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("action").GetString())
            .Should().Contain("Application.Created");
    }

    [Fact]
    public async Task SubmittingAndPayingIsRecordedInBothLogs()
    {
        var applicant = await RegisterApplicantAsync();
        var client = CreateClient(applicant.Token);

        var id = await CreatePaidApplicationAsync(applicant);

        var statusLog = await ReadJsonAsync(await EnsureSuccessAsync(
            await client.GetAsync(Url($"/applications/{id}/status-log"))));

        var transitions = statusLog.EnumerateArray()
            .Select(e => $"{e.GetProperty("fromStatusName").GetString()}->{e.GetProperty("toStatusName").GetString()}")
            .ToList();

        // Draft -> PendingPayment on submit, then PendingPayment -> Pending on payment.
        transitions.Should().ContainInOrder("Draft->PendingPayment", "PendingPayment->Pending");

        statusLog.EnumerateArray().First()
            .GetProperty("changedByTypeName").GetString().Should().Be("Applicant");

        var changeLog = await ReadJsonAsync(await EnsureSuccessAsync(
            await client.GetAsync(Url($"/applications/{id}/change-log"))));

        var actions = changeLog.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("action").GetString()).ToList();

        actions.Should().Contain("Application.Created");
        actions.Should().Contain("Application.Submitted");
        actions.Should().Contain("Application.FileUploaded");
    }

    [Fact]
    public async Task ChangeLogEntriesCarryReadableDetailsButNeverTheStoredJsonOrAnIpAddress()
    {
        var applicant = await RegisterApplicantAsync();
        var client = CreateClient(applicant.Token);

        var draft = await CreateDraftAsync(applicant.Token);
        var id = draft.GetProperty("id").GetString();
        var number = draft.GetProperty("applicationNumber").GetString();

        var changeLog = await ReadJsonAsync(await EnsureSuccessAsync(
            await client.GetAsync(Url($"/applications/{id}/change-log"))));

        var created = changeLog.GetProperty("items").EnumerateArray()
            .Single(e => e.GetProperty("action").GetString() == "Application.Created");

        created.GetProperty("details").GetProperty("applicationNumber").GetString().Should().Be(number);
        created.GetProperty("actorTypeName").GetString().Should().Be("Applicant");

        // The forensic fields stay in the admin realm.
        created.TryGetProperty("ipAddress", out _).Should().BeFalse();
        created.TryGetProperty("data", out _).Should().BeFalse();
    }

    [Fact]
    public async Task AReviewersInternalNoteIsNotInTheApplicantsChangeLog()
    {
        var applicant = await RegisterApplicantAsync();
        var id = await CreatePaidApplicationAsync(applicant);
        var adminToken = await AdminTokenAsync();
        var admin = CreateClient(adminToken);

        // Move it into review, then leave a note only the back office may read.
        await EnsureSuccessAsync(await admin.PostAsJsonAsync(
            Url($"/admin/applications/{id}/status"),
            new { toStatus = 3, userComment = (string?)null, internalComment = "Chasing the registrar by phone" }));

        await EnsureSuccessAsync(await admin.PostAsJsonAsync(
            Url($"/admin/applications/{id}/comments"),
            new { body = "Registrar unreachable - internal only", visibility = 1 }));

        var mine = await ReadJsonAsync(await EnsureSuccessAsync(
            await CreateClient(applicant.Token).GetAsync(Url($"/applications/{id}/change-log"))));

        var raw = mine.GetRawText();

        // Neither the internal comment's existence nor a hint that a private note was attached.
        mine.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("action").GetString())
            .Should().NotContain("Application.CommentAdded");

        raw.Should().NotContain("hasInternalComment");
        raw.Should().NotContain("Registrar unreachable");
        raw.Should().NotContain("Chasing the registrar");

        // The status change itself is still visible — that is the applicant's business.
        mine.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("action").GetString())
            .Should().Contain("Application.StatusChanged");

        // The withheld entry is excluded from the count too, not just from the rendered page —
        // otherwise the pager would promise rows that never appear.
        mine.GetProperty("totalCount").GetInt32()
            .Should().Be(mine.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task AUserVisibleReviewCommentDoesAppear()
    {
        var applicant = await RegisterApplicantAsync();
        var id = await CreatePaidApplicationAsync(applicant);
        var admin = CreateClient(await AdminTokenAsync());

        await EnsureSuccessAsync(await admin.PostAsJsonAsync(
            Url($"/admin/applications/{id}/status"),
            new { toStatus = 3, userComment = (string?)null, internalComment = (string?)null }));

        await EnsureSuccessAsync(await admin.PostAsJsonAsync(
            Url($"/admin/applications/{id}/comments"),
            new { body = "Please send a clearer scan of page 2", visibility = 0 }));

        var mine = await ReadJsonAsync(await EnsureSuccessAsync(
            await CreateClient(applicant.Token).GetAsync(Url($"/applications/{id}/change-log"))));

        mine.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("action").GetString())
            .Should().Contain("Application.CommentAdded");
    }

    [Fact]
    public async Task NeitherLogReachesAnotherOrdersApplication()
    {
        var owner = await RegisterApplicantAsync();
        var stranger = await RegisterApplicantAsync();

        var draft = await CreateDraftAsync(owner.Token);
        var id = draft.GetProperty("id").GetString();

        var client = CreateClient(stranger.Token);

        (await client.GetAsync(Url($"/applications/{id}/status-log")))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await client.GetAsync(Url($"/applications/{id}/change-log")))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TheChangeLogPages()
    {
        var applicant = await RegisterApplicantAsync();
        var client = CreateClient(applicant.Token);

        var id = await CreatePaidApplicationAsync(applicant);

        var first = await ReadJsonAsync(await EnsureSuccessAsync(
            await client.GetAsync(Url($"/applications/{id}/change-log?page=1&pageSize=2"))));

        first.GetProperty("pageSize").GetInt32().Should().Be(2);
        first.GetProperty("totalCount").GetInt32().Should().BeGreaterThan(2);
        first.GetProperty("hasNext").GetBoolean().Should().BeTrue();
        first.GetProperty("items").GetArrayLength().Should().BeLessThanOrEqualTo(2);

        var second = await ReadJsonAsync(await EnsureSuccessAsync(
            await client.GetAsync(Url($"/applications/{id}/change-log?page=2&pageSize=2"))));

        second.GetProperty("hasPrevious").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task TheChangeLogIsNewestFirst()
    {
        var applicant = await RegisterApplicantAsync();
        var client = CreateClient(applicant.Token);

        var id = await CreatePaidApplicationAsync(applicant);

        var log = await ReadJsonAsync(await EnsureSuccessAsync(
            await client.GetAsync(Url($"/applications/{id}/change-log?pageSize=50"))));

        var timestamps = log.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("createdAtUtc").GetDateTime())
            .ToList();

        timestamps.Should().BeInDescendingOrder();
    }
}
