using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Common.Models;
using DataVerification.Domain.Authorization;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Tickets.Admin;

/// <summary>The support queue, filtered the way a support desk actually works through it.</summary>
public sealed record ListTicketsQuery : PagedQuery, IRequest<PagedResult<TicketListItemDto>>
{
    public TicketStatus? Status { get; init; }

    public Guid? TicketCategoryId { get; init; }

    /// <summary>Narrows to one person's workload.</summary>
    public Guid? AssignedToAdminUserId { get; init; }

    /// <summary>True for tickets nobody has picked up yet — the queue's most useful filter.</summary>
    public bool? Unassigned { get; init; }
}

public sealed record GetTicketQuery(Guid TicketId) : IRequest<TicketDetailsDto>;

/// <summary>The support users a ticket can be handed to.</summary>
public sealed record GetTicketAssigneesQuery : IRequest<IReadOnlyList<TicketAssigneeDto>>;

/// <summary>Orders matching what the linker has typed so far.</summary>
public sealed record SearchTicketOrdersQuery(string? Search)
    : IRequest<IReadOnlyList<TicketOrderOptionDto>>;

/// <summary>The applications belonging to one order, for the second half of the link.</summary>
public sealed record GetTicketOrderApplicationsQuery(Guid OrderId)
    : IRequest<IReadOnlyList<TicketApplicationOptionDto>>;

