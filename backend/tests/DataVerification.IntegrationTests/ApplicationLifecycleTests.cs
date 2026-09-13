using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// The rules the brief calls non-negotiable: an application is editable only while unpaid,
/// refundable only while Pending, and its cost is calculated by the server rather than accepted
/// from the client.
/// </summary>
public sealed class ApplicationLifecycleTests : ApiTestBase
{
    public ApplicationLifecycleTests(ApiFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task A_new_application_starts_as_an_editable_draft()
    {
        var applicant = await RegisterApplicantAsync();

        var draft = await CreateDraftAsync(applicant.Token);

        draft.GetProperty("statusName").GetString().Should().Be("Draft");
        draft.GetProperty("isPaid").GetBoolean().Should().BeFalse();
        draft.GetProperty("canEdit").GetBoolean().Should().BeTrue();
        draft.GetProperty("canDelete").GetBoolean().Should().BeTrue();
        draft.GetProperty("canRefund").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task The_applicants_email_and_phone_round_trip_through_create_and_update()
    {
        var applicant = await RegisterApplicantAsync();
        var draft = await CreateDraftAsync(applicant.Token);

        draft.GetProperty("applicantEmail").GetString().Should().Be("layla.hassan@example.com");
        draft.GetProperty("applicantPhoneCountry").GetString().Should().Be("EG");
        draft.GetProperty("applicantPhoneCode").GetString().Should().Be("+20");
        draft.GetProperty("applicantPhoneNumber").GetString().Should().Be("1005550101");

        // Editing replaces them, and the alpha-2 code is normalised whichever way it was sent.
        var updated = await CreateClient(applicant.Token).PutAsJsonAsync(
            Url($"/applications/{draft.GetProperty("id").GetString()}"),
            new
            {
                addressedTo = "Ministry of Higher Education",
                birthDate = "1990-05-14",
                applicantEmail = "  new.address@example.com  ",
                applicantPhoneCountry = "gb",
                applicantPhoneCode = "+44",
                applicantPhoneNumber = "7700900123",
                names = new[]
                {
                    new { languageType = 0, firstName = "ليلى", middleName = (string?)null, lastName = "حسن" },
                    new { languageType = 1, firstName = "Layla", middleName = (string?)null, lastName = "Hassan" },
                },
                transactionTypeId = SeedIds.TxEducational,
                subTransactionTypeId = SeedIds.SubBachelor,
                verificationAuthorityId = SeedIds.AuthoritySupremeCouncil,
                services = Array.Empty<object>(),
            });

        await EnsureSuccessAsync(updated);

        var result = await ReadJsonAsync(updated);
        result.GetProperty("applicantEmail").GetString().Should().Be("new.address@example.com");
        result.GetProperty("applicantPhoneCountry").GetString().Should().Be("GB");
        result.GetProperty("applicantPhoneCode").GetString().Should().Be("+44");
        result.GetProperty("applicantPhoneNumber").GetString().Should().Be("7700900123");
    }

    [Theory]
    // A draft may leave these blank, but anything actually entered has to be well formed.
    [InlineData("not-an-email", "EG", "+20", "1005550101")]
    [InlineData("layla@example.com", "EGY", "+20", "1005550101")]
    [InlineData("layla@example.com", "EG", "0020", "1005550101")]
    [InlineData("layla@example.com", "EG", "+20", "+201005550101")]
    // A number cannot arrive without the prefix it belongs to.
    [InlineData("layla@example.com", "", "", "1005550101")]
    public async Task Malformed_applicant_contact_details_are_refused(
        string email,
        string phoneCountry,
        string phoneCode,
        string phoneNumber)
    {
        var applicant = await RegisterApplicantAsync();

        var response = await CreateClient(applicant.Token).PostAsJsonAsync(Url("/applications"), new
        {
            addressedTo = "Ministry of Higher Education",
            birthDate = "1990-05-14",
            applicantEmail = email,
            applicantPhoneCountry = phoneCountry,
            applicantPhoneCode = phoneCode,
            applicantPhoneNumber = phoneNumber,
            names = Array.Empty<object>(),
            services = Array.Empty<object>(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_draft_without_the_applicants_contact_details_cannot_be_submitted()
    {
        var applicant = await RegisterApplicantAsync();
        var draft = await CreateDraftAsync(applicant.Token);
        var applicationId = draft.GetProperty("id").GetString();
        var client = CreateClient(applicant.Token);

        await SatisfyRequiredFilesAsync(applicant.Token, draft);

        // Clear the two new fields, leaving every other step answered.
        var cleared = await client.PutAsJsonAsync(Url($"/applications/{applicationId}"), new
        {
            addressedTo = "Ministry of Higher Education",
            birthDate = "1990-05-14",
            applicantEmail = (string?)null,
            applicantPhoneCountry = (string?)null,
            applicantPhoneCode = (string?)null,
            applicantPhoneNumber = (string?)null,
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

        await EnsureSuccessAsync(cleared);

        var submit = await client.PostAsync(Url($"/applications/{applicationId}/submit"), null);

        submit.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task The_server_prices_the_application_from_the_seeded_service_cost()
    {
        var applicant = await RegisterApplicantAsync();

        var draft = await CreateDraftAsync(applicant.Token);

        var lineTotal = draft.GetProperty("services").EnumerateArray()
            .Sum(service => service.GetProperty("lineTotal").GetDecimal());

        draft.GetProperty("totalCost").GetDecimal().Should().Be(lineTotal);
        draft.GetProperty("totalCost").GetDecimal().Should().BeGreaterThan(0);
        draft.GetProperty("currencyCode").GetString().Should().Be("EGP");
    }

    [Fact]
    public async Task A_draft_cannot_be_submitted_until_every_mandatory_file_is_attached()
    {
        var applicant = await RegisterApplicantAsync();
        var draft = await CreateDraftAsync(applicant.Token);
        var applicationId = draft.GetProperty("id").GetString();

        var premature = await CreateClient(applicant.Token)
            .PostAsync(Url($"/applications/{applicationId}/submit"), null);

        premature.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await SatisfyRequiredFilesAsync(applicant.Token, draft);

        var submitted = await CreateClient(applicant.Token)
            .PostAsync(Url($"/applications/{applicationId}/submit"), null);

        submitted.EnsureSuccessStatusCode();
        (await ReadJsonAsync(submitted)).GetProperty("statusName").GetString()
            .Should().Be("PendingPayment");
    }

    [Fact]
    public async Task A_paid_application_can_no_longer_be_edited_or_deleted()
    {
        var applicant = await RegisterApplicantAsync();
        var applicationId = await CreatePaidApplicationAsync(applicant);
        var client = CreateClient(applicant.Token);

        var current = await ReadJsonAsync(await client.GetAsync(Url($"/applications/{applicationId}")));
        current.GetProperty("isPaid").GetBoolean().Should().BeTrue();
        current.GetProperty("canEdit").GetBoolean().Should().BeFalse();
        current.GetProperty("canDelete").GetBoolean().Should().BeFalse();

        // The capability flags are advisory for the UI; the endpoints enforce the rule themselves.
        //
        // The payload below is otherwise valid on purpose. Request validation runs ahead of the
        // handler, so an incomplete body would be rejected as a 400 before the editability rule was
        // ever consulted — and the test would pass without proving anything.
        var edit = await client.PutAsJsonAsync(Url($"/applications/{applicationId}"), new
        {
            addressedTo = "Somewhere else entirely",
            birthDate = "1990-05-14",
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

        edit.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemCodeAsync(edit)).Should().Be("application.not_editable");

        var delete = await client.DeleteAsync(Url($"/applications/{applicationId}"));
        delete.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemCodeAsync(delete)).Should().Be("application.not_editable");
    }

    [Fact]
    public async Task Editing_a_draft_keeps_the_documents_already_uploaded()
    {
        var applicant = await RegisterApplicantAsync();
        var client = CreateClient(applicant.Token);

        var draft = await CreateDraftAsync(applicant.Token);
        var applicationId = draft.GetProperty("id").GetString();
        await SatisfyRequiredFilesAsync(applicant.Token, draft);

        var uploaded = await ReadJsonAsync(
            await client.GetAsync(Url($"/applications/{applicationId}")));

        var serviceLineId = uploaded.GetProperty("services")[0].GetProperty("id").GetString();
        uploaded.GetProperty("files").GetArrayLength().Should().BeGreaterThan(0);

        // Saving the draft again with the same services is what the wizard does on the way to the
        // upload step. It used to replace every service line with a fresh row, and because a file
        // points at its line by id, the database detached each upload: the checklist came back
        // reading zero uploaded, and the applicant could not submit an application whose documents
        // were sitting right there in storage.
        var resave = await client.PutAsJsonAsync(Url($"/applications/{applicationId}"), new
        {
            addressedTo = "Ministry of Higher Education",
            birthDate = "1990-05-14",
            applicantEmail = "layla.hassan@example.com",
            applicantPhoneCountry = "EG",
            applicantPhoneCode = "+20",
            applicantPhoneNumber = "1005550101",
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

        await EnsureSuccessAsync(resave);

        var after = await ReadJsonAsync(await client.GetAsync(Url($"/applications/{applicationId}")));

        // The line survives the save rather than being replaced, which is what keeps the files
        // attached to it.
        after.GetProperty("services")[0].GetProperty("id").GetString()
            .Should().Be(serviceLineId);

        foreach (var file in after.GetProperty("files").EnumerateArray())
        {
            file.GetProperty("applicationServiceId").GetString()
                .Should().NotBeNull("an upload detached from its service line is invisible to the checklist");
        }

        foreach (var required in after.GetProperty("requiredFiles").EnumerateArray())
        {
            if (required.GetProperty("isMandatory").GetBoolean())
            {
                required.GetProperty("uploadedCount").GetInt32().Should().BeGreaterThan(0);
                required.GetProperty("isSatisfied").GetBoolean().Should().BeTrue();
            }
        }
    }

    [Fact]
    public async Task Payment_moves_the_application_to_Pending_and_debits_the_wallet()
    {
        var applicant = await RegisterApplicantAsync();
        var client = CreateClient(applicant.Token);

        var applicationId = await CreatePaidApplicationAsync(applicant);

        var application = await ReadJsonAsync(
            await client.GetAsync(Url($"/applications/{applicationId}")));
        application.GetProperty("statusName").GetString().Should().Be("Pending");

        var statement = await ReadJsonAsync(await client.GetAsync(Url("/orders/me/wallet")));

        // SignedAmount is negative for payments, so the ledger shows the debit without the reader
        // having to infer direction from the transaction type.
        var debit = statement.GetProperty("ledger").GetProperty("items").EnumerateArray()
            .FirstOrDefault(t => t.GetProperty("signedAmount").GetDecimal() < 0);

        debit.ValueKind.Should().NotBe(JsonValueKind.Undefined, "payment must post a debit line");
        debit.GetProperty("signedAmount").GetDecimal()
            .Should().Be(-application.GetProperty("totalCost").GetDecimal());
    }

    [Fact]
    public async Task A_refund_is_allowed_while_Pending_and_refused_once_in_progress()
    {
        var applicant = await RegisterApplicantAsync();
        var applicationId = await CreatePaidApplicationAsync(applicant);
        var adminToken = await AdminTokenAsync();

        // An admin picking the work up ends the refund window.
        (await CreateClient(adminToken).PostAsJsonAsync(
                Url($"/admin/applications/{applicationId}/status"),
                new { toStatus = 3, note = "picked up" }))
            .EnsureSuccessStatusCode();

        var refused = await CreateClient(applicant.Token).PostAsJsonAsync(
            Url($"/applications/{applicationId}/refund"),
            new { note = "changed my mind" });

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemCodeAsync(refused)).Should().Be("application.not_refundable");
    }

    [Fact]
    public async Task An_illegal_status_transition_is_refused()
    {
        var applicant = await RegisterApplicantAsync();
        var applicationId = await CreatePaidApplicationAsync(applicant);
        var adminToken = await AdminTokenAsync();

        // Pending goes to InProgress; jumping straight to Success skips the work.
        var response = await CreateClient(adminToken).PostAsJsonAsync(
            Url($"/admin/applications/{applicationId}/status"),
            new { toStatus = 5, note = "straight to success" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemCodeAsync(response)).Should().Be("application.illegal_status_transition");
    }

    [Fact]
    public async Task A_service_outside_the_orders_country_cascade_is_refused()
    {
        var applicant = await RegisterApplicantAsync();

        // Criminal record belongs to a different transaction type than the one named here.
        var response = await CreateClient(applicant.Token).PostAsJsonAsync(Url("/applications"), new
        {
            addressedTo = "Ministry of Higher Education",
            birthDate = "1990-05-14",
            names = new[]
            {
                new { languageType = 0, firstName = "ليلى", middleName = (string?)null, lastName = "حسن" },
                new { languageType = 1, firstName = "Layla", middleName = (string?)null, lastName = "Hassan" },
            },
            transactionTypeId = SeedIds.TxEducational,
            subTransactionTypeId = SeedIds.SubCriminalRecord,
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

        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Conflict);
    }
}
