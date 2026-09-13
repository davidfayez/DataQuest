using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Content;

/// <summary>One way to reach the organisation, resolved to the visitor's language.</summary>
/// <param name="CountryName">
/// Null when the entry names no country — which is the ordinary case for the administration.
/// </param>
public sealed record ContactDirectoryEntryDto(
    Guid Id,
    ContactEntryKind Kind,
    string KindName,
    string? CountryName,
    string? CountryCode,
    string? Title,
    string? Address,
    string? Phone,
    string? Email);

/// <summary>
/// The contact page's directory, split into the two groups it shows.
/// </summary>
/// <remarks>
/// Returned as two lists rather than one flat list the client has to sort out: the page renders
/// them as two distinct sections, and deciding which entry belongs where is the server's job.
/// </remarks>
public sealed record ContactDirectoryDto(
    IReadOnlyList<ContactDirectoryEntryDto> AuthorizedAgents,
    IReadOnlyList<ContactDirectoryEntryDto> Administration);

public sealed record GetContactDirectoryQuery : IRequest<ContactDirectoryDto>;

public sealed class GetContactDirectoryQueryHandler
    : IRequestHandler<GetContactDirectoryQuery, ContactDirectoryDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetContactDirectoryQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<ContactDirectoryDto> Handle(
        GetContactDirectoryQuery request,
        CancellationToken cancellationToken)
    {
        var entries = await _db.ContactDirectoryEntries
            .AsNoTracking()
            .Include(entry => entry.Country)
            .Where(entry => entry.IsActive)
            .OrderBy(entry => entry.SortOrder)
            .ToListAsync(cancellationToken);

        var language = _currentUser.LanguageCode;

        // An entry with no way to make contact is dropped rather than shown: telling a visitor
        // there is an agent in their country while giving them no number is worse than silence.
        var visible = entries
            .Where(entry => entry.IsReachable)
            .Select(entry => new ContactDirectoryEntryDto(
                entry.Id,
                entry.Kind,
                entry.Kind.ToString(),
                entry.Country?.ResolveName(language),
                entry.Country?.Code,
                entry.ResolveTitle(language),
                entry.ResolveAddress(language),
                entry.Phone,
                entry.Email))
            .ToList();

        return new ContactDirectoryDto(
            visible.Where(entry => entry.Kind == ContactEntryKind.AuthorizedAgent).ToList(),
            visible.Where(entry => entry.Kind == ContactEntryKind.Administration).ToList());
    }
}
