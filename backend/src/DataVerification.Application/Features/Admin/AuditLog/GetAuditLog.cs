using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Common.Models;
using DataVerification.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Admin.AuditLog;

/// <summary>The global audit trail, filterable by action, entity, actor and date.</summary>
public sealed record GetAuditLogQuery : PagedQuery, IRequest<PagedResult<AuditLogEntryDto>>
{
    public string? Action { get; init; }

    public string? EntityType { get; init; }

    public Guid? EntityId { get; init; }

    public ActorType? ActorType { get; init; }

    public DateTime? FromUtc { get; init; }

    public DateTime? ToUtc { get; init; }
}

public sealed record AuditLogEntryDto(
    Guid Id,
    string Action,
    string EntityType,
    Guid? EntityId,
    ActorType ActorType,
    string ActorTypeName,
    Guid? ActorId,
    string? ActorName,
    string? Data,
    string? IpAddress,
    DateTime CreatedAtUtc);

public sealed class GetAuditLogQueryHandler
    : IRequestHandler<GetAuditLogQuery, PagedResult<AuditLogEntryDto>>
{
    private readonly IApplicationDbContext _db;

    public GetAuditLogQueryHandler(IApplicationDbContext db) => _db = db;

    public Task<PagedResult<AuditLogEntryDto>> Handle(
        GetAuditLogQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = _db.AuditLog.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Action))
        {
            var action = request.Action.Trim();
            query = query.Where(entry => EF.Functions.Like(entry.Action, $"%{action}%"));
        }

        if (!string.IsNullOrWhiteSpace(request.EntityType))
        {
            query = query.Where(entry => entry.EntityType == request.EntityType);
        }

        if (request.EntityId is { } entityId)
        {
            query = query.Where(entry => entry.EntityId == entityId);
        }

        if (request.ActorType is { } actorType)
        {
            query = query.Where(entry => entry.ActorType == actorType);
        }

        if (request.FromUtc is { } from)
        {
            query = query.Where(entry => entry.CreatedAtUtc >= from);
        }

        if (request.ToUtc is { } to)
        {
            query = query.Where(entry => entry.CreatedAtUtc <= to);
        }

        // Free-text search spans the actor and the serialized payload, which is where the useful
        // identifying detail usually sits.
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(entry =>
                EF.Functions.Like(entry.Action, $"%{term}%")
                || EF.Functions.Like(entry.ActorName ?? string.Empty, $"%{term}%")
                || EF.Functions.Like(entry.Data ?? string.Empty, $"%{term}%"));
        }

        query = query.OrderByDescending(entry => entry.CreatedAtUtc);

        return query.ToPagedResultAsync(
            request,
            entry => new AuditLogEntryDto(
                entry.Id,
                entry.Action,
                entry.EntityType,
                entry.EntityId,
                entry.ActorType,
                entry.ActorType.ToString(),
                entry.ActorId,
                entry.ActorName,
                entry.Data,
                entry.IpAddress,
                entry.CreatedAtUtc),
            cancellationToken);
    }
}
