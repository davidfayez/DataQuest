using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Common.Models;
using DataVerification.Application.Features.Lookups.Admin;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Content.Admin;

/// <summary>An entry as the admin editor sees it — both scripts, and the inactive ones too.</summary>
public sealed record AdminContactDirectoryEntryDto(
    Guid Id,
    ContactEntryKind Kind,
    string KindName,
    Guid? CountryId,
    string? CountryName,
    string? TitleAr,
    string? TitleEn,
    string? AddressAr,
    string? AddressEn,
    string? Phone,
    string? Email,
    bool IsActive,
    int SortOrder,
    /// <summary>False when the entry has no way to make contact, so the page can say why it is hidden.</summary>
    bool IsReachable)
{
    public static AdminContactDirectoryEntryDto From(ContactDirectoryEntry entry, string? language)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new(
            entry.Id,
            entry.Kind,
            entry.Kind.ToString(),
            entry.CountryId,
            entry.Country?.ResolveName(language),
            entry.TitleAr,
            entry.TitleEn,
            entry.AddressAr,
            entry.AddressEn,
            entry.Phone,
            entry.Email,
            entry.IsActive,
            entry.SortOrder,
            entry.IsReachable);
    }
}

public sealed record ListContactDirectoryQuery
    : PagedQuery, IRequest<PagedResult<AdminContactDirectoryEntryDto>>
{
    public ContactEntryKind? Kind { get; init; }
}

public sealed record UpsertContactDirectoryEntryCommand(
    Guid? Id,
    ContactEntryKind Kind,
    Guid? CountryId,
    string? TitleAr,
    string? TitleEn,
    string? AddressAr,
    string? AddressEn,
    string? Phone,
    string? Email,
    bool IsActive,
    int SortOrder) : IRequest<AdminContactDirectoryEntryDto>;

public sealed record DeleteContactDirectoryEntryCommand(Guid Id) : IRequest<Unit>;

public sealed class UpsertContactDirectoryEntryCommandValidator
    : AbstractValidator<UpsertContactDirectoryEntryCommand>
{
    public UpsertContactDirectoryEntryCommandValidator()
    {
        RuleFor(c => c.Kind).IsInEnum();
        RuleFor(c => c.TitleAr).MaximumLength(200);
        RuleFor(c => c.TitleEn).MaximumLength(200);
        RuleFor(c => c.AddressAr).MaximumLength(500);
        RuleFor(c => c.AddressEn).MaximumLength(500);
        RuleFor(c => c.Phone).MaximumLength(40);
        RuleFor(c => c.SortOrder).GreaterThanOrEqualTo(0);

        RuleFor(c => c.Email)
            .MaximumLength(256)
            .EmailAddress().When(c => !string.IsNullOrWhiteSpace(c.Email))
            .WithMessage("That is not a valid email address.");

        // Numbers are typed by hand from a dozen countries, so the rule is deliberately loose:
        // digits, spaces, brackets, dashes and a leading plus. Anything stricter rejects a real
        // number somewhere in the world.
        RuleFor(c => c.Phone)
            .Matches(@"^\+?[\d\s\-()]{5,}$").When(c => !string.IsNullOrWhiteSpace(c.Phone))
            .WithMessage("That is not a valid phone number.");

        // An entry nobody can act on is not worth publishing.
        RuleFor(c => c)
            .Must(command => !string.IsNullOrWhiteSpace(command.Phone)
                || !string.IsNullOrWhiteSpace(command.Email)
                || !string.IsNullOrWhiteSpace(command.AddressEn)
                || !string.IsNullOrWhiteSpace(command.AddressAr))
            .WithMessage("Give at least a phone number, an email address or an address.");

        // An agent represents somewhere. Without the country the list has nothing to group it by.
        RuleFor(c => c.CountryId)
            .NotEmpty().When(c => c.Kind == ContactEntryKind.AuthorizedAgent)
            .WithMessage("Choose the country this agent covers.");
    }
}

