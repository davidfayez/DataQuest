using System.Net.Http.Json;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// Internal comments are filtered in the query layer, never in the UI. That distinction is the
/// whole point: a reviewer's private note must not reach the applicant's browser at all, so hiding
/// it with a CSS class or a client-side filter would be a leak even though the screen looked right.
///
/// These tests read the raw applicant payload and assert the text is simply absent.
/// </summary>
public sealed class CommentVisibilityTests : ApiTestBase
{
    private const string InternalNote = "INTERNAL-ONLY-do-not-disclose-to-the-applicant";
    private const string ApplicantNote = "Please upload a clearer copy of your certificate.";

    public CommentVisibilityTests(ApiFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task An_internal_comment_never_reaches_the_applicant()
    {
        var applicant = await RegisterApplicantAsync();
        var applicationId = await CreatePaidApplicationAsync(applicant);
        var admin = CreateClient(await AdminTokenAsync());

        // Visibility 0 = ForUser, 1 = Internal.
        (await admin.PostAsJsonAsync(
                Url($"/admin/applications/{applicationId}/comments"),
                new { body = ApplicantNote, visibility = 0 }))
            .EnsureSuccessStatusCode();

        (await admin.PostAsJsonAsync(
                Url($"/admin/applications/{applicationId}/comments"),
                new { body = InternalNote, visibility = 1 }))
            .EnsureSuccessStatusCode();

        var applicantClient = CreateClient(applicant.Token);

        var timeline = await applicantClient.GetStringAsync(
            Url($"/applications/{applicationId}/timeline"));
        var details = await applicantClient.GetStringAsync(
            Url($"/applications/{applicationId}"));
        var list = await applicantClient.GetStringAsync(Url("/applications"));

        timeline.Should().Contain(ApplicantNote, "a comment addressed to the applicant is theirs to read");
        timeline.Should().NotContain(InternalNote);
        details.Should().NotContain(InternalNote);
        list.Should().NotContain(InternalNote);
    }

    [Fact]
    public async Task A_status_change_carries_two_comments_and_never_leaks_the_internal_one()
    {
        var applicant = await RegisterApplicantAsync();
        var applicationId = await CreatePaidApplicationAsync(applicant);
        var admin = CreateClient(await AdminTokenAsync());

        // Pending -> InProgress (toStatus 3), carrying a comment for the applicant and an internal note.
        (await admin.PostAsJsonAsync(
                Url($"/admin/applications/{applicationId}/status"),
                new { toStatus = 3, userComment = ApplicantNote, internalComment = InternalNote }))
            .EnsureSuccessStatusCode();

        var applicantClient = CreateClient(applicant.Token);
        var applicantTimeline = await applicantClient.GetStringAsync(
            Url($"/applications/{applicationId}/timeline"));
        var applicantDetails = await applicantClient.GetStringAsync(
            Url($"/applications/{applicationId}"));

        // The applicant is told what the reviewer wanted them to see — the status note and the
        // user-facing comment — but the internal note is absent from every applicant-facing payload.
        applicantTimeline.Should().Contain(ApplicantNote);
        applicantTimeline.Should().NotContain(InternalNote);
        applicantDetails.Should().NotContain(InternalNote);

        // Both comments are on the thread the admin sees.
        var adminTimeline = await admin.GetStringAsync(
            Url($"/admin/applications/{applicationId}/timeline"));
        adminTimeline.Should().Contain(ApplicantNote);
        adminTimeline.Should().Contain(InternalNote);
    }

    [Fact]
    public async Task An_admin_sees_both_visibilities()
    {
        var applicant = await RegisterApplicantAsync();
        var applicationId = await CreatePaidApplicationAsync(applicant);
        var admin = CreateClient(await AdminTokenAsync());

        (await admin.PostAsJsonAsync(
                Url($"/admin/applications/{applicationId}/comments"),
                new { body = InternalNote, visibility = 1 }))
            .EnsureSuccessStatusCode();

        var timeline = await admin.GetStringAsync(Url($"/admin/applications/{applicationId}/timeline"));

        timeline.Should().Contain(InternalNote);
    }

    [Fact]
    public async Task An_applicant_comment_is_visible_to_both_sides()
    {
        var applicant = await RegisterApplicantAsync();
        var applicationId = await CreatePaidApplicationAsync(applicant);

        const string Message = "Here is the additional document you asked for.";

        (await CreateClient(applicant.Token).PostAsJsonAsync(
                Url($"/applications/{applicationId}/comments"),
                new { body = Message }))
            .EnsureSuccessStatusCode();

        var admin = CreateClient(await AdminTokenAsync());
        var adminTimeline = await admin.GetStringAsync(
            Url($"/admin/applications/{applicationId}/timeline"));
        var applicantTimeline = await CreateClient(applicant.Token).GetStringAsync(
            Url($"/applications/{applicationId}/timeline"));

        adminTimeline.Should().Contain(Message);
        applicantTimeline.Should().Contain(Message);
    }
}
