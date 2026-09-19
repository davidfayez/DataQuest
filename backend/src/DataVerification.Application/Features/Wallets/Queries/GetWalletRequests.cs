using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Common.Models;
using DataVerification.Application.Features.Payments;
using DataVerification.Domain.Common;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Wallets.Queries;

/// <summary>The caller's own deposit and withdrawal requests, newest first.</summary>
public sealed record GetMyWalletRequestsQuery : PagedQuery, IRequest<PagedResult<WalletRequestDto>>
{
    public WalletRequestStatus? Status { get; init; }

    public WalletRequestType? Type { get; init; }
}

/// <summary>
/// One of the applicant's own requests, for its details page.
/// </summary>
/// <remarks>
/// Scoped to their order in the query rather than checked afterwards, and answered with 404 when
/// it belongs to somebody else — an applicant has no business learning that another order's
/// request exists.
/// </remarks>
public sealed record GetMyWalletRequestQuery(Guid RequestId) : IRequest<WalletRequestDto>;

/// <summary>
/// One request for the back office, by id — what its own page reads before showing the trail.
/// </summary>
/// <remarks>
/// Unscoped, unlike <see cref="GetMyWalletRequestQuery"/>: a reviewer works the whole queue, and
/// the endpoint already sits behind <c>Orders.View</c>.
/// </remarks>
public sealed record GetWalletRequestQuery(Guid RequestId) : IRequest<WalletRequestDto>;

/// <summary>
/// The back-office queue. Defaults to everything; the admin page narrows it to Pending to work
/// through outstanding decisions. <see cref="PagedQuery.Search"/> matches the order number.
/// </summary>
public sealed record GetWalletRequestsQuery : PagedQuery, IRequest<PagedResult<WalletRequestDto>>
{
    public WalletRequestStatus? Status { get; init; }

    public WalletRequestType? Type { get; init; }

    /// <summary>Set when drilling in from a single order rather than browsing the whole queue.</summary>
    public Guid? OrderId { get; init; }
}

