using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;

using DataVerification.Application.Features.Lookups;

namespace DataVerification.Application.Features.Applications;

/// <summary>
/// Row shape for the applicant's applications table. The capability flags are computed from the
/// domain rules on the server, so the UI never has to re-derive when edit, delete or refund is
/// permitted — and cannot get it wrong.
/// </summary>
/// <param name="ApplicantNameAr">
/// Both scripts travel with the row rather than one resolved name, because the table's search
/// matches either — showing the English name for a hit that matched the Arabic one would look
/// like the search was broken.
/// </param>
/// <param name="ServiceTypeNames">
/// Distinct service names on this application, already localized. An application usually buys one
/// service, so this stays short.
/// </param>
public sealed record ApplicationListItemDto(
    Guid Id,
    string ApplicationNumber,
    string AddressedTo,
    ApplicationStatus Status,
    string StatusName,
    bool IsPaid,
    DateTime? PaidAtUtc,
    decimal TotalCost,
    string CurrencyCode,
    int ServiceCount,
    DateTime CreatedAtUtc,
    bool CanEdit,
    bool CanDelete,
    bool CanRefund,
    string? ApplicantNameAr,
    string? ApplicantNameEn,
    string? TransactionTypeName,
    string? SubTransactionTypeName,
    string? VerificationAuthorityName,
    IReadOnlyList<string> ServiceTypeNames);

/// <summary>The full aggregate as the applicant sees it — internal comments are never included.</summary>
public sealed record ApplicationDetailsDto(
    Guid Id,
    string ApplicationNumber,
    string AddressedTo,
    /// <summary>Null on a draft the applicant has not filled in yet.</summary>
    DateOnly? BirthDate,
    /// <summary>The applicant's own contact details, null until the personal step is filled in.</summary>
    string? ApplicantEmail,
    string? ApplicantPhoneCountry,
    string? ApplicantPhoneCode,
    string? ApplicantPhoneNumber,
    ApplicationStatus Status,
    string StatusName,
    bool IsPaid,
    DateTime? PaidAtUtc,
    decimal TotalCost,
    string CurrencyCode,
    DateTime CreatedAtUtc,
    bool CanEdit,
    bool CanDelete,
    bool CanRefund,
    LookupRefDto? TransactionType,
    LookupRefDto? SubTransactionType,
    LookupRefDto? VerificationAuthority,
    IReadOnlyList<ApplicationNameDto> Names,
    IReadOnlyList<ApplicationServiceDto> Services,
    IReadOnlyList<ApplicationFileDto> Files,
    IReadOnlyList<RequiredFileStatusDto> RequiredFiles,
    /// <summary>Documents the review team attached and chose to share. Never internal ones.</summary>
    IReadOnlyList<ApplicationDocumentDto> Documents);

public sealed record LookupRefDto(Guid Id, string Name);

/// <summary>
/// A document a reviewer attached, as the applicant sees it: the files to download and the details
/// recorded beside them. Whether it appears at all was decided in the query layer.
/// </summary>
public sealed record ApplicationDocumentDto(
    Guid Id,
    string Name,
    DateTime AttachedAtUtc,
    IReadOnlyList<ApplicationFileDto> Files,
    IReadOnlyList<DocumentFieldValueDto> Fields);

/// <summary>One recorded detail. <paramref name="DisplayValue"/> is what to show on screen.</summary>
public sealed record DocumentFieldValueDto(
    Guid Id,
    string Name,
    RequiredFieldType FieldType,
    string? Value,
    string? DisplayValue);

public sealed record ApplicationNameDto(
    NameLanguageType LanguageType,
    string FirstName,
    string? MiddleName,
    string LastName);

/// <summary>One row of the wizard's services summary table.</summary>
public sealed record ApplicationServiceDto(
    Guid Id,
    Guid ServiceTypeId,
    string ServiceName,
    string? Description,
    int ExecutionTimeDays,
    int Quantity,
    string LanguageCode,
    bool EnableExpress,
    bool IsExpress,
    decimal UnitCost,
    decimal ExpressCost,
    decimal LineTotal);

public sealed record ApplicationFileDto(
    Guid Id,
    Guid? ApplicationServiceId,
    Guid? RequiredFileId,
    /// <summary>The name as uploaded — what the applicant recognises in the list.</summary>
    string FileName,
    string ContentType,
    long SizeBytes,
    ApplicationFileKind Kind,
    DateTime UploadedAtUtc,
    /// <summary>
    /// What to call the file once it is saved: application, order and the document it satisfies.
    /// The front end fetches files as blobs and names them itself, so this is the name that
    /// actually reaches the disk — the API's own header carries the same one.
    /// </summary>
    string DownloadName);

/// <summary>
/// Tells the upload step which required files are still outstanding, so the UI can gate submission
/// on exactly the same condition the server enforces.
/// </summary>
public sealed record RequiredFileStatusDto(
    Guid RequiredFileId,
    Guid ApplicationServiceId,
    string Name,
    bool IsMandatory,
    bool IsSatisfied,
    /// <summary>Effective per-document upload cap in bytes.</summary>
    long MaxSizeBytes,
    int MaxFiles,
    int UploadedCount,
    IReadOnlyList<RequiredFileFieldDto> Fields,
    /// <summary>What the applicant has entered so far, keyed by field id.</summary>
    IReadOnlyDictionary<Guid, string> Values,
    /// <summary>False while a required custom field is still blank or invalid.</summary>
    bool AreFieldsComplete,
    /// <summary>
    /// The file extensions this document accepts, so the upload control offers and checks exactly
    /// what the server will take. Already resolved, so a document configured with no formats
    /// reports the platform default rather than an empty list.
    /// </summary>
    IReadOnlyList<string> AllowedExtensions);

internal static class ApplicationDtoMapper
{
    public static ApplicationListItemDto ToListItem(
        VerificationApplication application,
        string currencyCode,
        string? languageCode = null) => new(
        application.Id,
        application.ApplicationNumber,
        application.AddressedTo,
        application.Status,
        application.Status.ToString(),
        application.IsPaid,
        application.PaidAtUtc,
        application.TotalCost,
        currencyCode,
        application.Services.Count,
        application.CreatedAtUtc,
        application.CanEdit(),
        application.CanDelete(),
        application.CanRefund(),
        NameIn(application, NameLanguageType.Arabic),
        NameIn(application, NameLanguageType.English),
        application.TransactionType?.ResolveName(languageCode),
        application.SubTransactionType?.ResolveName(languageCode),
        application.VerificationAuthority?.ResolveName(languageCode),
        application.Services
            .Select(s => s.ServiceType?.ResolveName(languageCode))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct()
            .ToList());

    /// <summary>
    /// The applicant's name in one script, or null on a draft where it has not been entered yet.
    /// Callers that did not load <see cref="VerificationApplication.Names"/> get null too, which is
    /// correct for every screen that does not show it.
    /// </summary>
    private static string? NameIn(VerificationApplication application, NameLanguageType language) =>
        application.Names.FirstOrDefault(n => n.LanguageType == language)?.FullName;
}
