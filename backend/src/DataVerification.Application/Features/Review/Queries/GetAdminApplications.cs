using DataVerification.Application.Common.Exceptions;
using DataVerification.Domain.Common;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Common.Models;
using DataVerification.Application.Features.Applications;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Review.Queries;

/// <summary>The admin review queue, filterable by status, country, authority and date.</summary>
public sealed record GetAdminApplicationsQuery : PagedQuery, IRequest<PagedResult<AdminApplicationListItemDto>>
{
    public ApplicationStatus? Status { get; init; }

    public Guid? CountryId { get; init; }

    public Guid? VerificationAuthorityId { get; init; }

    public DateTime? CreatedFromUtc { get; init; }

    public DateTime? CreatedToUtc { get; init; }
}

public sealed class GetAdminApplicationsQueryHandler
    : IRequestHandler<GetAdminApplicationsQuery, PagedResult<AdminApplicationListItemDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetAdminApplicationsQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<AdminApplicationListItemDto>> Handle(
        GetAdminApplicationsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var language = _currentUser.LanguageCode;

        var query = _db.Applications.AsNoTracking().AsQueryable();

        if (request.Status is { } status)
        {
            query = query.Where(a => a.Status == status);
        }

        if (request.CountryId is { } countryId)
        {
            query = query.Where(a => a.Order!.VerificationCountryId == countryId);
        }

        if (request.VerificationAuthorityId is { } authorityId)
        {
            query = query.Where(a => a.VerificationAuthorityId == authorityId);
        }

        if (request.CreatedFromUtc is { } from)
        {
            query = query.Where(a => a.CreatedAtUtc >= from);
        }

        if (request.CreatedToUtc is { } to)
        {
            query = query.Where(a => a.CreatedAtUtc <= to);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(a =>
                EF.Functions.Like(a.ApplicationNumber, $"%{term}%")
                || EF.Functions.Like(a.AddressedTo, $"%{term}%")
                || EF.Functions.Like(a.Order!.OrderNumber, $"%{term}%")
                || EF.Functions.Like(a.Order!.Email, $"%{term}%"));
        }

        // Oldest first: the queue is worked front to back, so the longest-waiting applicant is
        // the one a reviewer sees at the top.
        query = query.OrderBy(a => a.CreatedAtUtc);

        // Projected in SQL rather than materialised through Includes. Loading every comment just
        // to count them made this page grow with total comment volume, not with page size; the
        // comment count is now a correlated subquery and only the columns shown are fetched.
        var projected = query.Select(a => new
        {
            a.Id,
            a.ApplicationNumber,
            a.AddressedTo,
            a.Status,
            a.PaidAtUtc,
            a.TotalCost,
            a.CreatedAtUtc,
            CurrencyCode = a.Order!.Currency!.Code,
            a.Order!.OrderNumber,
            a.Order!.Email,
            CountryNameAr = a.Order!.VerificationCountry!.NameAr,
            CountryNameEn = a.Order!.VerificationCountry!.NameEn,
            AuthorityNameAr = a.VerificationAuthority!.NameAr,
            AuthorityNameEn = a.VerificationAuthority!.NameEn,
            UnreadUserComments = a.Comments.Count(c => c.AuthorType == ActorType.Applicant),
        });

        var isArabic = language.StartsWith("ar", StringComparison.OrdinalIgnoreCase);

        return await projected.ToPagedResultAsync(
            request,
            row => new AdminApplicationListItemDto(
                row.Id,
                row.ApplicationNumber,
                row.AddressedTo,
                row.Status,
                row.Status.ToString(),
                row.PaidAtUtc.HasValue,
                row.TotalCost,
                row.CurrencyCode ?? string.Empty,
                row.OrderNumber ?? string.Empty,
                row.Email ?? string.Empty,
                (isArabic ? row.CountryNameAr : row.CountryNameEn) ?? string.Empty,
                (isArabic ? row.AuthorityNameAr : row.AuthorityNameEn) ?? string.Empty,
                row.CreatedAtUtc,
                row.PaidAtUtc,
                row.UnreadUserComments),
            cancellationToken);
    }
}

/// <summary>The full application as an admin sees it, including internal comments.</summary>
public sealed record GetAdminApplicationDetailsQuery(Guid ApplicationId)
    : IRequest<AdminApplicationDetailsDto>;

