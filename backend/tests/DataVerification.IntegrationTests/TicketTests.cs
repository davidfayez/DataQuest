using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace DataVerification.IntegrationTests;

/// <summary>
/// The contact form end to end: an anonymous stranger raises an enquiry, and support works it.
///
/// The tests that matter most here are the ones about what leaves the platform. A ticket thread
/// mixes notes support write to each other with answers meant for a member of the public, and one
/// flag separates them — so the rule that an internal note can never be emailed is pinned at the
/// HTTP boundary, not only in the domain.
/// </summary>
public sealed class TicketTests : ApiTestBase
{
    public TicketTests(ApiFactory factory) : base(factory)
    {
    }

    private static MultipartFormDataContent Enquiry(
        string categoryId,
        string subject = "A question about my certificate")
    {
        return new MultipartFormDataContent
        {
            { new StringContent(categoryId), "TicketCategoryId" },
            { new StringContent("Nadia Hassan"), "Name" },
            { new StringContent("nadia@example.com"), "Email" },
            { new StringContent("+20"), "PhoneCountryCode" },
            { new StringContent("1005551234"), "PhoneNumber" },
            { new StringContent(subject), "Subject" },
            { new StringContent("It has not appeared on my order."), "Description" },
        };
    }

    private async Task<string> FirstCategoryIdAsync()
    {
        var response = await CreateClient().GetAsync(Url("/tickets/categories"));
        await EnsureSuccessAsync(response);

        var categories = await ReadJsonAsync(response);
        return categories[0].GetProperty("id").GetString()!;
    }

    private async Task<(string Token, JsonElement Ticket)> RaiseAndOpenAsync()
    {
        var categoryId = await FirstCategoryIdAsync();

        var created = await CreateClient().PostAsync(Url("/tickets"), Enquiry(categoryId));
        await EnsureSuccessAsync(created);
        var ticketNumber = (await ReadJsonAsync(created)).GetProperty("ticketNumber").GetString();

        var token = await AdminTokenAsync();
        var queue = await CreateClient(token).GetAsync(
            Url($"/admin/tickets?page=1&pageSize=50&search={ticketNumber}"));
        await EnsureSuccessAsync(queue);

        var row = (await ReadJsonAsync(queue)).GetProperty("items")[0];
        var id = row.GetProperty("id").GetString();

        var details = await CreateClient(token).GetAsync(Url($"/admin/tickets/{id}"));
        await EnsureSuccessAsync(details);

        return (token, await ReadJsonAsync(details));
    }

