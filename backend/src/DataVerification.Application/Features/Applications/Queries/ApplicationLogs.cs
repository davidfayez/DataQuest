using System.Text.Json;
using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Common.Models;
using DataVerification.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Applications.Queries;

/// <summary>Every status transition this application went through, oldest first.</summary>
public sealed record GetApplicationStatusLogQuery(Guid ApplicationId)
    : IRequest<IReadOnlyList<ApplicationStatusLogEntryDto>>;

/// <summary>
/// Everything anyone did to this application — created, edited, submitted, paid, files uploaded,
/// results attached — newest first. Distinct from the status log, which records only the
/// lifecycle: an edit that changes nine fields and no status appears here and nowhere else.
/// </summary>
public sealed record GetApplicationChangeLogQuery : PagedQuery, IRequest<PagedResult<ApplicationChangeLogEntryDto>>
{
    public Guid ApplicationId { get; init; }
}

public sealed record ApplicationStatusLogEntryDto(
    Guid Id,
    ApplicationStatus FromStatus,
    string FromStatusName,
    ApplicationStatus ToStatus,
    string ToStatusName,
    ActorType ChangedByType,
    string ChangedByTypeName,
    string? ChangedByName,
    string? Note,
    DateTime CreatedAtUtc);

/// <param name="Details">
/// The audit payload, flattened to display pairs. Never the raw stored JSON: the applicant's view
/// is a curated projection, so a field added to an audit payload for internal use cannot start
/// appearing on their screen by accident.
/// </param>
public sealed record ApplicationChangeLogEntryDto(
    Guid Id,
    string Action,
    ActorType ActorType,
    string ActorTypeName,
    string? ActorName,
    IReadOnlyDictionary<string, string> Details,
    DateTime CreatedAtUtc);

