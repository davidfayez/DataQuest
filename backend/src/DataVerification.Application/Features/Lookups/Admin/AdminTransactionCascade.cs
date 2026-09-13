using DataVerification.Domain.Common;
using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Common.Models;
using DataVerification.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Lookups.Admin;

// --------------------------------------------------------- Transaction types

public sealed record ListTransactionTypesQuery : PagedQuery, IRequest<PagedResult<TransactionTypeDto>>
{
    /// <summary>Optional country filter; the admin screen is country-scoped.</summary>
    public Guid? CountryId { get; init; }
}

public sealed record UpsertTransactionTypeCommand(
    Guid? Id,
    IReadOnlyList<Guid> CountryIds,
    string NameAr,
    string NameEn,
    bool IsActive,
    string? DescriptionAr = null,
    string? DescriptionEn = null,
    string? Code = null) : IRequest<TransactionTypeDto>;

public sealed record DeleteTransactionTypeCommand(Guid Id) : IRequest<LookupDeleteOutcome>;

public sealed class UpsertTransactionTypeCommandValidator
    : AbstractValidator<UpsertTransactionTypeCommand>
{
    public UpsertTransactionTypeCommandValidator()
    {
        RuleFor(c => c.CountryIds).NotEmpty()
            .WithMessage("Select at least one country.");
        RuleFor(c => c.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(c => c.NameEn).NotEmpty().MaximumLength(200);
        // Both languages, every save: an authority, a transaction type or a sub-type is chosen
        // by an applicant who reads only one of them, so neither may be left blank.
        RuleFor(c => c.DescriptionAr).NotEmpty()
            .WithMessage("Enter the Arabic description.").MaximumLength(2000);
        RuleFor(c => c.DescriptionEn).NotEmpty()
            .WithMessage("Enter the English description.").MaximumLength(2000);
        RuleFor(c => c.Code)
            .Must(code => !string.IsNullOrWhiteSpace(code)).WithMessage("Enter a code.")
            .Must(code => string.IsNullOrWhiteSpace(code) || LookupCode.IsValid(code))
            .WithMessage("A code is 2 to 30 letters or digits, with - or _ between.");
    }
}

public sealed class TransactionTypeAdminHandlers :
    IRequestHandler<ListTransactionTypesQuery, PagedResult<TransactionTypeDto>>,
    IRequestHandler<UpsertTransactionTypeCommand, TransactionTypeDto>,
    IRequestHandler<DeleteTransactionTypeCommand, LookupDeleteOutcome>
{
    private readonly IApplicationDbContext _db;
    private readonly AdminLookupService _lookups;

    public TransactionTypeAdminHandlers(IApplicationDbContext db, AdminLookupService lookups)
    {
        _db = db;
        _lookups = lookups;
    }

    public Task<PagedResult<TransactionTypeDto>> Handle(
        ListTransactionTypesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var source = request.CountryId is { } countryId
            ? _db.TransactionTypes
                .Include(t => t.CountryLinks)
                .Where(t => t.CountryLinks.Any(link => link.CountryId == countryId))
            : _db.TransactionTypes.Include(t => t.CountryLinks);

        return _lookups.ListAsync(
            source,
            request,
            t => TransactionTypeDto.From(t, _lookups.Language),
            cancellationToken);
    }

    public async Task<TransactionTypeDto> Handle(
        UpsertTransactionTypeCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestedCountries = request.CountryIds.Distinct().ToList();
        var knownCountryCount = await _db.Countries
            .CountAsync(c => requestedCountries.Contains(c.Id), cancellationToken);
        if (knownCountryCount != requestedCountries.Count)
        {
            throw new NotFoundException("One or more of the countries supplied do not exist.");
        }

        TransactionType transactionType;
        if (request.Id is { } id)
        {
            transactionType = await _db.TransactionTypes
                .Include(type => type.CountryLinks)
                .FirstOrDefaultAsync(type => type.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(TransactionType), id);
        }
        else
        {
            transactionType = new TransactionType
            {
                NameAr = request.NameAr,
                NameEn = request.NameEn,
            };
            _db.TransactionTypes.Add(transactionType);
        }

        var code = LookupCode.Normalize(request.Code);

        // Checked ahead of the unique index so the admin is told which code is taken rather than
        // shown a database error. Excluding this row lets an edit keep the code it already has.
        if (await _db.TransactionTypes.AnyAsync(
                other => other.Code == code && other.Id != transactionType.Id,
                cancellationToken))
        {
            throw new ConflictException(
                "transaction_type.duplicate_code",
                $"Another transaction type already uses the code '{code}'.");
        }

        transactionType.Code = code;
        transactionType.NameAr = request.NameAr.Trim();
        transactionType.NameEn = request.NameEn.Trim();
        transactionType.DescriptionAr = request.DescriptionAr!.Trim();
        transactionType.DescriptionEn = request.DescriptionEn!.Trim();
        transactionType.IsActive = request.IsActive;

        var removedCountryIds = transactionType.CountryLinks
            .Where(link => !requestedCountries.Contains(link.CountryId))
            .Select(link => link.CountryId)
            .ToList();
        if (removedCountryIds.Count > 0)
        {
            var mappingInUse = await _db.Applications.AnyAsync(
                application => application.TransactionTypeId == transactionType.Id
                               && application.Order != null
                               && application.Order.VerificationCountryId != null
                               && removedCountryIds.Contains(
                                   application.Order.VerificationCountryId.Value),
                cancellationToken);
            if (mappingInUse)
            {
                throw new ConflictException(
                    "transaction_type.country_in_use",
                    "A country used by an existing application cannot be removed from this transaction type.");
            }
        }

        foreach (var link in transactionType.CountryLinks
                     .Where(link => !requestedCountries.Contains(link.CountryId))
                     .ToList())
        {
            _db.TransactionTypeCountries.Remove(link);
        }

        var existingCountryIds = transactionType.CountryLinks
            .Select(link => link.CountryId)
            .ToHashSet();
        foreach (var countryId in requestedCountries.Where(id => !existingCountryIds.Contains(id)))
        {
            transactionType.CountryLinks.Add(new TransactionTypeCountry
            {
                TransactionTypeId = transactionType.Id,
                CountryId = countryId,
            });
        }

        await _lookups.SaveAsync(cancellationToken);
        await _lookups.AuditAsync(
            request.Id is null ? "TransactionType.Created" : "TransactionType.Updated",
            nameof(TransactionType),
            transactionType.Id,
            new { transactionType.NameEn, CountryIds = requestedCountries },
            cancellationToken);

        return TransactionTypeDto.From(transactionType, _lookups.Language);
    }

    public Task<LookupDeleteOutcome> Handle(
        DeleteTransactionTypeCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _lookups.DeleteOrDeactivateAsync(
            _db.TransactionTypes,
            request.Id,
            async ct => await _db.Applications.AnyAsync(a => a.TransactionTypeId == request.Id, ct)
                        || await _db.SubTransactionTypes.AnyAsync(s => s.TransactionTypeId == request.Id, ct),
            "TransactionType",
            cancellationToken);
    }
}

// ----------------------------------------------------- Sub-transaction types

public sealed record ListSubTransactionTypesQuery
    : PagedQuery, IRequest<PagedResult<SubTransactionTypeDto>>
{
    public Guid? TransactionTypeId { get; init; }
}

/// <param name="CountryIds">
/// Where this sub-type is offered. Must be a subset of the parent transaction type's countries —
/// a sub-type cannot reach somewhere its parent does not.
/// </param>
public sealed record UpsertSubTransactionTypeCommand(
    Guid? Id,
    Guid TransactionTypeId,
    string NameAr,
    string NameEn,
    bool IsActive,
    IReadOnlyList<Guid>? CountryIds = null,
    string? DescriptionAr = null,
    string? DescriptionEn = null,
    string? Code = null) : IRequest<SubTransactionTypeDto>;

public sealed record DeleteSubTransactionTypeCommand(Guid Id) : IRequest<LookupDeleteOutcome>;

public sealed class UpsertSubTransactionTypeCommandValidator
    : AbstractValidator<UpsertSubTransactionTypeCommand>
{
    public UpsertSubTransactionTypeCommandValidator()
    {
        RuleFor(c => c.TransactionTypeId).NotEmpty();
        RuleFor(c => c.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(c => c.NameEn).NotEmpty().MaximumLength(200);
        // Both languages, every save: an authority, a transaction type or a sub-type is chosen
        // by an applicant who reads only one of them, so neither may be left blank.
        RuleFor(c => c.DescriptionAr).NotEmpty()
            .WithMessage("Enter the Arabic description.").MaximumLength(2000);
        RuleFor(c => c.DescriptionEn).NotEmpty()
            .WithMessage("Enter the English description.").MaximumLength(2000);
        RuleFor(c => c.Code)
            .Must(code => !string.IsNullOrWhiteSpace(code)).WithMessage("Enter a code.")
            .Must(code => string.IsNullOrWhiteSpace(code) || LookupCode.IsValid(code))
            .WithMessage("A code is 2 to 30 letters or digits, with - or _ between.");
    }
}

public sealed class SubTransactionTypeAdminHandlers :
    IRequestHandler<ListSubTransactionTypesQuery, PagedResult<SubTransactionTypeDto>>,
    IRequestHandler<UpsertSubTransactionTypeCommand, SubTransactionTypeDto>,
    IRequestHandler<DeleteSubTransactionTypeCommand, LookupDeleteOutcome>
{
    private readonly IApplicationDbContext _db;
    private readonly AdminLookupService _lookups;

    public SubTransactionTypeAdminHandlers(IApplicationDbContext db, AdminLookupService lookups)
    {
        _db = db;
        _lookups = lookups;
    }

    public Task<PagedResult<SubTransactionTypeDto>> Handle(
        ListSubTransactionTypesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var source = request.TransactionTypeId is { } parentId
            ? _db.SubTransactionTypes.Include(s => s.CountryLinks)
                .Where(s => s.TransactionTypeId == parentId)
            : _db.SubTransactionTypes.Include(s => s.CountryLinks);

        return _lookups.ListAsync(
            source,
            request,
            s => SubTransactionTypeDto.From(s, _lookups.Language),
            cancellationToken);
    }

    public async Task<SubTransactionTypeDto> Handle(
        UpsertSubTransactionTypeCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var parent = await _db.TransactionTypes
            .Include(type => type.CountryLinks)
            .FirstOrDefaultAsync(type => type.Id == request.TransactionTypeId, cancellationToken)
            ?? throw new NotFoundException(nameof(TransactionType), request.TransactionTypeId);

        // The parent decides where a sub-type can be offered. Anything outside that set is a
        // mistake in the caller, not something to silently drop.
        var requestedCountries = (request.CountryIds ?? []).Distinct().ToList();
        var parentCountries = parent.CountryLinks.Select(link => link.CountryId).ToHashSet();

        if (requestedCountries.Any(id => !parentCountries.Contains(id)))
        {
            throw new ConflictException(
                "sub_transaction_type.country_not_in_parent",
                "A sub-transaction type can only be offered in countries its transaction type covers.");
        }

        SubTransactionType subType;
        if (request.Id is { } id)
        {
            subType = await _db.SubTransactionTypes
                .Include(s => s.CountryLinks)
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(SubTransactionType), id);
        }
        else
        {
            subType = new SubTransactionType
            {
                TransactionTypeId = request.TransactionTypeId,
                NameAr = request.NameAr,
                NameEn = request.NameEn,
            };
            _db.SubTransactionTypes.Add(subType);
        }

        subType.TransactionTypeId = request.TransactionTypeId;
        var code = LookupCode.Normalize(request.Code);

        // Checked ahead of the unique index so the admin is told which code is taken rather than
        // shown a database error. Excluding this row lets an edit keep the code it already has.
        if (await _db.SubTransactionTypes.AnyAsync(
                other => other.Code == code && other.Id != subType.Id,
                cancellationToken))
        {
            throw new ConflictException(
                "sub_transaction_type.duplicate_code",
                $"Another sub-transaction type already uses the code '{code}'.");
        }

        subType.Code = code;
        subType.NameAr = request.NameAr.Trim();
        subType.NameEn = request.NameEn.Trim();
        subType.DescriptionAr = request.DescriptionAr!.Trim();
        subType.DescriptionEn = request.DescriptionEn!.Trim();
        subType.IsActive = request.IsActive;

        foreach (var stale in subType.CountryLinks
            .Where(link => !requestedCountries.Contains(link.CountryId))
            .ToList())
        {
            subType.CountryLinks.Remove(stale);
        }

        foreach (var countryId in requestedCountries
            .Where(id => subType.CountryLinks.All(link => link.CountryId != id)))
        {
            subType.CountryLinks.Add(new SubTransactionTypeCountry { CountryId = countryId });
        }

        await _lookups.SaveAsync(cancellationToken);
        await _lookups.AuditAsync(
            request.Id is null ? "SubTransactionType.Created" : "SubTransactionType.Updated",
            nameof(SubTransactionType),
            subType.Id,
            new { subType.NameEn, subType.TransactionTypeId },
            cancellationToken);

        return SubTransactionTypeDto.From(subType, _lookups.Language);
    }

    public Task<LookupDeleteOutcome> Handle(
        DeleteSubTransactionTypeCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _lookups.DeleteOrDeactivateAsync(
            _db.SubTransactionTypes,
            request.Id,
            async ct => await _db.Applications.AnyAsync(a => a.SubTransactionTypeId == request.Id, ct)
                        || await _db.AuthoritySubTransactionTypes
                            .AnyAsync(link => link.SubTransactionTypeId == request.Id, ct),
            "SubTransactionType",
            cancellationToken);
    }
}
