using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace DataVerification.IntegrationTests;

/// <summary>
/// What a downloaded file is called, from both sides of the platform.
///
/// The front ends fetch files as blobs and name them from the DTO, so the name the API reports and
/// the name in its own <c>Content-Disposition</c> header both have to be right — they are what
/// actually reaches the disk.
/// </summary>
public sealed class DownloadNamingTests : ApiTestBase
{
    public DownloadNamingTests(ApiFactory factory) : base(factory) { }

    /// <summary>Uploads a file against the first mandatory document and returns the application.</summary>
    private async Task<(JsonElement Application, Guid FileId, string DocumentLabel)> UploadAsync(
        Applicant applicant)
    {
        var draft = await CreateDraftAsync(applicant.Token);
        var applicationId = draft.GetProperty("id").GetString()!;

        var required = draft.GetProperty("requiredFiles").EnumerateArray().First();
        var requiredFileId = required.GetProperty("requiredFileId").GetString()!;
        var label = required.GetProperty("name").GetString()!;

        using var content = new MultipartFormDataContent();
        var bytes = new ByteArrayContent("%PDF-1.4\nnot really a document"u8.ToArray());
        bytes.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        // The endpoint binds from the form, so the two ids travel in the body beside the file.
        content.Add(bytes, "file", "whatever-i-called-it.pdf");
        content.Add(new StringContent(requiredFileId), "requiredFileId");
        content.Add(
            new StringContent(required.GetProperty("applicationServiceId").GetString()!),
            "applicationServiceId");

        var upload = await CreateClient(applicant.Token).PostAsync(
            Url($"/applications/{applicationId}/files"),
            content);

        await EnsureSuccessAsync(upload);
        var uploaded = await upload.Content.ReadFromJsonAsync<JsonElement>();

        var reread = await CreateClient(applicant.Token).GetAsync(Url($"/applications/{applicationId}"));
        await EnsureSuccessAsync(reread);

        return (
            await reread.Content.ReadFromJsonAsync<JsonElement>(),
            uploaded.GetProperty("id").GetGuid(),
            label);
    }

    [Fact]
    public async Task TheApplicantsFileIsNamedForItsApplicationOrderAndDocument()
    {
        var applicant = await RegisterApplicantAsync();
        var (application, fileId, label) = await UploadAsync(applicant);

        var applicationNumber = application.GetProperty("applicationNumber").GetString();

        var file = application.GetProperty("files").EnumerateArray()
            .First(f => f.GetProperty("id").GetGuid() == fileId);

        // The name it arrived with is still shown in the list — it is what the applicant
        // recognises — but it is not what the download is called.
        file.GetProperty("fileName").GetString().Should().Be("whatever-i-called-it.pdf");

        file.GetProperty("downloadName").GetString()
            .Should().Be($"{applicationNumber}_{applicant.OrderNumber}_{label}.pdf");
    }

    [Fact]
    public async Task TheHeaderCarriesTheSameName()
    {
        var applicant = await RegisterApplicantAsync();
        var (application, fileId, label) = await UploadAsync(applicant);

        var applicationId = application.GetProperty("id").GetString();
        var applicationNumber = application.GetProperty("applicationNumber").GetString();

        var response = await CreateClient(applicant.Token)
            .GetAsync(Url($"/applications/{applicationId}/files/{fileId}"));

        await EnsureSuccessAsync(response);

        // A front end that lets the browser name the file must land on the same answer as one
        // that names it from the DTO.
        var disposition = response.Content.Headers.ContentDisposition!;
        var named = disposition.FileNameStar ?? disposition.FileName!.Trim('"');

        named.Should().Be($"{applicationNumber}_{applicant.OrderNumber}_{label}.pdf");
    }

    [Fact]
    public async Task AnAdminDownloadsTheSameFileUnderTheSameName()
    {
        var applicant = await RegisterApplicantAsync();
        var (application, fileId, label) = await UploadAsync(applicant);

        var applicationId = application.GetProperty("id").GetString();
        var applicationNumber = application.GetProperty("applicationNumber").GetString();

        var admin = CreateClient(await AdminTokenAsync());
        var response = await admin.GetAsync(
            Url($"/admin/applications/{applicationId}/files/{fileId}"));

        await EnsureSuccessAsync(response);

        var disposition = response.Content.Headers.ContentDisposition!;
        var named = disposition.FileNameStar ?? disposition.FileName!.Trim('"');

        // Reviewer and applicant discussing "the card photo" should be looking at files with the
        // same name, not two names for one file.
        named.Should().Be($"{applicationNumber}_{applicant.OrderNumber}_{label}.pdf");
    }

    [Fact]
    public async Task TheAdminPanelReportsTheNameToo()
    {
        var applicant = await RegisterApplicantAsync();
        var (application, fileId, label) = await UploadAsync(applicant);

        var applicationId = application.GetProperty("id").GetString();
        var applicationNumber = application.GetProperty("applicationNumber").GetString();

        var admin = CreateClient(await AdminTokenAsync());
        var response = await admin.GetAsync(Url($"/admin/applications/{applicationId}"));
        await EnsureSuccessAsync(response);

        var file = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("files").EnumerateArray()
            .First(f => f.GetProperty("id").GetGuid() == fileId);

        file.GetProperty("downloadName").GetString()
            .Should().Be($"{applicationNumber}_{applicant.OrderNumber}_{label}.pdf");
    }
}
