using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// Payment gateways: the catalogue, the integrations that name one, and the payment methods that
/// pay through them carrying their own credentials.
///
/// The rules worth pinning: every gateway's required fields and formats are enforced on the method
/// that holds them, secrets never come back with the method and survive an edit that did not touch
/// them, moving to another gateway drops them, and an integration in use is retired rather than
/// deleted and cannot change gateway underneath the methods using it.
/// </summary>
public sealed class PaymentGatewayIntegrationTests : ApiTestBase
{
    public PaymentGatewayIntegrationTests(ApiFactory factory) : base(factory) { }

    private static string Integrations => Url("/admin/payment-integrations");
    private static string Methods => Url("/admin/payment-methods");

    private static JsonObject IntegrationBody(
        string gatewayCode,
        Guid? id = null,
        string? name = null,
        string mode = "Sandbox")
    {
        // One name for both languages: each is unique on its own, so a fixed Arabic name would
        // collide the second time a test made an integration.
        var chosen = name ?? $"Integration {Guid.NewGuid():N}";

        return new()
        {
            ["id"] = id,
            ["nameAr"] = $"{chosen} ar",
            ["nameEn"] = chosen,
            ["descriptionAr"] = "وصف",
            ["descriptionEn"] = "Description",
            ["gatewayCode"] = gatewayCode,
            ["mode"] = mode == "Live" ? 1 : 0,
            ["sortOrder"] = 0,
            ["isActive"] = true,
        };
    }

    private async Task<(Guid Id, string Name)> CreateIntegrationAsync(
        HttpClient admin,
        string gatewayCode,
        string mode = "Sandbox")
    {
        var saved = await ReadJsonAsync(await EnsureSuccessAsync(
            await admin.PostAsJsonAsync(Integrations, IntegrationBody(gatewayCode, mode: mode))));
        return (saved.GetProperty("id").GetGuid(), saved.GetProperty("nameEn").GetString()!);
    }

    /// <summary>A type whose methods pay through a link, so no receiving accounts are needed.</summary>
    private static async Task<Guid> LinkTypeAsync(HttpClient admin)
    {
        var types = await ReadJsonAsync(await admin.GetAsync($"{Methods}/types?pageSize=200"));
        return types.GetProperty("items").EnumerateArray()
            .First(t => t.GetProperty("isActive").GetBoolean()
                && t.GetProperty("requiresExternalUrl").GetBoolean()
                && !t.GetProperty("requiresAccountNumber").GetBoolean())
            .GetProperty("id").GetGuid();
    }

    private static JsonObject MethodBody(
        Guid typeId,
        string name,
        Guid? id = null,
        Guid? integrationId = null,
        JsonObject? settings = null,
        JsonObject? secrets = null) => new()
    {
        ["id"] = id,
        ["paymentMethodTypeId"] = typeId,
        ["nameAr"] = $"{name} ar",
        ["nameEn"] = name,
        ["externalUrl"] = "https://pay.example.com",
        ["sortOrder"] = 0,
        ["isActive"] = true,
        ["countryIds"] = new JsonArray(SeedIds.Egypt),
        ["currencyIds"] = new JsonArray(SeedIds.Egp),
        ["accounts"] = new JsonArray(),
        ["notificationEmails"] = new JsonArray(),
        ["gatewayIntegrationId"] = integrationId,
        ["gatewaySettings"] = settings,
        ["gatewaySecrets"] = secrets,
    };

    private static JsonObject PayPalSettings() => new()
    {
        ["clientId"] = "AbCdClientId",
        ["returnUrl"] = "https://example.com/done",
    };

    private static JsonObject SepaSettings() => new()
    {
        ["scheme"] = "SCT",
        ["creditorName"] = "Data Quest GmbH",
        ["iban"] = "DE89370400440532013000",
        ["bic"] = "COBADEFFXXX",
    };

