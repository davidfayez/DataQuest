using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Sdk;

namespace DataVerification.IntegrationTests;

/// <summary>
/// Shared plumbing for the HTTP suites.
///
/// Responses are read as <see cref="JsonElement"/> rather than deserialised into the application's
/// DTOs. These tests exist to pin the contract a browser actually receives, so reading the wire
/// format keeps them honest: a DTO rename that changes the JSON would silently keep compiling if
/// the test shared the type, but it would break the SPA.
/// </summary>
[Collection(ApiCollection.Name)]
public abstract class ApiTestBase
{
    private const string Base = "/api/v1";

    protected ApiTestBase(ApiFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Factory = factory;
    }

    protected ApiFactory Factory { get; }

    protected HttpClient CreateClient(string? token = null)
    {
        var client = Factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    protected static string Url(string path) => $"{Base}{path}";

    /// <summary>
    /// Like EnsureSuccessStatusCode, but puts the ProblemDetails body in the failure message.
    /// A bare "400 Bad Request" from a helper says nothing about which rule rejected the payload.
    /// </summary>
    protected static async Task<HttpResponseMessage> EnsureSuccessAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new XunitException(
                $"{(int)response.StatusCode} from {response.RequestMessage?.RequestUri}: {body}");
        }

        return response;
    }

    protected static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>The stable machine-readable code carried by every ProblemDetails this API returns.</summary>
    protected static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        var body = await ReadJsonAsync(response);
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    // --- actors -------------------------------------------------------------

    protected async Task<Applicant> RegisterApplicantAsync()
    {
        var client = CreateClient();
        var email = $"it-{Guid.NewGuid():N}@example.com";

        var registration = await client.PostAsJsonAsync(
            Url("/orders/register"),
            new { email, languageCode = "en" });
        registration.EnsureSuccessStatusCode();

        var registered = await ReadJsonAsync(registration);
        var orderNumber = registered.GetProperty("orderNumber").GetString()!;
        var password = registered.GetProperty("password").GetString()!;

        var login = await client.PostAsJsonAsync(
            Url("/orders/login"),
            new { orderNumber, password });
        login.EnsureSuccessStatusCode();

        var session = await ReadJsonAsync(login);
        var token = session.GetProperty("accessToken").GetString()!;

        // Country and currency are bound together; the order is unusable until both are chosen.
        // Setup also records the contact person, so all four values go up together.
        var setup = await CreateClient(token).PutAsJsonAsync(
            Url("/orders/setup"),
            new
            {
                verificationCountryId = SeedIds.Egypt,
                currencyId = SeedIds.Egp,
                contactPersonName = "Mona Fahmy",
                contactPersonPhoneCountry = "EG",
                contactPersonPhoneCode = "+20",
                contactPersonPhoneNumber = "1001234567",
            });
        setup.EnsureSuccessStatusCode();

        var orderId = session.GetProperty("orderId").GetString()!;
        return new Applicant(orderNumber, password, token, Guid.Parse(orderId), email);
    }

    protected async Task<string> AdminTokenAsync()
    {
        var response = await CreateClient().PostAsJsonAsync(
            Url("/admin/auth/login"),
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword });
        response.EnsureSuccessStatusCode();

        return (await ReadJsonAsync(response)).GetProperty("accessToken").GetString()!;
    }

    // --- application fixtures -----------------------------------------------

    /// <summary>Creates a Draft application on the seeded Egypt cascade.</summary>
    protected async Task<JsonElement> CreateDraftAsync(string token)
    {
        var response = await CreateClient(token).PostAsJsonAsync(Url("/applications"), new
        {
            addressedTo = "Ministry of Higher Education",
            birthDate = "1990-05-14",
            // The applicant's own contact details, mandatory at submit like the date of birth.
            applicantEmail = "layla.hassan@example.com",
            applicantPhoneCountry = "EG",
            applicantPhoneCode = "+20",
            applicantPhoneNumber = "1005550101",
            // Both scripts are mandatory: the authority receives the Arabic name, the applicant
            // reads the English one.
            names = new[]
            {
                new { languageType = 0, firstName = "ليلى", middleName = (string?)null, lastName = "حسن" },
                new { languageType = 1, firstName = "Layla", middleName = (string?)null, lastName = "Hassan" },
            },
            transactionTypeId = SeedIds.TxEducational,
            subTransactionTypeId = SeedIds.SubBachelor,
            verificationAuthorityId = SeedIds.AuthoritySupremeCouncil,
            services = new[]
            {
                new
                {
                    serviceTypeId = SeedIds.ServiceStandardVerification,
                    quantity = 1,
                    languageCode = "en",
                    isExpress = false,
                },
            },
        });

        await EnsureSuccessAsync(response);
        return await ReadJsonAsync(response);
    }

    /// <summary>Attaches every mandatory document, so the application can leave Draft.</summary>
    protected async Task SatisfyRequiredFilesAsync(string token, JsonElement application)
    {
        var client = CreateClient(token);
        var applicationId = application.GetProperty("id").GetString();

        foreach (var required in application.GetProperty("requiredFiles").EnumerateArray())
        {
            if (!required.GetProperty("isMandatory").GetBoolean())
            {
                continue;
            }

            using var content = new MultipartFormDataContent();
            var pdf = new ByteArrayContent(MinimalPdf);
            pdf.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");

            content.Add(pdf, "file", "evidence.pdf");
            content.Add(new StringContent(
                required.GetProperty("applicationServiceId").GetString()!), "applicationServiceId");
            content.Add(new StringContent(
                required.GetProperty("requiredFileId").GetString()!), "requiredFileId");

            var upload = await client.PostAsync(Url($"/applications/{applicationId}/files"), content);
            upload.EnsureSuccessStatusCode();
        }
    }

    /// <summary>Takes an application all the way to Pending: submitted, funded and paid.</summary>
    protected async Task<Guid> CreatePaidApplicationAsync(Applicant applicant)
    {
        ArgumentNullException.ThrowIfNull(applicant);

        var draft = await CreateDraftAsync(applicant.Token);
        var applicationId = Guid.Parse(draft.GetProperty("id").GetString()!);

        await SatisfyRequiredFilesAsync(applicant.Token, draft);

        var client = CreateClient(applicant.Token);
        (await client.PostAsync(Url($"/applications/{applicationId}/submit"), null))
            .EnsureSuccessStatusCode();

        // The wallet is the only funding route, so an admin tops it up before payment.
        var adminToken = await AdminTokenAsync();
        (await CreateClient(adminToken).PostAsJsonAsync(
                Url($"/admin/orders/{applicant.OrderId}/wallet/credit"),
                new { amount = 100000m, note = "integration test" }))
            .EnsureSuccessStatusCode();

        var payment = await client.PostAsJsonAsync(
            Url("/payments"),
            new { applicationIds = new[] { applicationId } });
        payment.EnsureSuccessStatusCode();

        return applicationId;
    }

    /// <summary>The shortest byte sequence the magic-number validator accepts as a PDF.</summary>
    private static byte[] MinimalPdf => "%PDF-1.4\n1 0 obj\n<<>>\nendobj\ntrailer\n<<>>\n%%EOF"u8.ToArray();

    /// <param name="Email">
    /// The address the order was registered with. Carried so a test can act as the mailbox behind
    /// the account — raising an anonymous ticket from it, for instance.
    /// </param>
    protected sealed record Applicant(
        string OrderNumber,
        string Password,
        string Token,
        Guid OrderId,
        string Email);
}

/// <summary>Fixed identifiers written by the seeder, mirrored here so tests read as scenarios.</summary>
internal static class SeedIds
{
    public const string Egypt = "22222222-0000-0000-0000-000000000001";
    public const string Egp = "22222222-0000-0000-0000-000000000002";
    public const string Usd = "22222222-0000-0000-0000-000000000003";
    public const string TxEducational = "44444444-0000-0000-0000-000000000001";
    public const string TxSecurity = "44444444-0000-0000-0000-000000000003";
    public const string SubBachelor = "55555555-0000-0000-0000-000000000001";
    public const string SubCriminalRecord = "55555555-0000-0000-0000-000000000005";
    public const string AuthoritySupremeCouncil = "66666666-0000-0000-0000-000000000001";
    public const string ServiceStandardVerification = "77777777-0000-0000-0000-000000000001";
}
