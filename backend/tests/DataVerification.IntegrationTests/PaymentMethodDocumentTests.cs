using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// Required documents on a payment method — the same shape a service type asks for — and the
/// deposit that has to carry them.
///
/// The rules worth pinning: the admin editor stores and edits them in place, the applicant is shown
/// them, a deposit is refused without a mandatory document, a required detail or the right format,
/// and what was sent stays readable on the request even after the method stops asking for it.
/// </summary>
public sealed class PaymentMethodDocumentTests : ApiTestBase
{
    public PaymentMethodDocumentTests(ApiFactory factory) : base(factory) { }

    private static string Methods => Url("/admin/payment-methods");

    private static byte[] Pdf() => "%PDF-1.4\n1 0 obj\n<<>>\nendobj\ntrailer\n<<>>\n%%EOF"u8.ToArray();

    private static byte[] Png() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    /// <summary>A transfer type that needs receiving numbers and nothing else, as Vodafone Cash is seeded.</summary>
    private static async Task<Guid> NumbersOnlyTypeAsync(HttpClient admin)
    {
        var page = await ReadJsonAsync(await admin.GetAsync($"{Methods}/types?pageSize=200"));

        return page.GetProperty("items").EnumerateArray()
            .First(type => type.GetProperty("isActive").GetBoolean()
                && type.GetProperty("kindName").GetString() == "Transfer"
                && type.GetProperty("requiresAccountNumber").GetBoolean()
                && !type.GetProperty("requiresBarcode").GetBoolean()
                && !type.GetProperty("requiresBank").GetBoolean()
                && !type.GetProperty("requiresExternalUrl").GetBoolean())
            .GetProperty("id").GetGuid();
    }

    private static JsonObject MethodBody(Guid typeId, string name, JsonArray requiredFiles, Guid? id = null) => new()
    {
        ["id"] = id,
        ["paymentMethodTypeId"] = typeId,
        ["nameAr"] = $"{name} ar",
        ["nameEn"] = name,
        ["sortOrder"] = 0,
        ["isActive"] = true,
        ["countryIds"] = new JsonArray(SeedIds.Egypt),
        ["currencyIds"] = new JsonArray(SeedIds.Egp),
        ["accounts"] = new JsonArray(new JsonObject
        {
            ["labelAr"] = "رئيسي",
            ["labelEn"] = "Main",
            ["accountNumber"] = $"010{Random.Shared.Next(10_000_000, 99_999_999)}",
            ["isActive"] = true,
            ["sortOrder"] = 0,
        }),
        ["notificationEmails"] = new JsonArray(),
        ["requiredFiles"] = requiredFiles,
    };

    private static JsonArray TwoDocuments() => new(
        new JsonObject
        {
            ["nameAr"] = "صورة البطاقة",
            ["nameEn"] = "ID card",
            ["isMandatory"] = true,
            ["maxFiles"] = 2,
            ["allowedFileTypes"] = new JsonArray("pdf"),
            ["fields"] = new JsonArray(new JsonObject
            {
                ["nameAr"] = "رقم البطاقة",
                ["nameEn"] = "ID number",
                ["fieldType"] = 0,
                ["isRequired"] = true,
                ["sortOrder"] = 0,
                ["maxLength"] = 20,
                ["dateRule"] = 0,
                ["options"] = new JsonArray(),
            }),
        },
        new JsonObject
        {
            ["nameAr"] = "خطاب",
            ["nameEn"] = "Cover letter",
            ["isMandatory"] = false,
            ["maxFiles"] = 1,
            ["fields"] = new JsonArray(),
        });

    private sealed record CreatedMethod(Guid Id, Guid AccountId, Guid IdCardId, Guid IdNumberFieldId, Guid LetterId, Guid TypeId);

    private static async Task<CreatedMethod> CreateMethodAsync(HttpClient admin, string? name = null)
    {
        var typeId = await NumbersOnlyTypeAsync(admin);
        var saved = await ReadJsonAsync(await EnsureSuccessAsync(await admin.PostAsJsonAsync(
            Methods, MethodBody(typeId, name ?? $"Docs method {Guid.NewGuid():N}", TwoDocuments()))));

        var documents = saved.GetProperty("requiredFiles").EnumerateArray().ToList();
        var idCard = documents.Single(d => d.GetProperty("nameEn").GetString() == "ID card");
        var letter = documents.Single(d => d.GetProperty("nameEn").GetString() == "Cover letter");

        return new CreatedMethod(
            saved.GetProperty("id").GetGuid(),
            saved.GetProperty("accounts")[0].GetProperty("id").GetGuid(),
            idCard.GetProperty("id").GetGuid(),
            idCard.GetProperty("fields")[0].GetProperty("id").GetGuid(),
            letter.GetProperty("id").GetGuid(),
            typeId);
    }