    private async Task<JsonElement> PostActionAsync(
        string token,
        string ticketId,
        bool isInternal,
        string body)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(isInternal ? string.Empty : body), "ReplyToSender" },
            { new StringContent(isInternal ? body : string.Empty), "InternalNote" },
        };

        var response = await CreateClient(token).PostAsync(
            Url($"/admin/tickets/{ticketId}/actions"), form);
        await EnsureSuccessAsync(response);

        return await ReadJsonAsync(response);
    }

    [Fact]
    public async Task AnyoneCanReadTheCategoriesWithoutSigningIn()
    {
        // The person most likely to need support is the one who cannot get in.
        var response = await CreateClient().GetAsync(Url("/tickets/categories"));

        await EnsureSuccessAsync(response);
        (await ReadJsonAsync(response)).GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AStrangerCanRaiseATicketAndIsGivenAReference()
    {
        var categoryId = await FirstCategoryIdAsync();

        var response = await CreateClient().PostAsync(Url("/tickets"), Enquiry(categoryId));

        await EnsureSuccessAsync(response);
        var body = await ReadJsonAsync(response);

        body.GetProperty("ticketNumber").GetString().Should().MatchRegex(@"^TKT-\d{4}-[A-Z0-9]{6}$");

        // Only the reference and the address it went to. The form is open to anyone, so echoing
        // the stored record back would confirm what the platform holds about a typed-in address.
        body.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo("ticketNumber", "email");
    }

    [Fact]
    public async Task EveryTimestampLeavesTheApiLabelledAsUtc()
    {
        // The rule the browser depends on: ECMAScript reads a date-time string with no offset as
        // local time, so an unlabelled UTC instant is shown as the viewer's own clock reading.
        // Asserted on the raw body, because a DTO would parse the ambiguity away.
        var (token, ticket) = await RaiseAndOpenAsync();
        var ticketId = ticket.GetProperty("id").GetString();

        var response = await CreateClient(token).GetAsync(Url($"/admin/tickets/{ticketId}"));
        await EnsureSuccessAsync(response);

        var body = await response.Content.ReadAsStringAsync();

        var stamps = System.Text.RegularExpressions.Regex
            .Matches(body, "\"[A-Za-z]*At[A-Za-z]*\":\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .Where(value => value.Contains('T', StringComparison.Ordinal))
            .ToList();

        stamps.Should().NotBeEmpty("the response carries timestamps to check");
        stamps.Should().OnlyContain(
            value => value.EndsWith("Z", StringComparison.Ordinal),
            "an unlabelled timestamp is read as local time by every browser");
    }

    [Fact]
    public async Task ATicketOpensAsPendingAndUnassigned()
    {
        var (_, ticket) = await RaiseAndOpenAsync();

        ticket.GetProperty("statusName").GetString().Should().Be("Pending");
        ticket.GetProperty("assignedToAdminUserId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task ACategoryThatDoesNotExistIsRefused()
    {
        // The posted id is an assertion from an anonymous caller, not a fact.
        var response = await CreateClient().PostAsync(
            Url("/tickets"), Enquiry(Guid.NewGuid().ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TheQueueIsClosedToAnyoneWhoIsNotSignedIn()
    {
        var response = await CreateClient().GetAsync(Url("/admin/tickets"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnInternalNoteCanNeverBeEmailedToTheSender()
    {
        var (token, ticket) = await RaiseAndOpenAsync();
        var ticketId = ticket.GetProperty("id").GetString();

        var afterNote = await PostActionAsync(token, ticketId!, isInternal: true, "Checked storage.");
        var action = afterNote.GetProperty("actions")[0];

        action.GetProperty("isInternal").GetBoolean().Should().BeTrue();

        // The UI is told not to offer it...
        action.GetProperty("canNotify").GetBoolean().Should().BeFalse();

        // ...and the server refuses regardless of what any UI decides to show.
        var actionId = action.GetProperty("id").GetString();
        var notify = await CreateClient(token).PostAsync(
            Url($"/admin/tickets/{ticketId}/actions/{actionId}/notify"), content: null);

        notify.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemCodeAsync(notify)).Should().Be("ticket_action.internal");
    }

    [Fact]
    public async Task APublicReplyIsEmailedOnceAndOnlyOnce()
    {
        var (token, ticket) = await RaiseAndOpenAsync();
        var ticketId = ticket.GetProperty("id").GetString();

        var afterReply = await PostActionAsync(
            token, ticketId!, isInternal: false, "We have found it.");
        var actionId = afterReply.GetProperty("actions")[0].GetProperty("id").GetString();

        var first = await CreateClient(token).PostAsync(
            Url($"/admin/tickets/{ticketId}/actions/{actionId}/notify"), content: null);
        await EnsureSuccessAsync(first);

        var sent = (await ReadJsonAsync(first)).GetProperty("actions")[0];
        sent.GetProperty("notifiedAtUtc").ValueKind.Should().NotBe(JsonValueKind.Null);
        sent.GetProperty("notifiedEmail").GetString().Should().Be("nadia@example.com");
        sent.GetProperty("canNotify").GetBoolean().Should().BeFalse();

        // A second press of the button would put the same answer in their inbox twice.
        var second = await CreateClient(token).PostAsync(
            Url($"/admin/tickets/{ticketId}/actions/{actionId}/notify"), content: null);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemCodeAsync(second)).Should().Be("ticket_action.already_notified");
    }

    /// <summary>
    /// A reply the transport refused is not a reply that was emailed.
    /// </summary>
    /// <remarks>
    /// The sender never throws, so nothing stops the handler from recording the send anyway — and
    /// recording it would tell three different people something untrue: the sender sees "also
    /// emailed to you" on a message their mailbox never received, the queue's not-emailed count
    /// drops the one reply that still needs sending, and the audit trail says it went.
    ///
    /// Delivery is failed here by enabling SendGrid with no API key, which the sender answers
    /// without touching the network.
    /// </remarks>
    [Fact]
    public async Task AReplyThatCouldNotBeEmailedIsNotRecordedAsSent()
    {
        var (token, ticket) = await RaiseAndOpenAsync();
        var ticketId = ticket.GetProperty("id").GetString();

        var afterReply = await PostActionAsync(
            token, ticketId!, isInternal: false, "This one cannot go out.");
        var actionId = afterReply.GetProperty("actions")[0].GetProperty("id").GetString();

        Environment.SetEnvironmentVariable("SendGrid__Enabled", "true");
        try
        {
            using var undeliverable = Factory.WithWebHostBuilder(_ => { });

            var client = undeliverable.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            var notify = await client.PostAsync(
                Url($"/admin/tickets/{ticketId}/actions/{actionId}/notify"), content: null);

            notify.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await ReadProblemCodeAsync(notify)).Should().Be("ticket_action.not_emailed");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SendGrid__Enabled", "false");
        }

        // Still unsent, and still sendable: the refusal left the reply exactly as it was.
        var reread = await ReadJsonAsync(await EnsureSuccessAsync(
            await CreateClient(token).GetAsync(Url($"/admin/tickets/{ticketId}"))));

        var action = reread.GetProperty("actions").EnumerateArray()
            .Single(candidate => candidate.GetProperty("id").GetString() == actionId);

        action.GetProperty("notifiedAtUtc").ValueKind.Should().Be(JsonValueKind.Null);
        action.GetProperty("canNotify").GetBoolean().Should().BeTrue();

        // And once delivery works again, the same button sends it.
        var retry = await CreateClient(token).PostAsync(
            Url($"/admin/tickets/{ticketId}/actions/{actionId}/notify"), content: null);
        await EnsureSuccessAsync(retry);

        (await ReadJsonAsync(retry)).GetProperty("actions")[0]
            .GetProperty("notifiedAtUtc").ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task AnsweringMovesTheTicketOn()
    {
        var (token, ticket) = await RaiseAndOpenAsync();
        var ticketId = ticket.GetProperty("id").GetString();

        var afterReply = await PostActionAsync(token, ticketId!, isInternal: false, "Here you go.");
        var actionId = afterReply.GetProperty("actions")[0].GetProperty("id").GetString();

        var notify = await CreateClient(token).PostAsync(
            Url($"/admin/tickets/{ticketId}/actions/{actionId}/notify"), content: null);
        await EnsureSuccessAsync(notify);

        (await ReadJsonAsync(notify)).GetProperty("statusName").GetString().Should().Be("Answered");
    }

    [Fact]
    public async Task WritingAnInternalNoteDoesNotMoveTheTicketOn()
    {
        // Only answering the sender does. A note to the team is not an answer.
        var (token, ticket) = await RaiseAndOpenAsync();
        var ticketId = ticket.GetProperty("id").GetString();

        var after = await PostActionAsync(token, ticketId!, isInternal: true, "Looking into it.");

        after.GetProperty("statusName").GetString().Should().Be("Pending");
    }

    [Fact]
    public async Task AnActionWithNothingInItIsRefused()
    {
        var (token, ticket) = await RaiseAndOpenAsync();
        var ticketId = ticket.GetProperty("id").GetString();

        var form = new MultipartFormDataContent
        {
            { new StringContent("   "), "ReplyToSender" },
            { new StringContent("  "), "InternalNote" },
        };

        var response = await CreateClient(token).PostAsync(
            Url($"/admin/tickets/{ticketId}/actions"), form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AReplyAndAnInternalNoteCanBeRecordedTogether()
    {
        // One piece of work — "here is what I told them, and here is what the team needs to know"
        // — so it is one submission, and it lands as two actions with two different audiences.
        var (token, ticket) = await RaiseAndOpenAsync();
        var ticketId = ticket.GetProperty("id").GetString();

        var form = new MultipartFormDataContent
        {
            { new StringContent("We have found your certificate."), "ReplyToSender" },
            { new StringContent("Checked storage; it was mis-filed."), "InternalNote" },
        };

        var response = await CreateClient(token).PostAsync(
            Url($"/admin/tickets/{ticketId}/actions"), form);
        await EnsureSuccessAsync(response);

        var body = await ReadJsonAsync(response);
        var actions = body.GetProperty("actions").EnumerateArray().ToList();

        actions.Should().HaveCount(2);
        actions.Should().ContainSingle(action => action.GetProperty("isInternal").GetBoolean());
        actions.Should().ContainSingle(action => !action.GetProperty("isInternal").GetBoolean());

        // The reply is the half that may be emailed; the note never can be.
        var note = actions.Single(action => action.GetProperty("isInternal").GetBoolean());
        note.GetProperty("canNotify").GetBoolean().Should().BeFalse();

        var reply = actions.Single(action => !action.GetProperty("isInternal").GetBoolean());
        reply.GetProperty("canNotify").GetBoolean().Should().BeTrue();

        // Answering the sender is what moves the ticket on.
        body.GetProperty("statusName").GetString().Should().Be("Answered");
    }

    [Fact]
    public async Task ANoteOnItsOwnDoesNotAnswerTheTicket()
    {
        var (token, ticket) = await RaiseAndOpenAsync();
        var ticketId = ticket.GetProperty("id").GetString();

        var form = new MultipartFormDataContent
        {
            { new StringContent("   "), "ReplyToSender" },
            { new StringContent("Still looking into this."), "InternalNote" },
        };

        var response = await CreateClient(token).PostAsync(
            Url($"/admin/tickets/{ticketId}/actions"), form);
        await EnsureSuccessAsync(response);

        var body = await ReadJsonAsync(response);
        body.GetProperty("actions").GetArrayLength().Should().Be(1);
        body.GetProperty("statusName").GetString().Should().Be("Pending");
    }

    [Fact]
    public async Task AnApplicationFromAnotherOrderCannotBeLinked()
    {
        var (token, ticket) = await RaiseAndOpenAsync();
        var ticketId = ticket.GetProperty("id").GetString();

        // Two applicants, so the application below genuinely belongs to the other one's order.
        var mine = await RegisterApplicantAsync();
        var theirs = await RegisterApplicantAsync();
        var application = await CreateDraftAsync(theirs.Token);
        var applicationId = application.GetProperty("id").GetString();

        var response = await CreateClient(token).PostAsJsonAsync(
            Url($"/admin/tickets/{ticketId}/link"),
            new { orderId = mine.OrderId, applicationId });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemCodeAsync(response)).Should().Be("ticket.application_not_in_order");
    }

    [Fact]
    public async Task ClearingTheOrderClearsItsApplicationToo()
    {
        var (token, ticket) = await RaiseAndOpenAsync();
        var ticketId = ticket.GetProperty("id").GetString();

        var applicant = await RegisterApplicantAsync();
        var application = await CreateDraftAsync(applicant.Token);

        var linked = await CreateClient(token).PostAsJsonAsync(
            Url($"/admin/tickets/{ticketId}/link"),
            new { orderId = applicant.OrderId, applicationId = application.GetProperty("id").GetString() });
        await EnsureSuccessAsync(linked);

        var cleared = await CreateClient(token).PostAsJsonAsync(
            Url($"/admin/tickets/{ticketId}/link"),
            new { orderId = (Guid?)null, applicationId = (Guid?)null });
        await EnsureSuccessAsync(cleared);

        var body = await ReadJsonAsync(cleared);
        body.GetProperty("orderId").ValueKind.Should().Be(JsonValueKind.Null);
        // An application without the order it belongs to is a link support cannot navigate.
        body.GetProperty("applicationId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task PickingUpATicketPutsItInProgress()
    {
        var (token, ticket) = await RaiseAndOpenAsync();
        var ticketId = ticket.GetProperty("id").GetString();

        var assignees = await CreateClient(token).GetAsync(Url("/admin/tickets/assignees"));
        await EnsureSuccessAsync(assignees);
        var assigneeId = (await ReadJsonAsync(assignees))[0].GetProperty("id").GetString();

        var response = await CreateClient(token).PostAsJsonAsync(
            Url($"/admin/tickets/{ticketId}/assign"),
            new { adminUserId = assigneeId });
        await EnsureSuccessAsync(response);

        var body = await ReadJsonAsync(response);
        body.GetProperty("assignedToAdminUserId").GetString().Should().Be(assigneeId);
        body.GetProperty("statusName").GetString().Should().Be("InProgress");
    }
}