public sealed class AdminTicketQueryHandlers :
    IRequestHandler<ListTicketsQuery, PagedResult<TicketListItemDto>>,
    IRequestHandler<GetTicketQuery, TicketDetailsDto>,
    IRequestHandler<GetTicketAssigneesQuery, IReadOnlyList<TicketAssigneeDto>>,
    IRequestHandler<SearchTicketOrdersQuery, IReadOnlyList<TicketOrderOptionDto>>,
    IRequestHandler<GetTicketOrderApplicationsQuery, IReadOnlyList<TicketApplicationOptionDto>>
{
    /// <summary>Enough to pick from without the picker becoming a report.</summary>
    private const int MaxPickerResults = 25;

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public AdminTicketQueryHandlers(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<TicketListItemDto>> Handle(
        ListTicketsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = _db.Tickets
            .AsNoTracking()
            .Include(ticket => ticket.Category)
            .Include(ticket => ticket.AssignedTo)
            .Include(ticket => ticket.Order)
            .Include(ticket => ticket.Application)
            .AsQueryable();

        if (request.Status is { } status)
        {
            query = query.Where(ticket => ticket.Status == status);
        }

        if (request.TicketCategoryId is { } categoryId)
        {
            query = query.Where(ticket => ticket.TicketCategoryId == categoryId);
        }

        if (request.AssignedToAdminUserId is { } assignee)
        {
            query = query.Where(ticket => ticket.AssignedToAdminUserId == assignee);
        }

        if (request.Unassigned == true)
        {
            query = query.Where(ticket => ticket.AssignedToAdminUserId == null);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();

            // The four things support has to hand when someone chases an enquiry: the reference
            // they were given, the subject, and the name or address they wrote in from.
            query = query.Where(ticket =>
                EF.Functions.Like(ticket.TicketNumber, $"%{term}%")
                || EF.Functions.Like(ticket.Subject, $"%{term}%")
                || EF.Functions.Like(ticket.Name, $"%{term}%")
                || EF.Functions.Like(ticket.Email, $"%{term}%"));
        }

        // Newest first: a support queue is worked from the top, and an enquiry that has been
        // waiting since yesterday is not more urgent than one that arrived this morning unanswered.
        query = query.OrderByDescending(ticket => ticket.CreatedAtUtc);

        var total = await query.CountAsync(cancellationToken);

        // This list builds its page by hand rather than through ToPagedResultAsync, so the sort the
        // reader asked for has to be applied here or the column headings do nothing.
        var rows = await query
            .ApplySort(request, TicketSortAliases)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(ticket => new
            {
                Ticket = ticket,
                FileCount = ticket.Files.Count(file => file.TicketActionDocumentId == null),
                ActionCount = ticket.Actions.Count,
                LastReplied = ticket.Actions
                    .Where(action => action.NotifiedAtUtc != null)
                    .Max(action => (DateTime?)action.NotifiedAtUtc),
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new TicketListItemDto(
                row.Ticket.Id,
                row.Ticket.TicketNumber,
                row.Ticket.Subject,
                row.Ticket.Name,
                row.Ticket.Email,
                row.Ticket.Category?.ResolveName(_currentUser.LanguageCode) ?? string.Empty,
                row.Ticket.Status,
                row.Ticket.Status.ToString(),
                row.Ticket.AssignedTo?.FullName,
                row.Ticket.Order?.OrderNumber,
                row.Ticket.Application?.ApplicationNumber,
                row.FileCount,
                row.ActionCount,
                row.LastReplied,
                row.Ticket.CreatedAtUtc))
            .ToList();

        return new PagedResult<TicketListItemDto>(items, request.Page, request.PageSize, total);
    }

    /// <summary>Columns the queue shows under names the ticket itself does not use.</summary>
    private static readonly Dictionary<string, string> TicketSortAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["categoryName"] = nameof(Ticket.TicketCategoryId),
            ["statusName"] = nameof(Ticket.Status),
            ["createdAtUtc"] = nameof(Ticket.CreatedAtUtc),
        };

    public async Task<TicketDetailsDto> Handle(
        GetTicketQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ticket = await _db.Tickets
            .AsNoTracking()
            .Include(t => t.Category)
            .Include(t => t.AssignedTo)
            .Include(t => t.Order)
            .Include(t => t.Application)
            .Include(t => t.Files)
            .Include(t => t.Actions).ThenInclude(a => a.Documents).ThenInclude(d => d.Files)
            .FirstOrDefaultAsync(t => t.Id == request.TicketId, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), request.TicketId);

        return new TicketDetailsDto(
            ticket.Id,
            ticket.TicketNumber,
            ticket.Subject,
            ticket.Description,
            ticket.Name,
            ticket.Email,
            ticket.PhoneCountryCode,
            ticket.PhoneNumber,
            ticket.FullPhoneNumber,
            ticket.LanguageCode,
            ticket.TicketCategoryId,
            ticket.Category?.ResolveName(_currentUser.LanguageCode) ?? string.Empty,
            ticket.Status,
            ticket.Status.ToString(),
            ticket.AssignedToAdminUserId,
            ticket.AssignedTo?.FullName,
            ticket.AssignedAtUtc,
            ticket.OrderId,
            ticket.Order?.OrderNumber,
            ticket.ApplicationId,
            ticket.Application?.ApplicationNumber,
            ticket.CreatedAtUtc,
            ticket.UpdatedAtUtc,
            // Only what the sender attached to the enquiry itself; a reply's files travel with the
            // document that names them.
            ticket.Files
                .Where(file => file.TicketActionDocumentId is null)
                .OrderBy(file => file.CreatedAtUtc)
                .Select(TicketFileDto.From)
                .ToList(),
            ticket.Actions
                .OrderBy(action => action.CreatedAtUtc)
                .Select(TicketActionDto.From)
                .ToList());
    }

    /// <summary>
    /// Who a ticket may be handed to: active administrators who can actually open the queue.
    ///
    /// Defined by capability rather than by a role named "technical support" — a role can be
    /// renamed or replaced, but someone without <see cref="Permissions.TicketsView"/> could never
    /// see what you assigned them, and offering them would create work that silently goes nowhere.
    /// </summary>
    public async Task<IReadOnlyList<TicketAssigneeDto>> Handle(
        GetTicketAssigneesQuery request,
        CancellationToken cancellationToken)
    {
        // The permission check is a Where, not a post-filter: it keeps the whole permission graph
        // in SQL instead of pulling every administrator's grants back to compare them here.
        return await _db.AdminUsers
            .AsNoTracking()
            .Where(user => user.IsActive)
            .Where(user => user.UserRoles.Any(userRole => userRole.Role!.RolePermissions
                    .Any(rolePermission => rolePermission.Permission!.Name == Permissions.TicketsView))
                || user.UserPermissions.Any(direct =>
                    direct.Permission!.Name == Permissions.TicketsView))
            .OrderBy(user => user.FullName)
            .Select(user => new TicketAssigneeDto(
                user.Id,
                user.FullName,
                user.Email,
                // Shown beside each name so a ticket is handed to whoever has room for it.
                _db.Tickets.Count(ticket =>
                    ticket.AssignedToAdminUserId == user.Id
                    && ticket.Status != TicketStatus.Closed)))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TicketOrderOptionDto>> Handle(
        SearchTicketOrdersQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = _db.Orders.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(order =>
                EF.Functions.Like(order.OrderNumber, $"%{term}%")
                || EF.Functions.Like(order.Email, $"%{term}%"));
        }

        return await query
            .OrderByDescending(order => order.CreatedAtUtc)
            .Take(MaxPickerResults)
            .Select(order => new TicketOrderOptionDto(order.Id, order.OrderNumber, order.Email))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TicketApplicationOptionDto>> Handle(
        GetTicketOrderApplicationsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _db.Applications
            .AsNoTracking()
            .Where(application => application.OrderId == request.OrderId)
            .OrderByDescending(application => application.CreatedAtUtc)
            .Select(application => new TicketApplicationOptionDto(
                application.Id,
                application.ApplicationNumber,
                application.Status.ToString(),
                application.AddressedTo))
            .ToListAsync(cancellationToken);
    }
}