public sealed class WalletRequestQueryHandlers :
    IRequestHandler<GetMyWalletRequestsQuery, PagedResult<WalletRequestDto>>,
    IRequestHandler<GetMyWalletRequestQuery, WalletRequestDto>,
    IRequestHandler<GetWalletRequestQuery, WalletRequestDto>,
    IRequestHandler<GetWalletRequestsQuery, PagedResult<WalletRequestDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public WalletRequestQueryHandlers(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<WalletRequestDto> Handle(
        GetMyWalletRequestQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var orderId = _currentUser.OrderId
            ?? throw new ForbiddenAccessException("This endpoint is only available to applicants.");

        var row = await Project(_db.WalletRequests
                .AsNoTracking()
                .Where(r => r.Id == request.RequestId && r.OrderId == orderId))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(WalletRequest), request.RequestId);

        var files = await LoadFilesAsync([row.Id], cancellationToken);
        var values = await LoadValuesAsync(row.Id, cancellationToken);

        return row.ToDto(
            _currentUser.LanguageCode,
            files.TryGetValue(row.Id, out var attached) ? attached : [],
            values);
    }

    public Task<PagedResult<WalletRequestDto>> Handle(
        GetMyWalletRequestsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var orderId = _currentUser.OrderId
            ?? throw new ForbiddenAccessException("This endpoint is only available to applicants.");

        var query = _db.WalletRequests.AsNoTracking().Where(r => r.OrderId == orderId);

        return LoadAsync(query, request, request.Status, request.Type, null, cancellationToken);
    }

    public async Task<WalletRequestDto> Handle(
        GetWalletRequestQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var row = await Project(_db.WalletRequests
                .AsNoTracking()
                .Where(r => r.Id == request.RequestId))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(WalletRequest), request.RequestId);

        var files = await LoadFilesAsync([row.Id], cancellationToken);
        var values = await LoadValuesAsync(row.Id, cancellationToken);

        return row.ToDto(
            _currentUser.LanguageCode,
            files.TryGetValue(row.Id, out var attached) ? attached : [],
            values);
    }

    public Task<PagedResult<WalletRequestDto>> Handle(
        GetWalletRequestsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = _db.WalletRequests.AsNoTracking();

        if (request.OrderId is { } orderId)
        {
            query = query.Where(r => r.OrderId == orderId);
        }

        return LoadAsync(query, request, request.Status, request.Type, request.Search, cancellationToken);
    }

    /// <summary>
    /// Every column the DTO needs and nothing else.
    /// </summary>
    /// <remarks>
    /// Deliberately not <c>Include</c> + entity: see <see cref="WalletRequestListRow"/> for what
    /// loading the whole graph cost.
    /// </remarks>
    private static IQueryable<WalletRequestListRow> Project(IQueryable<WalletRequest> query) =>
        query.Select(r => new WalletRequestListRow(
            r.Id,
            r.OrderId,
            r.Order!.OrderNumber,
            r.Type,
            r.Status,
            r.Amount,
            r.Wallet!.Currency!.Code,
            r.ApplicantNote,
            r.ReviewerNote,
            r.RequestedByName,
            r.ReviewedByName,
            r.ReviewedAtUtc,
            r.CreatedAtUtc,
            r.PaymentMethodId,
            r.PaymentMethod!.NameAr,
            r.PaymentMethod!.NameEn,
            r.PaymentMethod!.Type!.NameAr,
            r.PaymentMethod!.Type!.NameEn,
            (PaymentMethodKind?)r.PaymentMethod!.Type!.Kind,
            r.PaymentMethodAccountId,
            r.PaymentMethodAccount!.LabelAr,
            r.PaymentMethodAccount!.LabelEn,
            r.PaymentMethodAccount!.AccountNumber,
            r.ReferenceNumber,
            r.ConfirmedAmount,
            r.ConfirmedReference));

    /// <summary>The receipts for a set of requests, keyed by request.</summary>
    /// <remarks>
    /// A query of its own rather than a sub-collection on the projection: joining the files back in
    /// is what forces the ORDER BY that stitches them, and that sort is what waits on memory.
    /// Ordering the handful of rows in memory costs nothing and asks the server for nothing.
    /// </remarks>
    private async Task<Dictionary<Guid, IReadOnlyList<WalletRequestFileDto>>> LoadFilesAsync(
        IReadOnlyList<Guid> requestIds,
        CancellationToken cancellationToken)
    {
        var files = await _db.WalletRequestFiles
            .AsNoTracking()
            .Where(file => requestIds.Contains(file.WalletRequestId))
            .Select(file => new
            {
                file.WalletRequestId,
                file.Id,
                file.FileName,
                file.ContentType,
                file.SizeBytes,
                file.CreatedAtUtc,
                file.RequiredFileId,
                file.DocumentNameAr,
                file.DocumentNameEn,
            })
            .ToListAsync(cancellationToken);

        var language = _currentUser.LanguageCode;

        return files
            .GroupBy(file => file.WalletRequestId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<WalletRequestFileDto>)group
                    .OrderBy(file => file.CreatedAtUtc)
                    .Select(file => new WalletRequestFileDto(
                        file.Id,
                        file.FileName,
                        file.ContentType,
                        file.SizeBytes,
                        file.CreatedAtUtc,
                        file.RequiredFileId,
                        file.RequiredFileId is null
                            ? null
                            : LocalizedText.Resolve(file.DocumentNameAr, file.DocumentNameEn, language)))
                    .ToList());
    }

    /// <summary>
    /// The details filled in beside a request's documents, in the order they were shown. Only a
    /// single request's page asks for them; the queues have no room to show them.
    /// </summary>
    private async Task<IReadOnlyList<WalletRequestDocumentValueDto>> LoadValuesAsync(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        var values = await _db.WalletRequestDocumentValues
            .AsNoTracking()
            .Where(value => value.WalletRequestId == requestId)
            .ToListAsync(cancellationToken);

        return values
            .OrderBy(value => value.SortOrder)
            .Select(value => WalletRequestDocumentValueDto.From(value, _currentUser.LanguageCode))
            .ToList();
    }

    private async Task<PagedResult<WalletRequestDto>> LoadAsync(
        IQueryable<WalletRequest> query,
        PagedQuery paging,
        WalletRequestStatus? status,
        WalletRequestType? type,
        string? search,
        CancellationToken cancellationToken)
    {
        if (status is { } wanted)
        {
            query = query.Where(r => r.Status == wanted);
        }

        if (type is { } kind)
        {
            query = query.Where(r => r.Type == kind);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(r => r.Order!.OrderNumber.Contains(term) || r.Order!.Email.Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        // Paged on the keys alone, then loaded by key. Ordering the whole request and paging that
        // put `SELECT [w].*` inside the OFFSET/FETCH subquery, and the sort that followed carried
        // every column of the joined row; see WalletRequestListRow for what it cost. Sorting bare
        // keys asks the server for almost no memory, and the second query has no sort at all.
        //
        // Pending first regardless of the sort, so a queue left unfiltered still leads with work.
        // An explicit sort takes precedence over the pending-first default: a reader who clicked a
        // column heading asked for that order and nothing else.
        var ids = await query
            .OrderBy(r => r.Status == WalletRequestStatus.Pending ? 0 : 1)
            .ThenByDescending(r => r.CreatedAtUtc)
            .ApplySort(paging)
            .Select(r => r.Id)
            .Skip((paging.Page - 1) * paging.PageSize)
            .Take(paging.PageSize)
            .ToListAsync(cancellationToken);

        if (ids.Count == 0)
        {
            return new PagedResult<WalletRequestDto>([], paging.Page, paging.PageSize, totalCount);
        }

        var rows = await Project(_db.WalletRequests.AsNoTracking().Where(r => ids.Contains(r.Id)))
            .ToDictionaryAsync(row => row.Id, cancellationToken);

        var files = await LoadFilesAsync(ids, cancellationToken);
        var language = _currentUser.LanguageCode;

        // Back into the order the keys came in; neither query was asked to sort.
        return new PagedResult<WalletRequestDto>(
            ids
                .Where(rows.ContainsKey)
                .Select(id => rows[id].ToDto(
                    language,
                    files.TryGetValue(id, out var attached) ? attached : []))
                .ToList(),
            paging.Page,
            paging.PageSize,
            totalCount);
    }
}
