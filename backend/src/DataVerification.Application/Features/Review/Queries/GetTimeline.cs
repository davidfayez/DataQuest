using DataVerification.Application.Common.Exceptions;
using DataVerification.Domain.Common;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Review.Queries;

/// <summary>
/// The merged activity feed for one application. Serves both realms: an applicant caller is
/// scoped to their own order and never sees internal comments, while an admin caller sees
/// everything.
/// </summary>
public sealed record GetApplicationTimelineQuery(Guid ApplicationId)
    : IRequest<IReadOnlyList<TimelineEntryDto>>;

public sealed class GetApplicationTimelineQueryHandler
    : IRequestHandler<GetApplicationTimelineQuery, IReadOnlyList<TimelineEntryDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetApplicationTimelineQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<TimelineEntryDto>> Handle(
        GetApplicationTimelineQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var isAdmin = _currentUser.AdminUserId.HasValue;
        var orderId = _currentUser.OrderId;

        if (!isAdmin && orderId is null)
        {
            throw new ForbiddenAccessException("This endpoint requires an applicant or admin token.");
        }

        var applicationExists = await _db.Applications
            .AsNoTracking()
            .AnyAsync(
                a => a.Id == request.ApplicationId && (isAdmin || a.OrderId == orderId),
                cancellationToken);

        if (!applicationExists)
        {
            throw new NotFoundException("Application", request.ApplicationId);
        }

        // The visibility filter is applied here, in the query, not when shaping the response.
        // An internal comment therefore never leaves the database on an applicant request.
        var commentsQuery = _db.ApplicationComments
            .AsNoTracking()
            .Where(c => c.ApplicationId == request.ApplicationId);

        if (!isAdmin)
        {
            commentsQuery = commentsQuery.Where(c => c.Visibility == CommentVisibility.ForUser);
        }

        var comments = await commentsQuery.ToListAsync(cancellationToken);

        var statusChanges = await _db.ApplicationStatusHistory
            .AsNoTracking()
            .Where(h => h.ApplicationId == request.ApplicationId)
            .ToListAsync(cancellationToken);

        var files = await _db.ApplicationFiles
            .AsNoTracking()
            .Where(f => f.ApplicationId == request.ApplicationId)
            .ToListAsync(cancellationToken);

        var entries = new List<TimelineEntryDto>(comments.Count + statusChanges.Count + files.Count);

        entries.AddRange(comments.Select(c => new TimelineEntryDto(
            c.Id,
            TimelineEntryKind.Comment,
            nameof(TimelineEntryKind.Comment),
            c.AuthorType,
            c.AuthorName,
            c.CreatedAtUtc,
            c.Body,
            c.Visibility,
            null,
            null,
            null,
            null)));

        entries.AddRange(statusChanges.Select(h => new TimelineEntryDto(
            h.Id,
            TimelineEntryKind.StatusChange,
            nameof(TimelineEntryKind.StatusChange),
            h.ChangedByType,
            h.ChangedByName,
            h.CreatedAtUtc,
            h.Note,
            null,
            h.FromStatus,
            h.ToStatus,
            null,
            null)));

        entries.AddRange(files.Select(f =>
        {
            var kind = f.Kind == ApplicationFileKind.AdminResult
                ? TimelineEntryKind.ResultAttached
                : TimelineEntryKind.FileUploaded;

            return new TimelineEntryDto(
                f.Id,
                kind,
                kind.ToString(),
                f.UploadedByType,
                f.UploadedByName,
                f.CreatedAtUtc,
                null,
                null,
                null,
                null,
                f.FileName,
                f.Id);
        }));

        return entries.OrderBy(e => e.CreatedAtUtc).ThenBy(e => e.Kind).ToList();
    }
}

/// <summary>The verified deliverables, listed for the applicant once the application succeeded.</summary>
public sealed record GetApplicationResultsQuery(Guid ApplicationId)
    : IRequest<IReadOnlyList<ResultFileDto>>;

public sealed class GetApplicationResultsQueryHandler
    : IRequestHandler<GetApplicationResultsQuery, IReadOnlyList<ResultFileDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetApplicationResultsQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<ResultFileDto>> Handle(
        GetApplicationResultsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var isAdmin = _currentUser.AdminUserId.HasValue;
        var orderId = _currentUser.OrderId;

        if (!isAdmin && orderId is null)
        {
            throw new ForbiddenAccessException("This endpoint requires an applicant or admin token.");
        }

        var applicationExists = await _db.Applications
            .AsNoTracking()
            .AnyAsync(
                a => a.Id == request.ApplicationId && (isAdmin || a.OrderId == orderId),
                cancellationToken);

        if (!applicationExists)
        {
            throw new NotFoundException("Application", request.ApplicationId);
        }

        var files = await _db.ApplicationFiles
            .AsNoTracking()
            .Include(f => f.Application)
            .ThenInclude(a => a!.Order)
            .Where(f => f.ApplicationId == request.ApplicationId
                        && f.Kind == ApplicationFileKind.AdminResult)
            .OrderBy(f => f.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        // The URL points at the order-scoped download endpoint; the storage path is never exposed.
        return files
            .Select(f => new ResultFileDto(
                f.Id,
                f.FileName,
                f.ContentType,
                f.SizeBytes,
                f.CreatedAtUtc,
                $"/api/v1/applications/{request.ApplicationId}/files/{f.Id}",
                DownloadFileName.Compose(
                    f.Application?.ApplicationNumber,
                    f.Application?.Order?.OrderNumber,
                    null,
                    f.FileName)))
            .ToList();
    }
}