    private static async Task<HttpResponseMessage> DepositAsync(
        HttpClient applicant,
        CreatedMethod method,
        IReadOnlyList<(Guid DocumentId, byte[] Bytes, string FileName, string ContentType)> documents,
        IReadOnlyList<(Guid FieldId, string Value)> values)
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent(method.Id.ToString()), "paymentMethodId" },
            { new StringContent(method.AccountId.ToString()), "paymentMethodAccountId" },
            { new StringContent("100") , "amount" },
        };

        // The seeded numbers-only type asks for proof of the transfer as well.
        var proof = new ByteArrayContent(Png());
        proof.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(proof, "files", "receipt.png");

        var groups = documents.GroupBy(d => d.DocumentId).ToList();
        for (var i = 0; i < groups.Count; i++)
        {
            content.Add(new StringContent(groups[i].Key.ToString()), $"documents[{i}].requiredFileId");
            foreach (var file in groups[i])
            {
                var part = new ByteArrayContent(file.Bytes);
                part.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
                content.Add(part, $"documents[{i}].files", file.FileName);
            }
        }

        for (var i = 0; i < values.Count; i++)
        {
            content.Add(new StringContent(values[i].FieldId.ToString()), $"values[{i}].fieldId");
            content.Add(new StringContent(values[i].Value), $"values[{i}].value");
        }

        return await applicant.PostAsync(Url("/orders/me/wallet/requests/deposit"), content);
    }

    [Fact]
    public async Task DocumentsAreStoredAndEditedInPlace()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var method = await CreateMethodAsync(admin);

        var loaded = await ReadJsonAsync(await admin.GetAsync($"{Methods}/{method.Id}"));
        var documents = loaded.GetProperty("requiredFiles").EnumerateArray().ToList();
        documents.Should().HaveCount(2);

        var idCard = documents.Single(d => d.GetProperty("id").GetGuid() == method.IdCardId);
        idCard.GetProperty("isMandatory").GetBoolean().Should().BeTrue();
        idCard.GetProperty("maxFiles").GetInt32().Should().Be(2);
        idCard.GetProperty("paymentMethodId").GetGuid().Should().Be(method.Id);
        idCard.GetProperty("serviceTypeId").ValueKind.Should().Be(JsonValueKind.Null);
        idCard.GetProperty("allowedFileTypes").EnumerateArray().Select(t => t.GetString()).Should().Equal("pdf");
        idCard.GetProperty("fields")[0].GetProperty("nameEn").GetString().Should().Be("ID number");

        // Saved back with the ids, the ID card is renamed in place and the letter is dropped.
        var body = JsonNode.Parse(loaded.GetRawText())!.AsObject();
        var kept = body["requiredFiles"]!.AsArray()
            .Single(d => d!["id"]!.GetValue<Guid>() == method.IdCardId)!.DeepClone().AsObject();
        kept["nameEn"] = "National ID card";
        body["requiredFiles"] = new JsonArray(kept);
        body["integration"] = null;

        var saved = await ReadJsonAsync(await EnsureSuccessAsync(await admin.PostAsJsonAsync(Methods, body)));
        var after = saved.GetProperty("requiredFiles").EnumerateArray().ToList();
        after.Should().ContainSingle();
        after[0].GetProperty("id").GetGuid().Should().Be(method.IdCardId);
        after[0].GetProperty("nameEn").GetString().Should().Be("National ID card");
        after[0].GetProperty("fields")[0].GetProperty("id").GetGuid().Should().Be(method.IdNumberFieldId);
    }

    [Fact]
    public async Task SavingWithoutTheListLeavesTheDocumentsAlone()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var method = await CreateMethodAsync(admin);

        var loaded = await ReadJsonAsync(await admin.GetAsync($"{Methods}/{method.Id}"));
        var body = JsonNode.Parse(loaded.GetRawText())!.AsObject();
        body.Remove("requiredFiles");
        body["integration"] = null;

        var saved = await ReadJsonAsync(await EnsureSuccessAsync(await admin.PostAsJsonAsync(Methods, body)));
        saved.GetProperty("requiredFiles").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task AnInvalidDocumentIsRefused()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var typeId = await NumbersOnlyTypeAsync(admin);

        var documents = TwoDocuments();
        documents[0]!["nameEn"] = "";

        var response = await admin.PostAsJsonAsync(Methods, MethodBody(typeId, $"Bad {Guid.NewGuid():N}", documents));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TheApplicantIsShownTheDocuments()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var method = await CreateMethodAsync(admin);
        var applicant = CreateClient((await RegisterApplicantAsync()).Token);

        var options = await ReadJsonAsync(await applicant.GetAsync(Url("/orders/me/payment-methods")));
        var option = options.EnumerateArray().Single(o => o.GetProperty("id").GetGuid() == method.Id);

        var documents = option.GetProperty("requiredFiles").EnumerateArray().ToList();
        documents.Should().HaveCount(2);
        documents[0].GetProperty("nameEn").GetString().Should().Be("ID card");
        documents[0].GetProperty("allowedExtensions").EnumerateArray().Select(e => e.GetString())
            .Should().Equal(".pdf");
        documents[0].GetProperty("fields")[0].GetProperty("isRequired").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ADepositWithoutAMandatoryDocumentIsRefused()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var method = await CreateMethodAsync(admin);
        var applicant = CreateClient((await RegisterApplicantAsync()).Token);

        var response = await DepositAsync(applicant, method, [], [(method.IdNumberFieldId, "A1234")]);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemCodeAsync(response)).Should().Be("wallet_request.document_required");
    }

    [Fact]
    public async Task ADepositWithoutARequiredDetailIsRefused()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var method = await CreateMethodAsync(admin);
        var applicant = CreateClient((await RegisterApplicantAsync()).Token);

        var response = await DepositAsync(
            applicant, method, [(method.IdCardId, Pdf(), "id.pdf", "application/pdf")], []);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemCodeAsync(response)).Should().Be("field.required");
    }

    [Fact]
    public async Task AFileInAFormatTheDocumentDoesNotTakeIsRefused()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var method = await CreateMethodAsync(admin);
        var applicant = CreateClient((await RegisterApplicantAsync()).Token);

        // The ID card takes PDF only.
        var response = await DepositAsync(
            applicant,
            method,
            [(method.IdCardId, Png(), "id.png", "image/png")],
            [(method.IdNumberFieldId, "A1234")]);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemCodeAsync(response)).Should().Be("file.unsupported_type");
    }

    [Fact]
    public async Task MoreFilesThanADocumentTakesAreRefused()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var method = await CreateMethodAsync(admin);
        var applicant = CreateClient((await RegisterApplicantAsync()).Token);

        var response = await DepositAsync(
            applicant,
            method,
            [
                (method.IdCardId, Pdf(), "id.pdf", "application/pdf"),
                (method.LetterId, Pdf(), "letter-1.pdf", "application/pdf"),
                (method.LetterId, Pdf(), "letter-2.pdf", "application/pdf"),
            ],
            [(method.IdNumberFieldId, "A1234")]);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemCodeAsync(response)).Should().Be("file.too_many_for_document");
    }

    [Fact]
    public async Task ADepositCarriesItsDocumentsToTheReviewerAndKeepsThemAfterTheMethodChanges()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var method = await CreateMethodAsync(admin);
        var applicant = CreateClient((await RegisterApplicantAsync()).Token);

        var created = await ReadJsonAsync(await EnsureSuccessAsync(await DepositAsync(
            applicant,
            method,
            [
                (method.IdCardId, Pdf(), "id-front.pdf", "application/pdf"),
                (method.IdCardId, Pdf(), "id-back.pdf", "application/pdf"),
            ],
            [(method.IdNumberFieldId, "A1234")])));
        var requestId = created.GetProperty("id").GetGuid();

        async Task AssertReadableAsync(JsonElement request)
        {
            var files = request.GetProperty("files").EnumerateArray().ToList();
            files.Should().HaveCount(3);
            files.Count(f => f.GetProperty("documentName").ValueKind == JsonValueKind.Null).Should().Be(1);
            files.Where(f => f.GetProperty("requiredFileId").ValueKind != JsonValueKind.Null)
                .Should().OnlyContain(f => f.GetProperty("requiredFileId").GetGuid() == method.IdCardId
                    && f.GetProperty("documentName").GetString() == "ID card");

            var value = request.GetProperty("documentValues").EnumerateArray().Single();
            value.GetProperty("documentName").GetString().Should().Be("ID card");
            value.GetProperty("fieldName").GetString().Should().Be("ID number");
            value.GetProperty("value").GetString().Should().Be("A1234");

            await Task.CompletedTask;
        }

        await AssertReadableAsync(await ReadJsonAsync(
            await applicant.GetAsync(Url($"/orders/me/wallet/requests/{requestId}"))));

        var forReviewer = await ReadJsonAsync(await admin.GetAsync(Url($"/admin/wallet-requests/{requestId}")));
        await AssertReadableAsync(forReviewer);

        var documentFile = forReviewer.GetProperty("files").EnumerateArray()
            .First(f => f.GetProperty("requiredFileId").ValueKind != JsonValueKind.Null);
        var download = await admin.GetAsync(
            Url($"/admin/wallet-requests/{requestId}/files/{documentFile.GetProperty("id").GetGuid()}"));
        await EnsureSuccessAsync(download);
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(Pdf());

        // The method stops asking for documents. Both are dropped from the editor — the one
        // something was sent against is only switched off — and the request still reads the same.
        var loaded = await ReadJsonAsync(await admin.GetAsync($"{Methods}/{method.Id}"));
        var body = JsonNode.Parse(loaded.GetRawText())!.AsObject();
        body["requiredFiles"] = new JsonArray();
        body["integration"] = null;
        var saved = await ReadJsonAsync(await EnsureSuccessAsync(await admin.PostAsJsonAsync(Methods, body)));
        saved.GetProperty("requiredFiles").GetArrayLength().Should().Be(0);

        await AssertReadableAsync(await ReadJsonAsync(await admin.GetAsync(Url($"/admin/wallet-requests/{requestId}"))));

        var options = await ReadJsonAsync(await applicant.GetAsync(Url("/orders/me/payment-methods")));
        options.EnumerateArray().Single(o => o.GetProperty("id").GetGuid() == method.Id)
            .GetProperty("requiredFiles").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task AReferenceFileOnAMethodDocumentIsServedToApplicantsAndKeptToItsOwnEndpoints()
    {
        var admin = CreateClient(await AdminTokenAsync());
        var method = await CreateMethodAsync(admin);

        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Png());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "file", "example.png");
        content.Add(new StringContent("مثال"), "labelAr");
        content.Add(new StringContent("Example"), "labelEn");

        var created = await ReadJsonAsync(await EnsureSuccessAsync(
            await admin.PostAsync($"{Methods}/required-files/{method.IdCardId}/samples", content)));
        var sampleId = created.GetProperty("id").GetGuid();

        var loaded = await ReadJsonAsync(await admin.GetAsync($"{Methods}/{method.Id}"));
        loaded.GetProperty("requiredFiles").EnumerateArray()
            .Single(d => d.GetProperty("id").GetGuid() == method.IdCardId)
            .GetProperty("samples")[0].GetProperty("id").GetGuid().Should().Be(sampleId);

        (await admin.GetAsync($"{Methods}/samples/{sampleId}/file")).StatusCode.Should().Be(HttpStatusCode.OK);

        var applicant = CreateClient((await RegisterApplicantAsync()).Token);
        var served = await applicant.GetAsync(Url($"/required-files/samples/{sampleId}/file"));
        await EnsureSuccessAsync(served);
        (await served.Content.ReadAsByteArrayAsync()).Should().Equal(Png());

        // The service-type endpoints, behind a different permission, cannot reach it.
        (await admin.DeleteAsync(Url($"/admin/lookups/service-types/samples/{sampleId}")))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.GetAsync(Url($"/admin/lookups/service-types/samples/{sampleId}/file")))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await admin.DeleteAsync($"{Methods}/samples/{sampleId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await applicant.GetAsync(Url($"/required-files/samples/{sampleId}/file")))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TheNewEndpointsNeedAnAdminSession()
    {
        var anonymous = CreateClient();

        (await anonymous.GetAsync($"{Methods}/samples/{Guid.NewGuid()}/file"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.DeleteAsync($"{Methods}/samples/{Guid.NewGuid()}"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
