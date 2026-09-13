using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// Every applicant query is scoped to the order in the caller's token. Nothing in the URL space is
/// unguessable — application ids are returned to their owner — so scoping is the only thing keeping
/// one applicant out of another's file.
/// </summary>
public sealed class OrderIsolationTests : ApiTestBase
{
    public OrderIsolationTests(ApiFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task An_applicant_cannot_read_another_orders_application()
    {
        var owner = await RegisterApplicantAsync();
        var stranger = await RegisterApplicantAsync();

        var draft = await CreateDraftAsync(owner.Token);
        var applicationId = draft.GetProperty("id").GetString();

        var response = await CreateClient(stranger.Token)
            .GetAsync(Url($"/applications/{applicationId}"));

        // 404 rather than 403: a stranger should not learn that the id exists at all.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_applicant_cannot_edit_another_orders_application()
    {
        var owner = await RegisterApplicantAsync();
        var stranger = await RegisterApplicantAsync();

        var draft = await CreateDraftAsync(owner.Token);
        var applicationId = draft.GetProperty("id").GetString();

        var response = await CreateClient(stranger.Token).DeleteAsync(
            Url($"/applications/{applicationId}"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_applicant_cannot_pay_for_another_orders_application()
    {
        var owner = await RegisterApplicantAsync();
        var stranger = await RegisterApplicantAsync();

        var draft = await CreateDraftAsync(owner.Token);
        var applicationId = Guid.Parse(draft.GetProperty("id").GetString()!);

        var response = await CreateClient(stranger.Token).PostAsJsonAsync(
            Url("/payments"),
            new { applicationIds = new[] { applicationId } });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task An_applicants_list_contains_only_their_own_applications()
    {
        var owner = await RegisterApplicantAsync();
        var stranger = await RegisterApplicantAsync();

        var draft = await CreateDraftAsync(owner.Token);
        var ownerApplicationId = draft.GetProperty("id").GetString();

        var response = await CreateClient(stranger.Token).GetAsync(Url("/applications"));
        response.EnsureSuccessStatusCode();

        var body = await ReadJsonAsync(response);
        var ids = body.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString())
            .ToList();

        ids.Should().NotContain(ownerApplicationId);
    }
}
