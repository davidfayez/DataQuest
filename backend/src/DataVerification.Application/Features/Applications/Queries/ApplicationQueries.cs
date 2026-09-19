using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Common.Models;
using DataVerification.Domain.Common;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Applications.Queries;

/// <summary>
/// The applicant's own applications, newest first by default.
/// </summary>
/// <remarks>
/// <see cref="PagedQuery.Search"/> matches the application number, the addressee, and the
/// applicant's name in either script — the three things someone actually remembers about a row.
/// Every filter is an AND, so narrowing by authority and status returns the intersection.
/// </remarks>
public sealed record GetMyApplicationsQuery : PagedQuery, IRequest<PagedResult<ApplicationListItemDto>>
{
    /// <summary>Optional status filter for the dashboard's tabs.</summary>
    public ApplicationStatus? Status { get; init; }

    public Guid? TransactionTypeId { get; init; }

    public Guid? SubTransactionTypeId { get; init; }

    public Guid? VerificationAuthorityId { get; init; }

    /// <summary>Matches applications that bought this service, whatever else they also bought.</summary>
    public Guid? ServiceTypeId { get; init; }

    /// <summary>One of <see cref="ApplicationSort.Fields"/>. Anything else falls back to the default.</summary>
    public string? SortBy { get; init; }

    public bool SortDescending { get; init; } = true;
}

/// <summary>
/// The sortable columns, named as the client sends them. A whitelist rather than reflection over
/// the entity: it keeps the sort key part of the API's contract, and stops a caller ordering by a
/// column the table has no index for.
/// </summary>
public static class ApplicationSort
{
    public const string ApplicationNumber = "applicationNumber";
    public const string AddressedTo = "addressedTo";
    public const string ApplicantName = "applicantName";
    public const string Status = "status";
    public const string TotalCost = "totalCost";
    public const string CreatedAt = "createdAt";
    public const string TransactionType = "transactionType";
    public const string SubTransactionType = "subTransactionType";
    public const string Authority = "authority";
    public const string ServiceCount = "serviceCount";
    public const string IsPaid = "isPaid";

    /// <summary>
    /// The purchased services are deliberately absent: an application may hold several, so
    /// "sort by service" has no single value to order on.
    /// </summary>
    public static readonly IReadOnlyList<string> Fields =
    [
        ApplicationNumber, AddressedTo, ApplicantName, Status, TotalCost, CreatedAt,
        TransactionType, SubTransactionType, Authority, ServiceCount, IsPaid,
    ];
}

/// <summary>
/// The filter options the applicant's own applications actually contain, with a count each.
/// Building the dropdowns from the data rather than from the full cascade means no option ever
/// returns an empty grid, and the applicant never has to pick a transaction type just to reach
/// the authority list.
/// </summary>
public sealed record GetMyApplicationFiltersQuery : IRequest<ApplicationFiltersDto>;

public sealed record ApplicationFilterOptionDto(Guid Id, string Name, int Count);

public sealed record ApplicationStatusOptionDto(ApplicationStatus Status, string StatusName, int Count);

public sealed record ApplicationFiltersDto(
    IReadOnlyList<ApplicationStatusOptionDto> Statuses,
    IReadOnlyList<ApplicationFilterOptionDto> TransactionTypes,
    IReadOnlyList<ApplicationFilterOptionDto> SubTransactionTypes,
    IReadOnlyList<ApplicationFilterOptionDto> VerificationAuthorities,
    IReadOnlyList<ApplicationFilterOptionDto> ServiceTypes);

public sealed record GetApplicationDetailsQuery(Guid ApplicationId) : IRequest<ApplicationDetailsDto>;

/// <summary>Metadata plus a stream for a file the caller is entitled to download.</summary>
public sealed record DownloadApplicationFileQuery(Guid ApplicationId, Guid FileId)
    : IRequest<ApplicationFileDownload>;

public sealed record ApplicationFileDownload(string FileName, string ContentType, Stream Content);

