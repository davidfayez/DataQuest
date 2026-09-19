using DataVerification.Application.Common.Exceptions;
using DataVerification.Domain.Common;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Applications;

/// <summary>
/// Builds the applicant-facing application detail. Kept in one place because create, update,
/// submit and the details query all return the same shape, and because the visibility rule for
/// comments must not be re-implemented (and mis-implemented) per call site.
/// </summary>
internal static class ApplicationDetailsProjection
{
    public static async Task<ApplicationDetailsDto> LoadAsync(
        IApplicationDbContext db,
        ApplicationWriteService writeService,
        Guid applicationId,
        Guid orderId,
        string languageCode,
        CancellationToken cancellationToken)
    {
        // Split, not joined. Nine Includes over five collections is a cartesian product in one
        // result set: names x services x files x documents x document fields, every row repeating
        // every other. It costs a memory grant out of proportion to the handful of rows actually
        // wanted, and on a busy server that grant is what it ends up waiting for.
        var application = await db.Applications
            .AsNoTracking()
            .AsSplitQuery()
            .Include(a => a.Names)
            .Include(a => a.Services).ThenInclude(s => s.ServiceType)
            .Include(a => a.Files).ThenInclude(f => f.RequiredFile)
            .Include(a => a.Documents.Where(d => d.IsVisibleToApplicant))
                .ThenInclude(d => d.Fields).ThenInclude(f => f.Options)
            .Include(a => a.Documents.Where(d => d.IsVisibleToApplicant))
                .ThenInclude(d => d.Files)
            .Include(a => a.TransactionType)
            .Include(a => a.SubTransactionType)
            .Include(a => a.VerificationAuthority)
            .Include(a => a.Order).ThenInclude(o => o!.Currency)
            .Include(a => a.Currency)
            .FirstOrDefaultAsync(a => a.Id == applicationId && a.OrderId == orderId, cancellationToken)
            ?? throw new NotFoundException("Application", applicationId);

        var requiredFiles = await writeService.GetRequiredFileStatusAsync(
            application,
            languageCode,
            cancellationToken);

        return new ApplicationDetailsDto(
            application.Id,
            application.ApplicationNumber,
            application.AddressedTo,
            application.BirthDate,
            application.ApplicantEmail,
            application.ApplicantPhoneCountry,
            application.ApplicantPhoneCode,
            application.ApplicantPhoneNumber,
            application.Status,
            application.Status.ToString(),
            application.IsPaid,
            application.PaidAtUtc,
            application.TotalCost,
            // Its own currency; the order's main one for rows from before that was recorded.
            application.Currency?.Code ?? application.Order?.Currency?.Code ?? string.Empty,
            application.CreatedAtUtc,
            application.CanEdit(),
            application.CanDelete(),
            application.CanRefund(),
            // A step the applicant has not reached yet has no reference to report.
            application.TransactionTypeId is { } transactionTypeId
                ? new LookupRefDto(
                    transactionTypeId,
                    application.TransactionType?.ResolveName(languageCode) ?? string.Empty)
                : null,
            application.SubTransactionTypeId is { } subTransactionTypeId
                ? new LookupRefDto(
                    subTransactionTypeId,
                    application.SubTransactionType?.ResolveName(languageCode) ?? string.Empty)
                : null,
            application.VerificationAuthorityId is { } authorityId
                ? new LookupRefDto(
                    authorityId,
                    application.VerificationAuthority?.ResolveName(languageCode) ?? string.Empty)
                : null,
            application.Names
                .OrderBy(n => n.LanguageType)
                .Select(n => new ApplicationNameDto(n.LanguageType, n.FirstName, n.MiddleName, n.LastName))
                .ToList(),
            application.Services
                .Select(s => new ApplicationServiceDto(
                    s.Id,
                    s.ServiceTypeId,
                    s.ServiceType?.ResolveName(languageCode) ?? string.Empty,
                    s.ServiceType?.ResolveApplicantDescription(languageCode),
                    s.ServiceType?.ExecutionTimeDays ?? 0,
                    s.Quantity,
                    s.LanguageCode,
                    s.ServiceType?.EnableExpress ?? false,
                    s.IsExpress,
                    s.UnitCost,
                    s.ExpressCost,
                    s.LineTotal))
                .ToList(),
            // Admin result files are included so the applicant can download deliverables; internal
            // comments are simply never part of this projection. Files belonging to an attached
            // document are reported under that document instead, so nothing is listed twice.
            application.Files
                .Where(f => f.ApplicationDocumentId is null)
                .OrderBy(f => f.CreatedAtUtc)
                .Select(f => new ApplicationFileDto(
                    f.Id,
                    f.ApplicationServiceId,
                    f.RequiredFileId,
                    f.FileName,
                    f.ContentType,
                    f.SizeBytes,
                    f.Kind,
                    f.CreatedAtUtc,
                    DownloadFileName.Compose(
                        application.ApplicationNumber,
                        application.Order?.OrderNumber,
                        f.RequiredFile?.ResolveName(languageCode),
                        f.FileName)))
                .ToList(),
            requiredFiles,
            // Only the documents the reviewer marked visible reach the applicant, and the filter is
            // applied in the query above rather than here — an internal document is never loaded.
            application.Documents
                .OrderBy(d => d.SortOrder)
                .Select(d => new ApplicationDocumentDto(
                    d.Id,
                    d.ResolveName(languageCode),
                    d.CreatedAtUtc,
                    d.Files
                        .OrderBy(f => f.CreatedAtUtc)
                        .Select(f => new ApplicationFileDto(
                            f.Id,
                            f.ApplicationServiceId,
                            f.RequiredFileId,
                            f.FileName,
                            f.ContentType,
                            f.SizeBytes,
                            f.Kind,
                            f.CreatedAtUtc,
                            DownloadFileName.Compose(
                                application.ApplicationNumber,
                                application.Order?.OrderNumber,
                                d.ResolveName(languageCode),
                                f.FileName)))
                        .ToList(),
                    d.Fields
                        .OrderBy(f => f.SortOrder)
                        .Select(f => new DocumentFieldValueDto(
                            f.Id,
                            f.ResolveName(languageCode),
                            f.FieldType,
                            f.Value,
                            ResolveDisplayValue(f, languageCode)))
                        .ToList()))
                .ToList());
    }

    /// <summary>
    /// What to show for a field's answer. A dropdown stores the option's stable value, which is
    /// meaningless on screen, so its label is resolved for the reader's language; every other type
    /// shows what was typed.
    /// </summary>
    internal static string? ResolveDisplayValue(ApplicationDocumentField field, string? languageCode)
    {
        if (field.Value is null || field.FieldType != RequiredFieldType.Dropdown)
        {
            return field.Value;
        }

        var option = field.Options.FirstOrDefault(
            o => string.Equals(o.Value, field.Value, StringComparison.OrdinalIgnoreCase));

        return option?.ResolveLabel(languageCode) ?? field.Value;
    }

    /// <summary>
    /// True when every mandatory required-file definition has a matching upload. Submission is
    /// gated on this so an application cannot enter the payment queue half-documented.
    /// </summary>
    public static bool AllMandatoryFilesPresent(IReadOnlyList<RequiredFileStatusDto> statuses) =>
        statuses.Where(s => s.IsMandatory).All(s => s.IsSatisfied);
}
