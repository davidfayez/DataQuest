using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace DataVerification.IntegrationTests;

/// <summary>
/// What an applicant may see of their own support tickets.
///
/// Every test here is about a boundary. A ticket thread holds notes support write to each other,
/// answers drafted but not yet sent, and answers actually delivered — and only the last of those
/// belongs to the applicant. The filtering happens in the query, so these tests read the wire
/// format to prove nothing leaked into it.
/// </summary>
public sealed class MyTicketTests : ApiTestBase
{
    private const string InternalNote = "INTERNAL-do-not-show";
    private const string SentReply = "Here is the answer you asked for.";

    public MyTicketTests(ApiFactory factory) : base(factory)
    {
    }

    private async Task<string> FirstCategoryIdAsync()
    {
        var response = await CreateClient().GetAsync(Url("/tickets/categories"));
        await EnsureSuccessAsync(response);

        return (await ReadJsonAsync(response))[0].GetProperty("id").GetString()!;
    }

    private static MultipartFormDataContent Enquiry(string categoryId, string email, string subject)
    {
        return new MultipartFormDataContent
        {
            { new StringContent(categoryId), "TicketCategoryId" },
            { new StringContent("Nadia Hassan"), "Name" },
            { new StringContent(email), "Email" },
            { new StringContent(subject), "Subject" },
            { new StringContent("Something needs looking at."), "Description" },
        };
    }

    private async Task<(Applicant Applicant, string TicketId, string AdminToken)> RaiseAsync()
    {
        var applicant = await RegisterApplicantAsync();
        var categoryId = await FirstCategoryIdAsync();

        var created = await CreateClient(applicant.Token).PostAsync(
            Url("/tickets"),
            Enquiry(categoryId, $"{Guid.NewGuid():N}@example.com", "Signed-in enquiry"));
        await EnsureSuccessAsync(created);

        var mine = await CreateClient(applicant.Token).GetAsync(Url("/tickets/mine"));
        await EnsureSuccessAsync(mine);

        var ticketId = (await ReadJsonAsync(mine))[0].GetProperty("id").GetString()!;

        return (applicant, ticketId, await AdminTokenAsync());
    }