public sealed record AdminApplicationDetailsDto(
    Guid Id,
    string ApplicationNumber,
    string AddressedTo,
    DateOnly? BirthDate,
    ApplicationStatus Status,
    string StatusName,
    bool IsPaid,
    DateTime? PaidAtUtc,
    decimal TotalCost,
    string CurrencyCode,
    string OrderNumber,
    string OrderEmail,
    Guid OrderId,
    DateTime CreatedAtUtc,
    string TransactionTypeName,
    string SubTransactionTypeName,
    string AuthorityName,
    IReadOnlyList<AdminNameDto> Names,
    IReadOnlyList<AdminServiceDto> Services,
    IReadOnlyList<AdminFileDto> Files,
    IReadOnlyList<AdminDocumentDto> Documents,
    IReadOnlyList<CommentDto> Comments);

public sealed record AdminNameDto(
    NameLanguageType LanguageType,
    string FirstName,
    string? MiddleName,
    string LastName);

public sealed record AdminServiceDto(
    Guid Id,
    string ServiceName,
    int Quantity,
    string LanguageCode,
    bool IsExpress,
    decimal UnitCost,
    decimal ExpressCost,
    decimal LineTotal);

public sealed record AdminFileDto(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    ApplicationFileKind Kind,
    string? RequiredFileName,
    DateTime UploadedAtUtc,
    string DownloadUrl,
    /// <summary>
    /// What to call the file once it is saved: application, order and the document it satisfies.
    /// The panel fetches files as blobs and names them itself, so this is the name that actually
    /// reaches the disk — the API's own header carries the same one.
    /// </summary>
    string DownloadName);

/// <summary>
/// A document the review team attached, as an admin sees it — including the ones withheld from the
/// applicant, which is the difference between this and the applicant's own projection.
/// </summary>
public sealed record AdminDocumentDto(
    Guid Id,
    string Name,
    string NameAr,
    string NameEn,
    bool IsVisibleToApplicant,
    ApplicationStatus? AttachedAtStatus,
    string? UploadedByName,
    DateTime AttachedAtUtc,
    IReadOnlyList<AdminFileDto> Files,
    IReadOnlyList<AdminDocumentFieldDto> Fields);

public sealed record AdminDocumentFieldDto(
    Guid Id,
    string Name,
    string NameAr,
    string NameEn,
    RequiredFieldType FieldType,
    bool IsRequired,
    string? Value,
    string? DisplayValue);