public sealed class ApplicationQueryHandlers :
    IRequestHandler<GetMyApplicationsQuery, PagedResult<ApplicationListItemDto>>,
    IRequestHandler<GetMyApplicationFiltersQuery, ApplicationFiltersDto>,
    IRequestHandler<GetApplicationDetailsQuery, ApplicationDetailsDto>,
    IRequestHandler<DownloadApplicationFileQuery, ApplicationFileDownload>
{
    private readonly IApplicationDbContext _db;
    private readonly ApplicationWriteService _writeService;
    private readonly IFileStorage _storage;
    private readonly ICurrentUser _currentUser;

    public ApplicationQueryHandlers(
        IApplicationDbContext db,
        ApplicationWriteService writeService,
        IFileStorage storage,
        ICurrentUser currentUser)
    {
        _db = db;
        _writeService = writeService;
        _storage = storage;
        _currentUser = currentUser;
    }

    private Guid RequireOrderId() => _currentUser.OrderId
        ?? throw new ForbiddenAccessException("This endpoint is only available to applicants.");

    public async Task<PagedResult<ApplicationListItemDto>> Handle(
        GetMyApplicationsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var orderId = RequireOrderId();

        var currencyCode = await _db.Orders
            .AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(o => o.Currency!.Code)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        // Scoped to the caller's order — this is what stops one order reading another's data.
        // Split: names and services joined into one result set multiply each other per row, the
        // same shape that made the service-types list wait on SQL Server for a memory grant.
        var query = _db.Applications
            .AsNoTracking()
            .AsSplitQuery()
            .Include(a => a.Names)
            .Include(a => a.Services)
            .ThenInclude(s => s.ServiceType)
            .Include(a => a.TransactionType)
            .Include(a => a.SubTransactionType)
            .Include(a => a.VerificationAuthority)
            .Include(a => a.Currency)
            .Where(a => a.OrderId == orderId);

        var isArabic = _currentUser.LanguageCode
            .StartsWith("ar", StringComparison.OrdinalIgnoreCase);

        query = Filter(query, request);
        query = Sort(query, request, isArabic);

        return await query.ToPagedResultAsync(
            request,
            // Its own currency; the order's main one for rows from before that was recorded.
            application => ApplicationDtoMapper.ToListItem(
                application,
                application.Currency?.Code ?? currencyCode,
                _currentUser.LanguageCode),
            cancellationToken);
    }

    /// <summary>
    /// Shared by the grid and by the filter-option counts, so an option can never offer a
    /// combination the grid would then show as empty.
    /// </summary>
    private static IQueryable<VerificationApplication> Filter(
        IQueryable<VerificationApplication> query,
        GetMyApplicationsQuery request)
    {
        if (request.Status is { } status)
        {
            query = query.Where(a => a.Status == status);
        }

        if (request.TransactionTypeId is { } transactionTypeId)
        {
            query = query.Where(a => a.TransactionTypeId == transactionTypeId);
        }

        if (request.SubTransactionTypeId is { } subTransactionTypeId)
        {
            query = query.Where(a => a.SubTransactionTypeId == subTransactionTypeId);
        }

        if (request.VerificationAuthorityId is { } authorityId)
        {
            query = query.Where(a => a.VerificationAuthorityId == authorityId);
        }

        if (request.ServiceTypeId is { } serviceTypeId)
        {
            query = query.Where(a => a.Services.Any(s => s.ServiceTypeId == serviceTypeId));
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();

            // Name parts are matched individually rather than as a concatenated full name: the
            // database can use the column directly, and "Layla Hassan" typed with a different
            // middle name still finds the row.
            query = query.Where(a =>
                a.ApplicationNumber.Contains(term)
                || a.AddressedTo.Contains(term)
                || a.Names.Any(n =>
                    n.FirstName.Contains(term)
                    || n.LastName.Contains(term)
                    || (n.MiddleName != null && n.MiddleName.Contains(term))));
        }

        return query;
    }

    private static IQueryable<VerificationApplication> Sort(
        IQueryable<VerificationApplication> query,
        GetMyApplicationsQuery request,
        bool isArabic)
    {
        var descending = request.SortDescending;

        return request.SortBy switch
        {
            ApplicationSort.ApplicationNumber => Order(query, a => a.ApplicationNumber, descending),
            ApplicationSort.AddressedTo => Order(query, a => a.AddressedTo, descending),

            // Sorted on the English name because it is the one every row is guaranteed to carry in
            // a single script; the grid still displays whichever script the locale calls for.
            ApplicationSort.ApplicantName => Order(
                query,
                a => a.Names
                    .Where(n => n.LanguageType == NameLanguageType.English)
                    .Select(n => n.FirstName)
                    .FirstOrDefault(),
                descending),

            ApplicationSort.Status => Order(query, a => a.Status, descending),
            ApplicationSort.TotalCost => Order(query, a => a.TotalCost, descending),

            // Lookups sort on the name actually being displayed, so the order on screen matches
            // the alphabet the reader is using rather than always following English.
            ApplicationSort.TransactionType => Order(
                query,
                a => isArabic ? a.TransactionType!.NameAr : a.TransactionType!.NameEn,
                descending),

            ApplicationSort.SubTransactionType => Order(
                query,
                a => isArabic ? a.SubTransactionType!.NameAr : a.SubTransactionType!.NameEn,
                descending),

            ApplicationSort.Authority => Order(
                query,
                a => isArabic ? a.VerificationAuthority!.NameAr : a.VerificationAuthority!.NameEn,
                descending),

            ApplicationSort.ServiceCount => Order(query, a => a.Services.Count, descending),

            // PaidAtUtc rather than the boolean: among paid rows, most-recently-paid first is the
            // useful order, and unpaid rows still group together because theirs is null.
            ApplicationSort.IsPaid => Order(query, a => a.PaidAtUtc, descending),

            _ => Order(query, a => a.CreatedAtUtc, descending),
        };
    }

    private static IQueryable<VerificationApplication> Order<TKey>(
        IQueryable<VerificationApplication> query,
        System.Linq.Expressions.Expression<Func<VerificationApplication, TKey>> key,
        bool descending) =>
        descending ? query.OrderByDescending(key) : query.OrderBy(key);

    public async Task<ApplicationFiltersDto> Handle(
        GetMyApplicationFiltersQuery request,
        CancellationToken cancellationToken)
    {
        var orderId = RequireOrderId();
        var language = _currentUser.LanguageCode;

        var applications = await _db.Applications
            .AsNoTracking()
            .Where(a => a.OrderId == orderId)
            .Select(a => new
            {
                a.Status,
                a.TransactionType,
                a.SubTransactionType,
                a.VerificationAuthority,
                ServiceTypes = a.Services.Select(s => s.ServiceType).ToList(),
            })
            .ToListAsync(cancellationToken);

        var statuses = applications
            .GroupBy(a => a.Status)
            .Select(group => new ApplicationStatusOptionDto(
                group.Key,
                group.Key.ToString(),
                group.Count()))
            .OrderBy(option => option.Status)
            .ToList();

        return new ApplicationFiltersDto(
            statuses,
            Options(applications.Select(a => a.TransactionType), language),
            Options(applications.Select(a => a.SubTransactionType), language),
            Options(applications.Select(a => a.VerificationAuthority), language),

            // A service appears once per application that bought it, however many lines it has —
            // otherwise a quantity of three would look like three matching applications.
            Options(applications.SelectMany(a => a.ServiceTypes.DistinctBy(s => s?.Id)), language));
    }

    private static List<ApplicationFilterOptionDto> Options(
        IEnumerable<LocalizedLookup?> lookups,
        string language) =>
        lookups
            .Where(lookup => lookup is not null)
            .GroupBy(lookup => lookup!.Id)
            .Select(group => new ApplicationFilterOptionDto(
                group.Key,
                group.First()!.ResolveName(language),
                group.Count()))
            .OrderBy(option => option.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public Task<ApplicationDetailsDto> Handle(
        GetApplicationDetailsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ApplicationDetailsProjection.LoadAsync(
            _db,
            _writeService,
            request.ApplicationId,
            RequireOrderId(),
            _currentUser.LanguageCode,
            cancellationToken);
    }

    public async Task<ApplicationFileDownload> Handle(
        DownloadApplicationFileQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var orderId = RequireOrderId();

        // Both the file and its application are matched against the caller's order, so a guessed
        // file id belonging to someone else resolves to nothing. A file inside a document the
        // reviewer withheld is filtered out here too — it is on the applicant's own application, so
        // order scoping alone would happily serve it, and a 404 keeps its existence unconfirmed.
        var file = await _db.ApplicationFiles
            .AsNoTracking()
            .Include(f => f.Application)
            .ThenInclude(a => a!.Order)
            .Include(f => f.RequiredFile)
            .FirstOrDefaultAsync(
                f => f.Id == request.FileId
                     && f.ApplicationId == request.ApplicationId
                     && f.Application!.OrderId == orderId
                     && (f.ApplicationDocumentId == null || f.Document!.IsVisibleToApplicant),
                cancellationToken)
            ?? throw new NotFoundException("ApplicationFile", request.FileId);

        var content = await _storage.OpenReadAsync(file.StoragePath, cancellationToken);

        // Named for where it came from rather than whatever it was called on the uploader's own
        // computer, so it still means something in a downloads folder.
        var downloadName = DownloadFileName.Compose(
            file.Application?.ApplicationNumber,
            file.Application?.Order?.OrderNumber,
            file.RequiredFile?.ResolveName(_currentUser.LanguageCode),
            file.FileName);

        return new ApplicationFileDownload(downloadName, file.ContentType, content);
    }
}
