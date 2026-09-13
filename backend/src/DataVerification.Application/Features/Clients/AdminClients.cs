using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Common.Models;
using DataVerification.Application.Features.Lookups.Admin;
using DataVerification.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Clients;

/// <summary>A tenant the platform serves. Carries a unique code, a display name and an active flag.</summary>
/// <param name="IsLocalOrder">True for the one client whose orders are local. Exactly one has it.</param>
public sealed record ClientDto(
    Guid Id,
    string Code,
    string Name,
    bool IsActive,
    bool IsLocalOrder,
    int OrderCount)
{
    public static ClientDto From(Client client, int orderCount) =>
        new(client.Id, client.Code, client.Name, client.IsActive, client.IsLocalOrder, orderCount);
}

public sealed record ListClientsQuery : PagedQuery, IRequest<PagedResult<ClientDto>>;

/// <param name="IsLocalOrder">
/// Setting this moves the flag here from whichever client held it. It cannot be cleared on its own:
/// a platform with no local client would have nowhere for a local order to belong, so the way to
/// take it off one client is to put it on another.
/// </param>
public sealed record UpsertClientCommand(
    Guid? Id,
    string Code,
    string Name,
    bool IsActive,
    bool IsLocalOrder = false) : IRequest<ClientDto>;

public sealed record DeleteClientCommand(Guid Id) : IRequest<LookupDeleteOutcome>;

public sealed class UpsertClientCommandValidator : AbstractValidator<UpsertClientCommand>
{
    public UpsertClientCommandValidator()
    {
        RuleFor(c => c.Code).NotEmpty().MaximumLength(20);
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
    }
}

public sealed class ClientAdminHandlers :
    IRequestHandler<ListClientsQuery, PagedResult<ClientDto>>,
    IRequestHandler<UpsertClientCommand, ClientDto>,
    IRequestHandler<DeleteClientCommand, LookupDeleteOutcome>
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditLogger _auditLogger;

    public ClientAdminHandlers(IApplicationDbContext db, IAuditLogger auditLogger)
    {
        _db = db;
        _auditLogger = auditLogger;
    }

    public Task<PagedResult<ClientDto>> Handle(ListClientsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = _db.Clients.AsNoTracking();

        if (request.IsActive is { } isActive)
        {
            query = query.Where(c => c.IsActive == isActive);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(c => EF.Functions.Like(c.Name, $"%{term}%")
                                     || EF.Functions.Like(c.Code, $"%{term}%"));
        }

        // Ordered and paged as entities, shaped into the DTO afterwards. Ordering a queryable that
        // has already been projected makes the whole projection the sort key, and this one counts
        // a subquery — which SQL cannot order by.
        return query
            .OrderBy(c => c.Name)
            .ToPagedResultInSqlAsync(
                request,
                c => new ClientDto(
                    c.Id, c.Code, c.Name, c.IsActive, c.IsLocalOrder, c.Orders.Count),
                cancellationToken);
    }

    public Task<ClientDto> Handle(UpsertClientCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Moving the local-order flag is two writes — clear the old holder, set the new one — and
        // they are sent as two saves so the database's unique index never sees both rows carrying
        // it at once. A transaction is what keeps that pair atomic: a failure between them would
        // otherwise leave the platform with no local client at all.
        return _db.ExecuteInTransactionAsync(ct => SaveAsync(request, ct), cancellationToken);
    }

    private async Task<ClientDto> SaveAsync(UpsertClientCommand request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim();

        // Reject a duplicate code before the unique index does, so the admin sees a field message.
        var codeTaken = await _db.Clients
            .AnyAsync(c => c.Code == code && c.Id != (request.Id ?? Guid.Empty), cancellationToken);
        if (codeTaken)
        {
            throw new ConflictException("client.code_taken", "A client with this code already exists.");
        }

        Client client;
        if (request.Id is { } id)
        {
            client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(Client), id);
        }
        else
        {
            client = new Client { Code = code, Name = request.Name.Trim() };
            _db.Clients.Add(client);
        }

        client.Code = code;
        client.Name = request.Name.Trim();
        client.IsActive = request.IsActive;

        await ApplyLocalOrderAsync(client, request.IsLocalOrder, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        await _auditLogger.LogAsync(
            request.Id is null ? "Client.Created" : "Client.Updated",
            nameof(Client),
            client.Id,
            new { client.Code, client.Name },
            cancellationToken);

        var orderCount = request.Id is null
            ? 0
            : await _db.Orders.CountAsync(o => o.ClientId == client.Id, cancellationToken);

        return ClientDto.From(client, orderCount);
    }

    /// <summary>
    /// Moves the local-order flag onto this client, or refuses to leave the platform without one.
    ///
    /// The previous holder is cleared in the same save, so the two writes land together and the
    /// database's unique index never sees two rows carrying it. Clearing it from the only client
    /// that has it is refused rather than obeyed: "exactly one" is the rule, and a save that left
    /// none would be a silent way to break it.
    /// </summary>
    private async Task ApplyLocalOrderAsync(
        Client client,
        bool isLocalOrder,
        CancellationToken cancellationToken)
    {
        if (isLocalOrder)
        {
            var others = await _db.Clients
                .Where(c => c.IsLocalOrder && c.Id != client.Id)
                .ToListAsync(cancellationToken);

            if (others.Count > 0)
            {
                foreach (var other in others) other.IsLocalOrder = false;

                // Saved on its own, before the flag is set here. Sent together, the two rows would
                // both carry it for the length of one statement and the unique index would reject
                // the write — the caller's transaction is what makes this pair safe to split.
                await _db.SaveChangesAsync(cancellationToken);
            }

            client.IsLocalOrder = true;
            return;
        }

        if (!client.IsLocalOrder) return;

        throw new ConflictException(
            "client.local_order_required",
            "One client must be the local one. Set another client as the local order instead.");
    }

    public async Task<LookupDeleteOutcome> Handle(DeleteClientCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Client), request.Id);

        // Deleting the local client would leave the platform without one, the same end the upsert
        // refuses. Move the flag first.
        if (client.IsLocalOrder)
        {
            throw new ConflictException(
                "client.local_order_required",
                "This is the local client. Set another client as the local order before removing it.");
        }

        // A client with orders is the tenant those orders belong to; retire it instead of deleting,
        // which would orphan them.
        var referenced = await _db.Orders.AnyAsync(o => o.ClientId == request.Id, cancellationToken);
        if (referenced)
        {
            client.IsActive = false;
            await _db.SaveChangesAsync(cancellationToken);
            await _auditLogger.LogAsync("Client.Deactivated", nameof(Client), request.Id, null, cancellationToken);
            return LookupDeleteOutcome.Deactivated;
        }

        _db.Clients.Remove(client);
        await _db.SaveChangesAsync(cancellationToken);
        await _auditLogger.LogAsync("Client.Deleted", nameof(Client), request.Id, null, cancellationToken);
        return LookupDeleteOutcome.Deleted;
    }
}
