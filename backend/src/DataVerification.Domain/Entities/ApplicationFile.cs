using DataVerification.Domain.Common;
using DataVerification.Domain.Enums;

namespace DataVerification.Domain.Entities;

/// <summary>
/// A stored file — either evidence the applicant uploaded, or a deliverable an admin attached once
/// the verification succeeded. Only the metadata lives in SQL; bytes go through IFileStorage.
/// </summary>
public class ApplicationFile : Entity
{
    /// <summary>Content types the platform accepts. Enforced by magic-byte sniffing, not extension.</summary>
    public static readonly IReadOnlyDictionary<string, string> AllowedContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "application/pdf",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        };

    public const long MaxFileSizeBytes = 5 * 1024 * 1024;

    public Guid ApplicationId { get; set; }

    public VerificationApplication? Application { get; set; }

    /// <summary>Set when the file belongs to a specific purchased service line.</summary>
    public Guid? ApplicationServiceId { get; set; }

    public ApplicationService? ApplicationService { get; set; }

    /// <summary>Set when the file satisfies a specific required-file definition.</summary>
    public Guid? RequiredFileId { get; set; }

    public ServiceTypeRequiredFile? RequiredFile { get; set; }

    /// <summary>Set when the file is part of a document an administrator attached during review.</summary>
    public Guid? ApplicationDocumentId { get; set; }

    public ApplicationDocument? Document { get; set; }

    public required string FileName { get; set; }

    /// <summary>Provider-relative path; never exposed to clients, who download via a scoped endpoint.</summary>
    public required string StoragePath { get; set; }

    public required string ContentType { get; set; }

    public long SizeBytes { get; set; }

    public ApplicationFileKind Kind { get; set; } = ApplicationFileKind.UserUpload;

    public ActorType UploadedByType { get; set; }

    public Guid? UploadedById { get; set; }

    public string? UploadedByName { get; set; }

    public static bool IsAllowedExtension(string extension) =>
        AllowedContentTypes.ContainsKey(extension);
}