public sealed class ContactDirectoryHandlers :
    IRequestHandler<ListContactDirectoryQuery, PagedResult<AdminContactDirectoryEntryDto>>,
    IRequestHandler<UpsertContactDirectoryEntryCommand, AdminContactDirectoryEntryDto>,
    IRequestHandler<DeleteContactDirectoryEntryCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly AdminLookupService _lookups;

    public ContactDirectoryHandlers(IApplicationDbContext db, AdminLookupService lookups)
    {
        _db = db;
        _lookups = lookups;
    }

    public async Task<PagedResult<AdminContactDirectoryEntryDto>> Handle(
        ListContactDirectoryQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = _db.ContactDirectoryEntries
            .AsNoTracking()
            .Include(entry => entry.Country)
            .AsQueryable();

        if (request.Kind is { } kind)
        {
            query = query.Where(entry => entry.Kind == kind);
        }

        if (request.IsActive is { } isActive)
        {
            query = query.Where(entry => entry.IsActive == isActive);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(entry =>
                EF.Functions.Like(entry.TitleEn!, $"%{term}%")
                || EF.Functions.Like(entry.TitleAr!, $"%{term}%")
                || EF.Functions.Like(entry.Phone!, $"%{term}%")
                || EF.Functions.Like(entry.Email!, $"%{term}%")
                || EF.Functions.Like(entry.Country!.NameEn, $"%{term}%"));
        }

        // Grouped the way the public page reads, then by the operator's own arrangement.
        query = query
            .OrderBy(entry => entry.Kind)
            .ThenBy(entry => entry.SortOrder)
            .ThenBy(entry => entry.Country!.NameEn);

        return await query.ToPagedResultAsync(
            request,
            entry => AdminContactDirectoryEntryDto.From(entry, _lookups.Language),
            cancellationToken);
    }

    public async Task<AdminContactDirectoryEntryDto> Handle(
        UpsertContactDirectoryEntryCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.CountryId is { } countryId)
        {
            var exists = await _db.Countries
                .AnyAsync(country => country.Id == countryId, cancellationToken);

            if (!exists)
            {
                throw new NotFoundException(nameof(Country), countryId);
            }
        }

        ContactDirectoryEntry entry;

        if (request.Id is { } id && id != Guid.Empty)
        {
            entry = await _db.ContactDirectoryEntries
                .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(ContactDirectoryEntry), id);
        }
        else
        {
            entry = new ContactDirectoryEntry();
            _db.ContactDirectoryEntries.Add(entry);
        }

        entry.Kind = request.Kind;
        // The administration is the organisation itself, so a country on it would be noise.
        entry.CountryId = request.Kind == ContactEntryKind.AuthorizedAgent ? request.CountryId : null;
        entry.TitleAr = Trim(request.TitleAr);
        entry.TitleEn = Trim(request.TitleEn);
        entry.AddressAr = Trim(request.AddressAr);
        entry.AddressEn = Trim(request.AddressEn);
        entry.Phone = Trim(request.Phone);
        entry.Email = Trim(request.Email);
        entry.IsActive = request.IsActive;
        entry.SortOrder = request.SortOrder;
        entry.UpdatedAtUtc = DateTime.UtcNow;

        await _lookups.SaveAsync(cancellationToken);
        await _lookups.AuditAsync(
            "ContactDirectory.Saved",
            nameof(ContactDirectoryEntry),
            entry.Id,
            new { Kind = entry.Kind.ToString(), entry.CountryId, entry.IsActive },
            cancellationToken);

        var saved = await _db.ContactDirectoryEntries
            .AsNoTracking()
            .Include(candidate => candidate.Country)
            .FirstAsync(candidate => candidate.Id == entry.Id, cancellationToken);

        return AdminContactDirectoryEntryDto.From(saved, _lookups.Language);
    }

    public async Task<Unit> Handle(
        DeleteContactDirectoryEntryCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entry = await _db.ContactDirectoryEntries
            .FirstOrDefaultAsync(candidate => candidate.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(ContactDirectoryEntry), request.Id);

        // Genuinely deleted, not deactivated: nothing else in the platform references a directory
        // entry, so there is no history to preserve by keeping a hidden row.
        _db.ContactDirectoryEntries.Remove(entry);

        await _lookups.SaveAsync(cancellationToken);
        await _lookups.AuditAsync(
            "ContactDirectory.Deleted",
            nameof(ContactDirectoryEntry),
            request.Id,
            new { Kind = entry.Kind.ToString() },
            cancellationToken);

        return Unit.Value;
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
