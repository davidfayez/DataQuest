using DataVerification.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Tickets.Queries;

/// <summary>
/// The categories the public contact form offers, resolved to the caller's language.
///
/// Active rows only: a retired category must stop being offered without the tickets already filed
/// under it losing what they were about.
/// </summary>
public sealed record GetTicketCategoriesQuery : IRequest<IReadOnlyList<TicketCategoryDto>>;

public sealed class GetTicketCategoriesQueryHandler
    : IRequestHandler<GetTicketCategoriesQuery, IReadOnlyList<TicketCategoryDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetTicketCategoriesQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<TicketCategoryDto>> Handle(
        GetTicketCategoriesQuery request,
        CancellationToken cancellationToken)
    {
        var categories = await _db.TicketCategories
            .AsNoTracking()
            .Where(category => category.IsActive)
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.NameEn)
            .ToListAsync(cancellationToken);

        return categories
            .Select(category => TicketCategoryDto.From(category, _currentUser.LanguageCode))
            .ToList();
    }
}