public sealed class GetAdminApplicationDetailsQueryHandler
    : IRequestHandler<GetAdminApplicationDetailsQuery, AdminApplicationDetailsDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetAdminApplicationDetailsQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<AdminApplicationDetailsDto> Handle(
        GetAdminApplicationDetailsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var language = _currentUser.LanguageCode;

        // Split, not joined. Six collections in one result set — names, services, files,
        // documents, document fields, comments — multiply into a cartesian product whose memory
        // grant dwarfs the handful of rows actually wanted, and waiting for that grant is what
        // makes this the query that stalls when the server is busy.
        var application = await _db.Applications
            .AsNoTracking()
            .AsSplitQuery()
            .Include(a => a.Order).ThenInclude(o => o!.Currency)
            .Include(a => a.Names)
            .Include(a => a.Services).ThenInclude(s => s.ServiceType)
            .Include(a => a.Files).ThenInclude(f => f.RequiredFile)
            .Include(a => a.Documents).ThenInclude(d => d.Fields).ThenInclude(f => f.Options)
            .Include(a => a.Documents).ThenInclude(d => d.Files)
            .Include(a => a.Comments)
            .Include(a => a.TransactionType)
            .Include(a => a.SubTransactionType)
            .Include(a => a.VerificationAuthority)
            .FirstOrDefaultAsync(a => a.Id == request.ApplicationId, cancellationToken)
            ?? throw new NotFoundException("Application", request.ApplicationId);

        return new AdminApplicationDetailsDto(
            application.Id,
            application.ApplicationNumber,
            application.AddressedTo,
            application.BirthDate,
            application.Status,
            application.Status.ToString(),
            application.IsPaid,
            application.PaidAtUtc,
            application.TotalCost,
            application.Order?.Currency?.Code ?? string.Empty,
            application.Order?.OrderNumber ?? string.Empty,
            application.Order?.Email ?? string.Empty,
            application.OrderId,
            application.CreatedAtUtc,
            application.TransactionType?.ResolveName(language) ?? string.Empty,
            application.SubTransactionType?.ResolveName(language) ?? string.Empty,
            application.VerificationAuthority?.ResolveName(language) ?? string.Empty,
            application.Names
                .OrderBy(n => n.LanguageType)
                .Select(n => new AdminNameDto(n.LanguageType, n.FirstName, n.MiddleName, n.LastName))
                .ToList(),
            application.Services
                .Select(s => new AdminServiceDto(
                    s.Id,
                    s.ServiceType?.ResolveName(language) ?? string.Empty,
                    s.Quantity,
                    s.LanguageCode,
                    s.IsExpress,
                    s.UnitCost,
                    s.ExpressCost,
                    s.LineTotal))
                .ToList(),
            // Files that belong to an attached document are listed under it instead, so the tab
            // does not show the same upload twice.
            application.Files
                .Where(f => f.ApplicationDocumentId is null)
                .OrderBy(f => f.CreatedAtUtc)
                .Select(f => ToFileDto(f, application.Id, language, application.ApplicationNumber, application.Order?.OrderNumber))
                .ToList(),
            application.Documents
                .OrderBy(d => d.SortOrder)
                .Select(d => new AdminDocumentDto(
                    d.Id,
                    d.ResolveName(language),
                    d.NameAr,
                    d.NameEn,
                    d.IsVisibleToApplicant,
                    d.AttachedAtStatus,
                    d.UploadedByName,
                    d.CreatedAtUtc,
                    d.Files
                        .OrderBy(f => f.CreatedAtUtc)
                        .Select(f => ToFileDto(f, application.Id, language, application.ApplicationNumber, application.Order?.OrderNumber))
                        .ToList(),
                    d.Fields
                        .OrderBy(f => f.SortOrder)
                        .Select(f => new AdminDocumentFieldDto(
                            f.Id,
                            f.ResolveName(language),
                            f.NameAr,
                            f.NameEn,
                            f.FieldType,
                            f.IsRequired,
                            f.Value,
                            ApplicationDetailsProjection.ResolveDisplayValue(f, language)))
                        .ToList()))
                .ToList(),
            // Admins see the whole thread, internal notes included.
            application.Comments
                .OrderBy(c => c.CreatedAtUtc)
                .Select(c => new CommentDto(
                    c.Id,
                    c.AuthorType,
                    c.AuthorName,
                    c.Visibility,
                    c.Body,
                    c.CreatedAtUtc))
                .ToList());
    }

    private static AdminFileDto ToFileDto(
        ApplicationFile file,
        Guid applicationId,
        string? language,
        string? applicationNumber,
        string? orderNumber) =>
        new(
            file.Id,
            file.FileName,
            file.ContentType,
            file.SizeBytes,
            file.Kind,
            file.RequiredFile?.ResolveName(language),
            file.CreatedAtUtc,
            $"/api/v1/admin/applications/{applicationId}/files/{file.Id}",
            DownloadFileName.Compose(
                applicationNumber,
                orderNumber,
                file.RequiredFile?.ResolveName(language),
                file.FileName));
}

/// <summary>Streams any file on an application for an admin reviewing it.</summary>
public sealed record DownloadAdminFileQuery(Guid ApplicationId, Guid FileId)
    : IRequest<AdminFileDownload>;

public sealed record AdminFileDownload(string FileName, string ContentType, Stream Content);

public sealed class DownloadAdminFileQueryHandler
    : IRequestHandler<DownloadAdminFileQuery, AdminFileDownload>
{
    private readonly IApplicationDbContext _db;
    private readonly IFileStorage _storage;
    // The document's label is bilingual, so the name depends on who is downloading it.
    private readonly ICurrentUser _currentUser;

    public DownloadAdminFileQueryHandler(
        IApplicationDbContext db,
        IFileStorage storage,
        ICurrentUser currentUser)
    {
        _currentUser = currentUser;
        _db = db;
        _storage = storage;
    }

    public async Task<AdminFileDownload> Handle(
        DownloadAdminFileQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var file = await _db.ApplicationFiles
            .AsNoTracking()
            .Include(f => f.Application)
            .ThenInclude(a => a!.Order)
            .Include(f => f.RequiredFile)
            .FirstOrDefaultAsync(
                f => f.Id == request.FileId && f.ApplicationId == request.ApplicationId,
                cancellationToken)
            ?? throw new NotFoundException("ApplicationFile", request.FileId);

        var content = await _storage.OpenReadAsync(file.StoragePath, cancellationToken);

        var downloadName = DownloadFileName.Compose(
            file.Application?.ApplicationNumber,
            file.Application?.Order?.OrderNumber,
            file.RequiredFile?.ResolveName(_currentUser.LanguageCode),
            file.FileName);

        return new AdminFileDownload(downloadName, file.ContentType, content);
    }
}
