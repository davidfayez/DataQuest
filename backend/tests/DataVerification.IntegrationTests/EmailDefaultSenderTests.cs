using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// The platform-wide default sender an administrator sets on the Emails configurations page.
///
/// What matters is the order of precedence, because it is what decides the address on every email
/// the platform sends: a kind's own override beats the stored default, which beats the address the
/// server was configured with. And clearing the default must restore the configured one rather
/// than leave the platform with no sender at all.
/// </summary>
public sealed class EmailDefaultSenderTests : ApiTestBase
{
    private const int OrderCreated = 0;
    private const int ForgotPassword = 1;

    public EmailDefaultSenderTests(ApiFactory factory) : base(factory) { }

    private static string Endpoint => Url("/admin/settings/email-default-sender");

    private static async Task ClearDefaultAsync(HttpClient admin) =>
        await admin.PutAsJsonAsync(Endpoint, new { fromAddress = (string?)null, fromName = (string?)null });

    [Fact]
    public async Task WithNothingStoredTheConfiguredAddressIsReported()
    {
        var admin = CreateClient(await AdminTokenAsync());
        await ClearDefaultAsync(admin);

        var body = await admin.GetFromJsonAsync<JsonElement>(Endpoint);

        body.GetProperty("source").GetString().Should().Be("Configuration");
        body.GetProperty("fromAddress").GetString()
            .Should().Be(body.GetProperty("configuredFromAddress").GetString());
    }

    [Fact]
    public async Task AStoredDefaultOverridesTheConfiguredAddress()
    {
        var admin = CreateClient(await AdminTokenAsync());

        try
        {
            var saved = await admin.PutAsJsonAsync(Endpoint, new
            {
                fromAddress = "stored-default@example.com",
                fromName = "Stored Default",
            });

            await EnsureSuccessAsync(saved);

            var body = await admin.GetFromJsonAsync<JsonElement>(Endpoint);
            body.GetProperty("source").GetString().Should().Be("Database");
            body.GetProperty("fromAddress").GetString().Should().Be("stored-default@example.com");

            // The delivery banner reads the same address, so the page cannot contradict itself.
            var status = await admin.GetFromJsonAsync<JsonElement>(Url("/admin/settings/email-delivery"));
            status.GetProperty("defaultFromAddress").GetString().Should().Be("stored-default@example.com");
        }
        finally
        {
            await ClearDefaultAsync(admin);
        }
    }

    [Fact]
    public async Task AnEmailWithNoPerKindOverrideIsSentFromTheStoredDefault()
    {
        var admin = CreateClient(await AdminTokenAsync());

        try
        {
            // No override on this kind, so the stored default is what should apply.
            await EnsureSuccessAsync(await admin.PutAsJsonAsync(Url("/admin/settings/email-routing"), new
            {
                type = ForgotPassword,
                fromAddress = (string?)null,
                fromName = (string?)null,
                bcc = Array.Empty<object>(),
            }));

            await EnsureSuccessAsync(await admin.PutAsJsonAsync(Endpoint, new
            {
                fromAddress = "fallback@example.com",
                fromName = "Fallback",
            }));

            var test = await admin.PostAsJsonAsync(Url("/admin/settings/email-routing/test"), new
            {
                type = ForgotPassword,
                to = "someone@example.com",
            });

            await EnsureSuccessAsync(test);

            var result = await test.Content.ReadFromJsonAsync<JsonElement>();
            result.GetProperty("fromAddress").GetString().Should().Be("fallback@example.com");
        }
        finally
        {
            await ClearDefaultAsync(admin);
        }
    }

    [Fact]
    public async Task AKindsOwnOverrideStillBeatsTheStoredDefault()
    {
        var admin = CreateClient(await AdminTokenAsync());

        try
        {
            await EnsureSuccessAsync(await admin.PutAsJsonAsync(Endpoint, new
            {
                fromAddress = "fallback@example.com",
                fromName = "Fallback",
            }));

            await EnsureSuccessAsync(await admin.PutAsJsonAsync(Url("/admin/settings/email-routing"), new
            {
                type = OrderCreated,
                fromAddress = "orders@example.com",
                fromName = "Orders",
                bcc = Array.Empty<object>(),
            }));

            var test = await admin.PostAsJsonAsync(Url("/admin/settings/email-routing/test"), new
            {
                type = OrderCreated,
                to = "someone@example.com",
            });

            await EnsureSuccessAsync(test);

            var result = await test.Content.ReadFromJsonAsync<JsonElement>();
            result.GetProperty("fromAddress").GetString().Should().Be("orders@example.com");
        }
        finally
        {
            // Put the kind back to inheriting, then clear the default.
            await admin.PutAsJsonAsync(Url("/admin/settings/email-routing"), new
            {
                type = OrderCreated,
                fromAddress = (string?)null,
                fromName = (string?)null,
                bcc = Array.Empty<object>(),
            });
            await ClearDefaultAsync(admin);
        }
    }

    [Fact]
    public async Task ClearingTheDefaultRestoresTheConfiguredAddress()
    {
        var admin = CreateClient(await AdminTokenAsync());

        var configured = (await admin.GetFromJsonAsync<JsonElement>(Endpoint))
            .GetProperty("configuredFromAddress").GetString();

        await EnsureSuccessAsync(await admin.PutAsJsonAsync(Endpoint, new
        {
            fromAddress = "temporary@example.com",
            fromName = (string?)null,
        }));

        await EnsureSuccessAsync(await admin.PutAsJsonAsync(Endpoint, new
        {
            fromAddress = (string?)null,
            fromName = (string?)null,
        }));

        var body = await admin.GetFromJsonAsync<JsonElement>(Endpoint);
        body.GetProperty("source").GetString().Should().Be("Configuration");
        body.GetProperty("fromAddress").GetString().Should().Be(configured);
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("missing@")]
    public async Task AnInvalidAddressIsRefused(string address)
    {
        var admin = CreateClient(await AdminTokenAsync());

        var response = await admin.PutAsJsonAsync(Endpoint, new
        {
            fromAddress = address,
            fromName = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ADisplayNameWithNoAddressIsRefused()
    {
        var admin = CreateClient(await AdminTokenAsync());

        // It would store a setting that can never apply, and read back as saved.
        var response = await admin.PutAsJsonAsync(Endpoint, new
        {
            fromAddress = (string?)null,
            fromName = "Orphaned Name",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ReadingOrSettingTheDefaultRequiresAnAdminSession()
    {
        var anonymous = CreateClient();

        (await anonymous.GetAsync(Endpoint)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

        (await anonymous.PutAsJsonAsync(Endpoint, new { fromAddress = "x@example.com", fromName = (string?)null }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