    [Fact]
    public async Task TheCatalogueListsWorldGatewaysWithTheirOwnSettings()
    {
        var admin = CreateClient(await AdminTokenAsync());

        var gateways = (await ReadJsonAsync(await EnsureSuccessAsync(await admin.GetAsync($"{Integrations}/gateways"))))
            .EnumerateArray().ToList();

        gateways.Count.Should().BeGreaterThan(40);
        var codes = gateways.Select(g => g.GetProperty("code").GetString()).ToList();
        codes.Should().Contain(["paypal", "stripe", "fawry", "paymob", "checkout_com", "sepa", "ach", "swift", "custom"]);
        codes.Should().OnlyHaveUniqueItems();

        var fawry = gateways.Single(g => g.GetProperty("code").GetString() == "fawry");
        var fawryFields = fawry.GetProperty("fields").EnumerateArray().ToList();
        fawryFields.Should().Contain(f => f.GetProperty("key").GetString() == "merchantCode"
            && f.GetProperty("isRequired").GetBoolean()
            && f.GetProperty("typeName").GetString() == "Text");
        fawryFields.Should().Contain(f => f.GetProperty("key").GetString() == "securityKey"
            && f.GetProperty("typeName").GetString() == "Secret");

        var ach = gateways.Single(g => g.GetProperty("code").GetString() == "ach");
        ach.GetProperty("categoryName").GetString().Should().Be("BankTransfer");
        ach.GetProperty("fields").EnumerateArray()
            .Single(f => f.GetProperty("key").GetString() == "accountType")
            .GetProperty("options").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task AnIntegrationNamesAGatewayAndHoldsNoSettings()
    {
        var admin = CreateClient(await AdminTokenAsync());

        var saved = await ReadJsonAsync(await EnsureSuccessAsync(
            await admin.PostAsJsonAsync(Integrations, IntegrationBody("sepa"))));
        var id = saved.GetProperty("id").GetGuid();

        try
        {
            saved.GetProperty("gatewayName").GetString().Should().Be("SEPA");
            saved.GetProperty("modeName").GetString().Should().Be("Sandbox");
            // The settings belong to the methods now; nothing of the sort comes back here.
            saved.TryGetProperty("settings", out _).Should().BeFalse();
            saved.TryGetProperty("configuredSecrets", out _).Should().BeFalse();

            var list = await ReadJsonAsync(await EnsureSuccessAsync(
                await admin.GetAsync($"{Integrations}?gatewayCode=sepa&pageSize=200")));
            list.GetProperty("items").EnumerateArray().Should().Contain(i => i.GetProperty("id").GetGuid() == id);

            // A gateway that is not in the catalogue is refused.
            (await admin.PostAsJsonAsync(Integrations, IntegrationBody("not-a-gateway")))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally
        {
            await admin.DeleteAsync($"{Integrations}/{id}");
        }
    }

    [Fact]
    public async Task AMethodCarriesTheGatewaysSettingsAndEachGatewaysRulesAreEnforced()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var typeId = await LinkTypeAsync(admin);
        var (integrationId, _) = await CreateIntegrationAsync(admin, "ach");
        var name = $"ACH method {Guid.NewGuid():N}";
        Guid? methodId = null;

        try
        {
            // A required field missing and a malformed routing number, reported together.
            var refused = await admin.PostAsJsonAsync(Methods, MethodBody(typeId, name, null, integrationId, new JsonObject
            {
                ["routingNumber"] = "12345",
                ["accountNumber"] = "000123456789",
                ["accountType"] = "checking",
            }));
            refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var errors = (await ReadJsonAsync(refused)).GetProperty("errors").EnumerateObject()
                .Select(e => e.Name.ToLowerInvariant()).ToList();
            errors.Should().Contain(["fields.companyname", "fields.routingnumber"]);

            // An option the gateway does not offer.
            (await admin.PostAsJsonAsync(Methods, MethodBody(typeId, name, null, integrationId, new JsonObject
            {
                ["companyName"] = "Data Quest",
                ["routingNumber"] = "021000021",
                ["accountNumber"] = "000123456789",
                ["accountType"] = "crypto",
            }))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var saved = await ReadJsonAsync(await EnsureSuccessAsync(
                await admin.PostAsJsonAsync(Methods, MethodBody(typeId, name, null, integrationId, new JsonObject
                {
                    ["companyName"] = "Data Quest",
                    ["routingNumber"] = "021000021",
                    ["accountNumber"] = "000123456789",
                    ["accountType"] = "checking",
                }))));
            methodId = saved.GetProperty("id").GetGuid();

            saved.GetProperty("gatewayIntegrationId").GetGuid().Should().Be(integrationId);
            saved.GetProperty("gatewayCode").GetString().Should().Be("ach");
            saved.GetProperty("gatewaySettings").GetProperty("routingNumber").GetString().Should().Be("021000021");

            // Detaching the integration clears what was entered for it.
            var detached = await ReadJsonAsync(await EnsureSuccessAsync(
                await admin.PostAsJsonAsync(Methods, MethodBody(typeId, name, methodId, null))));
            detached.GetProperty("gatewayIntegrationId").ValueKind.Should().Be(JsonValueKind.Null);
            detached.GetProperty("gatewaySettings").EnumerateObject().Should().BeEmpty();
        }
        finally
        {
            if (methodId is { } id) await admin.DeleteAsync($"{Methods}/{id}");
            await admin.DeleteAsync($"{Integrations}/{integrationId}");
        }
    }

    [Fact]
    public async Task MethodSecretsStayHiddenAndSurviveAnEditThatDidNotTouchThem()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var typeId = await LinkTypeAsync(admin);
        var (paypalId, _) = await CreateIntegrationAsync(admin, "paypal");
        var (sepaId, _) = await CreateIntegrationAsync(admin, "sepa");
        var name = $"PayPal method {Guid.NewGuid():N}";
        const string Secret = "paypal-client-secret-not-real";
        Guid? methodId = null;

        try
        {
            // A required secret that was never supplied.
            (await admin.PostAsJsonAsync(Methods, MethodBody(typeId, name, null, paypalId, PayPalSettings())))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var saved = await ReadJsonAsync(await EnsureSuccessAsync(await admin.PostAsJsonAsync(
                Methods,
                MethodBody(typeId, name, null, paypalId, PayPalSettings(), new JsonObject { ["clientSecret"] = Secret }))));
            methodId = saved.GetProperty("id").GetGuid();

            if (!saved.GetProperty("canStoreGatewaySecrets").GetBoolean()) return;

            saved.GetProperty("configuredGatewaySecrets").EnumerateArray().Select(s => s.GetString())
                .Should().BeEquivalentTo(["clientSecret"]);
            (await (await admin.GetAsync($"{Methods}/{methodId}")).Content.ReadAsStringAsync())
                .Should().NotContain(Secret);

            // Shown only on request, never cached.
            var shown = await admin.GetAsync($"{Methods}/{methodId}/secrets/clientSecret");
            await EnsureSuccessAsync(shown);
            shown.Headers.CacheControl!.NoStore.Should().BeTrue();
            (await ReadJsonAsync(shown)).GetProperty("value").GetString().Should().Be(Secret);

            // An edit that leaves the secret out keeps it.
            await EnsureSuccessAsync(await admin.PostAsJsonAsync(
                Methods, MethodBody(typeId, name, methodId, paypalId, PayPalSettings(), new JsonObject())));
            (await ReadJsonAsync(await admin.GetAsync($"{Methods}/{methodId}/secrets/clientSecret")))
                .GetProperty("value").GetString().Should().Be(Secret);

            // Clearing a required secret is refused.
            (await admin.PostAsJsonAsync(Methods, MethodBody(
                typeId, name, methodId, paypalId, PayPalSettings(), new JsonObject { ["clientSecret"] = "" })))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);

            // Moving to another gateway drops what the old one held.
            var moved = await ReadJsonAsync(await EnsureSuccessAsync(await admin.PostAsJsonAsync(
                Methods, MethodBody(typeId, name, methodId, sepaId, SepaSettings()))));
            moved.GetProperty("configuredGatewaySecrets").GetArrayLength().Should().Be(0);
            moved.GetProperty("gatewaySettings").TryGetProperty("clientId", out _).Should().BeFalse();
            moved.GetProperty("gatewaySettings").GetProperty("iban").GetString().Should().Be("DE89370400440532013000");
        }
        finally
        {
            if (methodId is { } id) await admin.DeleteAsync($"{Methods}/{id}");
            await admin.DeleteAsync($"{Integrations}/{paypalId}");
            await admin.DeleteAsync($"{Integrations}/{sepaId}");
        }
    }

