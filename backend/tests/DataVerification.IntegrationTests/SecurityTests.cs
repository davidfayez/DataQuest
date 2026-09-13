using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// The authorization posture is fail-closed: a fallback policy means any endpoint that forgets its
/// attribute still demands authentication. These tests exist so that guarantee cannot regress
/// unnoticed — a new controller without an attribute would break them rather than ship open.
/// </summary>
public sealed class SecurityTests : ApiTestBase
{
    public SecurityTests(ApiFactory factory) : base(factory)
    {
    }

    public static TheoryData<string> ProtectedEndpoints => new()
    {
        "/applications",
        "/orders/me/wallet",
        "/countries",
        "/transaction-types",
        "/admin/dashboard",
        "/admin/applications",
        "/admin/lookups/countries",
        "/admin/audit-log",
        "/admin/users",
    };

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task Anonymous_callers_are_refused(string path)
    {
        var response = await CreateClient().GetAsync(Url(path));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Health_is_reachable_without_a_token()
    {
        var response = await CreateClient().GetAsync(Url("/health"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Responses_carry_the_expected_security_headers()
    {
        var response = await CreateClient().GetAsync(Url("/health"));

        response.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().Contain("DENY");
        response.Headers.GetValues("Referrer-Policy").Should().Contain("no-referrer");
        response.Headers.Should().ContainSingle(h => h.Key == "Permissions-Policy");
        response.Headers.GetValues("Cross-Origin-Resource-Policy").Should().Contain("same-origin");
    }

    [Fact]
    public async Task An_applicant_token_is_not_accepted_on_admin_endpoints()
    {
        var applicant = await RegisterApplicantAsync();

        var response = await CreateClient(applicant.Token).GetAsync(Url("/admin/lookups/countries"));

        // 403 and not 401: the caller is authenticated, they simply hold the wrong realm's token.
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_admin_token_is_not_accepted_on_applicant_endpoints()
    {
        var adminToken = await AdminTokenAsync();

        var response = await CreateClient(adminToken).GetAsync(Url("/applications"));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_tampered_token_is_rejected()
    {
        var applicant = await RegisterApplicantAsync();
        var tampered = applicant.Token[..^4] + "AAAA";

        var response = await CreateClient(tampered).GetAsync(Url("/applications"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Sign_in_with_a_wrong_password_does_not_reveal_whether_the_order_exists()
    {
        var applicant = await RegisterApplicantAsync();
        var client = CreateClient();

        var wrongPassword = await client.PostAsJsonAsync(
            Url("/orders/login"),
            new { orderNumber = applicant.OrderNumber, password = "definitely-not-it" });

        // Well-formed but unissued: a client code followed by nine digits, long enough to clear
        // validation. A malformed number would be rejected before the lookup this test is about.
        var unknownOrder = await client.PostAsJsonAsync(
            Url("/orders/login"),
            new { orderNumber = "ZZZ999999999", password = "definitely-not-it" });

        wrongPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        unknownOrder.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var wrongPasswordCode = await ReadProblemCodeAsync(wrongPassword);
        var unknownOrderCode = await ReadProblemCodeAsync(unknownOrder);
        wrongPasswordCode.Should().Be(unknownOrderCode);
    }
}