    private async Task<JsonElement> ActionAsync(
        string adminToken,
        string ticketId,
        bool isInternal,
        string body)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(isInternal ? string.Empty : body), "ReplyToSender" },
            { new StringContent(isInternal ? body : string.Empty), "InternalNote" },
        };

        var response = await CreateClient(adminToken).PostAsync(
            Url($"/admin/tickets/{ticketId}/actions"), form);
        await EnsureSuccessAsync(response);

        return await ReadJsonAsync(response);
    }

    [Fact]
    public async Task ATicketRaisedWhileSignedInAppearsInTheApplicantsList()
    {
        var (applicant, _, _) = await RaiseAsync();

        var response = await CreateClient(applicant.Token).GetAsync(Url("/tickets/mine"));

        await EnsureSuccessAsync(response);
        (await ReadJsonAsync(response)).GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task TheListIsClosedToAnyoneNotSignedIn()
    {
        var response = await CreateClient().GetAsync(Url("/tickets/mine"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnInternalNoteNeverReachesTheApplicant()
    {
        var (applicant, ticketId, admin) = await RaiseAsync();
        await ActionAsync(admin, ticketId, isInternal: true, InternalNote);

        var response = await CreateClient(applicant.Token).GetAsync(Url($"/tickets/mine/{ticketId}"));
        await EnsureSuccessAsync(response);

        var body = await response.Content.ReadAsStringAsync();

        // Read as raw text on purpose: the point is that the string is not on the wire at all,
        // not merely that some DTO property happens to hide it.
        body.Should().NotContain(InternalNote);
        (await ReadJsonAsync(response)).GetProperty("messages").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task AReplyIsVisibleAsSoonAsItIsWritten()
    {
        // Visibility and delivery are separate questions. A reply addressed to the sender is
        // theirs to read the moment it is saved; emailing a copy is a further, optional step.
        // Support who want to think out loud first use an internal note.
        var (applicant, ticketId, admin) = await RaiseAsync();
        await ActionAsync(admin, ticketId, isInternal: false, SentReply);

        var response = await CreateClient(applicant.Token).GetAsync(Url($"/tickets/mine/{ticketId}"));
        await EnsureSuccessAsync(response);

        var messages = (await ReadJsonAsync(response)).GetProperty("messages");
        messages.GetArrayLength().Should().Be(1);
        messages[0].GetProperty("body").GetString().Should().Be(SentReply);

        // Attributed to support rather than to the applicant...
        messages[0].GetProperty("fromSupport").GetBoolean().Should().BeTrue();

        // ...and honest about not having been emailed.
        messages[0].GetProperty("emailedAtUtc").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task WritingAReplyAnswersTheTicket()
    {
        var (applicant, ticketId, admin) = await RaiseAsync();
        await ActionAsync(admin, ticketId, isInternal: false, SentReply);

        var response = await CreateClient(applicant.Token).GetAsync(Url($"/tickets/mine/{ticketId}"));
        await EnsureSuccessAsync(response);

        (await ReadJsonAsync(response)).GetProperty("statusName").GetString().Should().Be("Answered");
    }

    [Fact]
    public async Task EmailingAReplyIsRecordedAgainstIt()
    {
        var (applicant, ticketId, admin) = await RaiseAsync();
        var after = await ActionAsync(admin, ticketId, isInternal: false, SentReply);
        var actionId = after.GetProperty("actions")[0].GetProperty("id").GetString();

        var notify = await CreateClient(admin).PostAsync(
            Url($"/admin/tickets/{ticketId}/actions/{actionId}/notify"), content: null);
        await EnsureSuccessAsync(notify);

        var response = await CreateClient(applicant.Token).GetAsync(Url($"/tickets/mine/{ticketId}"));
        await EnsureSuccessAsync(response);

        var messages = (await ReadJsonAsync(response)).GetProperty("messages");
        messages.GetArrayLength().Should().Be(1);
        messages[0].GetProperty("body").GetString().Should().Be(SentReply);

        // The applicant is told a copy reached their mailbox too.
        messages[0].GetProperty("emailedAtUtc").ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task TheApplicantsViewCarriesNoVisibilityOrAuthorFields()
    {
        // There is no flag to get wrong: the shape itself cannot describe an internal note.
        var (applicant, ticketId, admin) = await RaiseAsync();
        await ActionAsync(admin, ticketId, isInternal: true, InternalNote);

        var response = await CreateClient(applicant.Token).GetAsync(Url($"/tickets/mine/{ticketId}"));
        await EnsureSuccessAsync(response);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("isInternal");
        body.Should().NotContain("authorName");
        body.Should().NotContain("notifiedEmail");
    }

    [Fact]
    public async Task TheApplicantCanWriteBackUntilTheTicketIsClosed()
    {
        var (applicant, ticketId, admin) = await RaiseAsync();

        var reply = new MultipartFormDataContent
        {
            { new StringContent("One more detail I forgot."), "Body" },
        };

        var sent = await CreateClient(applicant.Token).PostAsync(
            Url($"/tickets/mine/{ticketId}/replies"), reply);
        await EnsureSuccessAsync(sent);

        var thread = await ReadJsonAsync(sent);
        var messages = thread.GetProperty("messages").EnumerateArray().ToList();

        messages.Should().ContainSingle();
        messages[0].GetProperty("fromSupport").GetBoolean().Should().BeFalse();
        thread.GetProperty("canReply").GetBoolean().Should().BeTrue();

        // Closing is the one status that ends the conversation.
        var close = await CreateClient(admin).PostAsJsonAsync(
            Url($"/admin/tickets/{ticketId}/status"), new { status = 3 });
        await EnsureSuccessAsync(close);

        var afterClose = await CreateClient(applicant.Token).GetAsync(Url($"/tickets/mine/{ticketId}"));
        await EnsureSuccessAsync(afterClose);
        (await ReadJsonAsync(afterClose)).GetProperty("canReply").GetBoolean().Should().BeFalse();

        var refused = await CreateClient(applicant.Token).PostAsync(
            Url($"/tickets/mine/{ticketId}/replies"),
            new MultipartFormDataContent { { new StringContent("Trying anyway."), "Body" } });

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemCodeAsync(refused)).Should().Be("ticket.closed");
    }

    [Fact]
    public async Task AStrangerCannotWriteOnSomebodyElsesTicket()
    {
        var (_, ticketId, _) = await RaiseAsync();
        var stranger = await RegisterApplicantAsync();

        var response = await CreateClient(stranger.Token).PostAsync(
            Url($"/tickets/mine/{ticketId}/replies"),
            new MultipartFormDataContent { { new StringContent("Not mine."), "Body" } });

        // 404, not 403: they have no business learning the ticket exists.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AnApplicantsOwnMessageIsNeverOfferedForEmailingBackToThem()
    {
        var (applicant, ticketId, admin) = await RaiseAsync();

        await EnsureSuccessAsync(await CreateClient(applicant.Token).PostAsync(
            Url($"/tickets/mine/{ticketId}/replies"),
            new MultipartFormDataContent { { new StringContent("A detail."), "Body" } }));

        var seenByAdmin = await CreateClient(admin).GetAsync(Url($"/admin/tickets/{ticketId}"));
        await EnsureSuccessAsync(seenByAdmin);

        var action = (await ReadJsonAsync(seenByAdmin)).GetProperty("actions")[0];

        action.GetProperty("isFromApplicant").GetBoolean().Should().BeTrue();
        // Mailing somebody their own words back is not a reply.
        action.GetProperty("canNotify").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task OneApplicantCannotSeeAnothersTicket()
    {
        var (_, ticketId, _) = await RaiseAsync();
        var stranger = await RegisterApplicantAsync();

        var list = await CreateClient(stranger.Token).GetAsync(Url("/tickets/mine"));
        await EnsureSuccessAsync(list);
        (await ReadJsonAsync(list)).GetArrayLength().Should().Be(0);

        var direct = await CreateClient(stranger.Token).GetAsync(Url($"/tickets/mine/{ticketId}"));

        // 404, not 403: a stranger has no business learning the ticket exists.
        direct.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// An address typed into the public form is not proof of anything, so it does not hand anybody
    /// a place in somebody's portal — not even their own.
    /// </summary>
    [Fact]
    public async Task ATicketRaisedWithoutSigningInStaysOutOfTheOrdersList()
    {
        var applicant = await RegisterApplicantAsync();

        // Raised anonymously, quoting the applicant's own address — which anyone could do.
        var anonymous = await CreateClient().PostAsync(
            Url("/tickets"),
            Enquiry(await FirstCategoryIdAsync(), applicant.Email, "Raised before signing in"));
        await EnsureSuccessAsync(anonymous);
        var ticketNumber = (await ReadJsonAsync(anonymous)).GetProperty("ticketNumber").GetString();

        var mine = await CreateClient(applicant.Token).GetAsync(Url("/tickets/mine"));
        await EnsureSuccessAsync(mine);
        (await ReadJsonAsync(mine)).GetArrayLength().Should().Be(0);

        // Support can still reunite it with the order, and that is what makes it appear: a
        // decision on the record rather than a guess from an unverified field.
        var admin = await AdminTokenAsync();
        var queue = await CreateClient(admin).GetAsync(
            Url($"/admin/tickets?page=1&pageSize=50&search={ticketNumber}"));
        await EnsureSuccessAsync(queue);
        var ticketId = (await ReadJsonAsync(queue)).GetProperty("items")[0].GetProperty("id").GetString();

        await EnsureSuccessAsync(await CreateClient(admin).PostAsJsonAsync(
            Url($"/admin/tickets/{ticketId}/link"),
            new { orderId = applicant.OrderId, applicationId = (Guid?)null }));

        var afterLinking = await CreateClient(applicant.Token).GetAsync(Url("/tickets/mine"));
        await EnsureSuccessAsync(afterLinking);
        (await ReadJsonAsync(afterLinking))[0].GetProperty("subject").GetString()
            .Should().Be("Raised before signing in");
    }

    /// <summary>
    /// The reply endpoint is scoped the same way as the read: an unattributed ticket quoting the
    /// applicant's address is not theirs to write on either.
    /// </summary>
    [Fact]
    public async Task AnUnattributedTicketCannotBeRepliedTo()
    {
        var applicant = await RegisterApplicantAsync();

        var anonymous = await CreateClient().PostAsync(
            Url("/tickets"),
            Enquiry(await FirstCategoryIdAsync(), applicant.Email, "Not theirs to answer"));
        await EnsureSuccessAsync(anonymous);
        var ticketNumber = (await ReadJsonAsync(anonymous)).GetProperty("ticketNumber").GetString();

        var admin = await AdminTokenAsync();
        var queue = await CreateClient(admin).GetAsync(
            Url($"/admin/tickets?page=1&pageSize=50&search={ticketNumber}"));
        await EnsureSuccessAsync(queue);
        var ticketId = (await ReadJsonAsync(queue)).GetProperty("items")[0].GetProperty("id").GetString();

        using var reply = new MultipartFormDataContent { { new StringContent("Let me add to this."), "Body" } };
        var attempt = await CreateClient(applicant.Token)
            .PostAsync(Url($"/tickets/mine/{ticketId}/replies"), reply);

        attempt.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AnAnonymousTicketAttributedToSomeoneElseStaysHidden()
    {
        // Once support decide whose case it is, a matching address does not override that.
        var applicant = await RegisterApplicantAsync();
        var stranger = await RegisterApplicantAsync();

        var anonymous = await CreateClient().PostAsync(
            Url("/tickets"),
            Enquiry(await FirstCategoryIdAsync(), applicant.Email, "Attributed elsewhere"));
        await EnsureSuccessAsync(anonymous);
        var ticketNumber = (await ReadJsonAsync(anonymous)).GetProperty("ticketNumber").GetString();

        var admin = await AdminTokenAsync();
        var queue = await CreateClient(admin).GetAsync(
            Url($"/admin/tickets?page=1&pageSize=50&search={ticketNumber}"));
        await EnsureSuccessAsync(queue);
        var ticketId = (await ReadJsonAsync(queue)).GetProperty("items")[0].GetProperty("id").GetString();

        var linked = await CreateClient(admin).PostAsJsonAsync(
            Url($"/admin/tickets/{ticketId}/link"),
            new { orderId = stranger.OrderId, applicationId = (Guid?)null });
        await EnsureSuccessAsync(linked);

        var mine = await CreateClient(applicant.Token).GetAsync(Url("/tickets/mine"));
        await EnsureSuccessAsync(mine);

        (await ReadJsonAsync(mine)).GetArrayLength().Should().Be(0);
    }
}