    [Fact]
    public async Task AnIntegrationInUseIsRetiredAndKeepsItsGateway()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var typeId = await LinkTypeAsync(admin);
        var (integrationId, integrationName) = await CreateIntegrationAsync(admin, "sepa");
        var name = $"SEPA method {Guid.NewGuid():N}";
        Guid? methodId = null;

        try
        {
            var method = await ReadJsonAsync(await EnsureSuccessAsync(await admin.PostAsJsonAsync(
                Methods, MethodBody(typeId, name, null, integrationId, SepaSettings()))));
            methodId = method.GetProperty("id").GetGuid();

            (await ReadJsonAsync(await admin.GetAsync($"{Integrations}/{integrationId}")))
                .GetProperty("methodCount").GetInt32().Should().Be(1);

            // Its gateway cannot move under the method that is configured against it.
            var refused = await admin.PostAsJsonAsync(
                Integrations, IntegrationBody("stripe", integrationId, integrationName));
            refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await ReadProblemCodeAsync(refused)).Should().Be("payment_integration.gateway_in_use");

            // In use: retired, not removed.
            (await ReadJsonAsync(await EnsureSuccessAsync(await admin.DeleteAsync($"{Integrations}/{integrationId}"))))
                .GetInt32().Should().Be(1);

            // The method keeps it, but a retired integration cannot be newly chosen.
            (await ReadJsonAsync(await admin.GetAsync($"{Methods}/{methodId}")))
                .GetProperty("gatewayIntegrationId").GetGuid().Should().Be(integrationId);

            var other = $"Other method {Guid.NewGuid():N}";
            (await admin.PostAsJsonAsync(Methods, MethodBody(typeId, other, null, integrationId, SepaSettings())))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);

            await EnsureSuccessAsync(await admin.PostAsJsonAsync(Methods, MethodBody(typeId, name, methodId, null)));

            // Unused again: now it goes.
            (await ReadJsonAsync(await EnsureSuccessAsync(await admin.DeleteAsync($"{Integrations}/{integrationId}"))))
                .GetInt32().Should().Be(0);
        }
        finally
        {
            if (methodId is { } id) await admin.DeleteAsync($"{Methods}/{id}");
            await admin.DeleteAsync($"{Integrations}/{integrationId}");
        }
    }

    [Fact]
    public async Task TheEndpointsNeedAnAdministrator()
    {
        var anonymous = CreateClient();
        (await anonymous.GetAsync($"{Integrations}/gateways")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync(Integrations)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var applicant = await RegisterApplicantAsync();
        var client = CreateClient(applicant.Token);
        (await client.GetAsync(Integrations)).StatusCode
            .Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
        (await client.GetAsync($"{Methods}/{Guid.NewGuid()}/secrets/apiKey")).StatusCode
            .Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }
}