public sealed class ApplicationLogQueryHandlers :
    IRequestHandler<GetApplicationStatusLogQuery, IReadOnlyList<ApplicationStatusLogEntryDto>>,
    IRequestHandler<GetApplicationChangeLogQuery, PagedResult<ApplicationChangeLogEntryDto>>
{
    /// <summary>
    /// Audit payload keys the applicant never sees. <c>HasInternalComment</c> would disclose that a
    /// reviewer left a private note, which is admin process rather than a change to the application.
    /// </summary>
    private static readonly HashSet<string> HiddenDetailKeys =
        new(StringComparer.OrdinalIgnoreCase) { "hasInternalComment" };

    private const string InternalCommentAction = "Application.CommentAdded";

    /// <summary>How <see cref="CommentVisibility.Internal"/> appears in a serialised audit payload.</summary>
    private const string InternalVisibilityFragment = "\"visibility\":1";

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ApplicationLogQueryHandlers(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<ApplicationStatusLogEntryDto>> Handle(
        GetApplicationStatusLogQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await EnsureVisibleAsync(request.ApplicationId, cancellationToken);

        var history = await _db.ApplicationStatusHistory
            .AsNoTracking()
            .Where(h => h.ApplicationId == request.ApplicationId)
            .OrderBy(h => h.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return history
            .Select(h => new ApplicationStatusLogEntryDto(
                h.Id,
                h.FromStatus,
                h.FromStatus.ToString(),
                h.ToStatus,
                h.ToStatus.ToString(),
                h.ChangedByType,
                h.ChangedByType.ToString(),
                h.ChangedByName,
                h.Note,
                h.CreatedAtUtc))
            .ToList();
    }

    public async Task<PagedResult<ApplicationChangeLogEntryDto>> Handle(
        GetApplicationChangeLogQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await EnsureVisibleAsync(request.ApplicationId, cancellationToken);

        var query = _db.AuditLog
            .AsNoTracking()
            .Where(a => a.EntityType == "Application" && a.EntityId == request.ApplicationId);

        // Internal comments are invisible to the applicant everywhere else, so the entry recording
        // one must not surface here either. Excluded in SQL — not just from the materialised page —
        // so the total and the page boundaries count the same rows the applicant is shown.
        //
        // The pattern is coupled to how AuditLogger serialises: JsonSerializerDefaults.Web, no
        // indentation, enums as numbers. IsInternalOnly below repeats the check against the parsed
        // payload, so if that serialisation ever changes the entry is still withheld — only the
        // count would drift, which is the harmless half of the failure.
        query = query.Where(a =>
            a.Action != InternalCommentAction
            || a.Data == null
            || !a.Data.Contains(InternalVisibilityFragment));

        var entries = await query
            .OrderByDescending(a => a.CreatedAtUtc)
            .ToPagedResultAsync(request, entry => entry, cancellationToken);

        var visible = entries.Items
            .Where(entry => !IsInternalOnly(entry.Action, entry.Data))
            .Select(entry => new ApplicationChangeLogEntryDto(
                entry.Id,
                entry.Action,
                entry.ActorType,
                entry.ActorType.ToString(),
                entry.ActorName,
                Details(entry.Data),

                // IpAddress is deliberately absent: it is a forensic field for administrators.
                entry.CreatedAtUtc))
            .ToList();

        return new PagedResult<ApplicationChangeLogEntryDto>(
            visible,
            entries.Page,
            entries.PageSize,
            entries.TotalCount);
    }

    /// <summary>
    /// An applicant may read only their own application; an admin may read any. A row the caller
    /// is not entitled to is reported as missing rather than forbidden, so probing ids reveals
    /// nothing about which ones exist.
    /// </summary>
    private async Task EnsureVisibleAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        var isAdmin = _currentUser.AdminUserId.HasValue;
        var orderId = _currentUser.OrderId;

        if (!isAdmin && orderId is null)
        {
            throw new ForbiddenAccessException("This endpoint requires an applicant or admin token.");
        }

        var exists = await _db.Applications
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(
                a => a.Id == applicationId && (isAdmin || a.OrderId == orderId),
                cancellationToken);

        if (!exists)
        {
            throw new NotFoundException("Application", applicationId);
        }
    }

    private static bool IsInternalOnly(string action, string? data)
    {
        if (!string.Equals(action, InternalCommentAction, StringComparison.Ordinal))
        {
            return false;
        }

        return ReadVisibility(data) == CommentVisibility.Internal;
    }

    private static CommentVisibility? ReadVisibility(string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(data);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!property.NameEquals("visibility"))
                {
                    continue;
                }

                // Serialized as a number by default, but tolerate the name in case that changes.
                return property.Value.ValueKind switch
                {
                    JsonValueKind.Number when property.Value.TryGetInt32(out var value)
                        => (CommentVisibility)value,
                    JsonValueKind.String when Enum.TryParse<CommentVisibility>(
                        property.Value.GetString(), true, out var parsed) => parsed,
                    _ => null,
                };
            }
        }
        catch (JsonException)
        {
            // A malformed payload must not hide the entry it belongs to; treat it as non-internal
            // and let the details projection drop what it cannot read.
        }

        return null;
    }

    /// <summary>
    /// Flattens the audit payload's top-level scalars into display pairs. Nested objects and arrays
    /// are skipped rather than stringified — an unreadable blob on screen helps nobody.
    /// </summary>
    private static Dictionary<string, string> Details(string? data)
    {
        var details = new Dictionary<string, string>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(data))
        {
            return details;
        }

        try
        {
            using var document = JsonDocument.Parse(data);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return details;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (HiddenDetailKeys.Contains(property.Name))
                {
                    continue;
                }

                var value = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number => property.Value.ToString(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => null,
                };

                if (value is not null)
                {
                    details[property.Name] = value;
                }
            }
        }
        catch (JsonException)
        {
            // As above: an entry with an unreadable payload still belongs in the log.
        }

        return details;
    }
}
