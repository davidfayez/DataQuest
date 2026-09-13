using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Common.Models;
using DataVerification.Application.Features.Lookups.Admin;
using DataVerification.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Tickets.Admin;

/// <summary>
/// The categories the public contact form offers, managed like every other lookup on the platform.
/// </summary>
public sealed record ListTicketCategoriesQuery : PagedQuery, IRequest<PagedResult<TicketCategoryDto>>;

public sealed record UpsertTicketCategoryCommand(
    Guid? Id,
    string NameAr,
    string NameEn,
    int SortOrder,
    bool IsActive) : IRequest<TicketCategoryDto>;

public sealed record DeleteTicketCategoryCommand(Guid Id) : IRequest<LookupDeleteOutcome>;

public sealed class UpsertTicketCategoryCommandValidator
    : AbstractValidator<UpsertTicketCategoryCommand>
{
    public UpsertTicketCategoryCommandValidator()
    {
        RuleFor(c => c.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(c => c.NameEn).NotEmpty().MaximumLength(200);
        RuleFor(c => c.SortOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed class TicketCategoryHandlers :
    IRequestHandler<ListTicketCategoriesQuery, PagedResult<TicketCategoryDto>>,
    IRequestHandler<UpsertTicketCategoryCommand, TicketCategoryDto>,
    IRequestHandler<DeleteTicketCategoryCommand, LookupDeleteOutcome>
{
    private readonly IApplicationDbContext _db;
    private readonly AdminLookupService _lookups;

    public TicketCategoryHandlers(IApplicationDbContext db, AdminLookupService lookups)
    {
        _db = db;
        _lookups = lookups;
    }

    public async Task<PagedResult<TicketCategoryDto>> Handle(
        ListTicketCategoriesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = _db.TicketCategories.AsNoTracking().AsQueryable();

        if (request.IsActive is { } isActive)
        {
            query = query.Where(category => category.IsActive == isActive);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(category => EF.Functions.Like(category.NameEn, $"%{term}%")
                                            || EF.Functions.Like(category.NameAr, $"%{term}%"));
        }

        query = query.OrderBy(category => category.SortOrder).ThenBy(category => category.NameEn);

        return await query.ToPagedResultAsync(
            request,
            category => TicketCategoryDto.From(category, _lookups.Language),
            cancellationToken);
    }

    public async Task<TicketCategoryDto> Handle(
        UpsertTicketCategoryCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        TicketCategory category;

        if (request.Id is { } id && id != Guid.Empty)
        {
            category = await _lookups.RequireAsync(_db.TicketCategories, id, cancellationToken);
        }
        else
        {
            category = new TicketCategory { NameAr = request.NameAr, NameEn = request.NameEn };
            _db.TicketCategories.Add(category);
        }

        await _lookups.EnsureUniqueAsync(
            _db.TicketCategories,
            other => other.Id != category.Id
                && (other.NameEn == request.NameEn || other.NameAr == request.NameAr),
            "ticket_category.duplicate_name",
            "A category with this name already exists.",
            cancellationToken);

        category.NameAr = request.NameAr;
        category.NameEn = request.NameEn;
        category.SortOrder = request.SortOrder;
        category.IsActive = request.IsActive;
        category.UpdatedAtUtc = DateTime.UtcNow;

        await _lookups.SaveAsync(cancellationToken);
        await _lookups.AuditAsync(
            "TicketCategory.Saved",
            nameof(TicketCategory),
            category.Id,
            new { category.NameEn, category.IsActive },
            cancellationToken);

        return TicketCategoryDto.From(category, _lookups.Language);
    }

    public Task<LookupDeleteOutcome> Handle(
        DeleteTicketCategoryCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Deactivated rather than removed once tickets have been filed under it: deleting would
        // leave historic enquiries with no record of what they were about.
        return _lookups.DeleteOrDeactivateAsync(
            _db.TicketCategories,
            request.Id,
            ct => _db.Tickets.AnyAsync(ticket => ticket.TicketCategoryId == request.Id, ct),
            "TicketCategory",
            cancellationToken);
    }
}
