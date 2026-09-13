using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Common.Models;
using DataVerification.Application.Features.Lookups.Admin;
using DataVerification.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Payments.Admin;

/// <summary>
/// The bank catalogue, scoped by country. Seeded with a starting list, but admin-managed: a
/// merger or a new licence is an edit here rather than a deployment.
/// </summary>
public sealed record ListBanksQuery : PagedQuery, IRequest<PagedResult<BankDto>>
{
    /// <summary>The bank list is read one country at a time, both here and by the method editor.</summary>
    public Guid? CountryId { get; init; }
}

public sealed record UpsertBankCommand(
    Guid? Id,
    Guid CountryId,
    string NameAr,
    string NameEn,
    string? SwiftCode,
    int SortOrder,
    bool IsActive) : IRequest<BankDto>;

public sealed record DeleteBankCommand(Guid Id) : IRequest<LookupDeleteOutcome>;

public sealed class UpsertBankCommandValidator : AbstractValidator<UpsertBankCommand>
{
    public UpsertBankCommandValidator()
    {
        RuleFor(c => c.CountryId).NotEmpty().WithMessage("Choose the country this bank operates in.");
        RuleFor(c => c.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(c => c.NameEn).NotEmpty().MaximumLength(200);
        RuleFor(c => c.SortOrder).GreaterThanOrEqualTo(0);

        // SWIFT/BIC is 8 or 11 characters; anything else is a typo rather than a shorter code.
        RuleFor(c => c.SwiftCode)
            .Must(code => string.IsNullOrWhiteSpace(code) || code.Trim().Length is 8 or 11)
            .WithMessage("A SWIFT/BIC code is either 8 or 11 characters.");
    }
}

public sealed class BankHandlers :
    IRequestHandler<ListBanksQuery, PagedResult<BankDto>>,
    IRequestHandler<UpsertBankCommand, BankDto>,
    IRequestHandler<DeleteBankCommand, LookupDeleteOutcome>
{
    private readonly IApplicationDbContext _db;
    private readonly AdminLookupService _lookups;

    public BankHandlers(IApplicationDbContext db, AdminLookupService lookups)
    {
        _db = db;
        _lookups = lookups;
    }

    public async Task<PagedResult<BankDto>> Handle(
        ListBanksQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = _db.Banks.AsNoTracking().Include(bank => bank.Country).AsQueryable();

        if (request.CountryId is { } countryId)
        {
            query = query.Where(bank => bank.CountryId == countryId);
        }

        if (request.IsActive is { } isActive)
        {
            query = query.Where(bank => bank.IsActive == isActive);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(bank => EF.Functions.Like(bank.NameEn, $"%{term}%")
                                        || EF.Functions.Like(bank.NameAr, $"%{term}%")
                                        || EF.Functions.Like(bank.SwiftCode!, $"%{term}%"));
        }

        // Sort order is the operator's own arrangement of the picker; ties fall back to the name.
        query = query.OrderBy(bank => bank.SortOrder).ThenBy(bank => bank.NameEn);

        return await query.ToPagedResultAsync(
            request,
            bank => BankDto.From(bank, _lookups.Language),
            cancellationToken);
    }

    public async Task<BankDto> Handle(UpsertBankCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var countryExists = await _db.Countries
            .AnyAsync(country => country.Id == request.CountryId, cancellationToken);

        if (!countryExists)
        {
            throw new NotFoundException(nameof(Country), request.CountryId);
        }

        Bank bank;

        if (request.Id is { } id && id != Guid.Empty)
        {
            bank = await _lookups.RequireAsync(_db.Banks, id, cancellationToken);
        }
        else
        {
            bank = new Bank
            {
                NameAr = request.NameAr,
                NameEn = request.NameEn,
                CountryId = request.CountryId,
            };

            _db.Banks.Add(bank);
        }

        await _lookups.EnsureUniqueAsync(
            _db.Banks,
            other => other.Id != bank.Id
                && other.CountryId == request.CountryId
                && (other.NameEn == request.NameEn || other.NameAr == request.NameAr),
            "bank.duplicate_name",
            "A bank with this name already exists in that country.",
            cancellationToken);

        bank.CountryId = request.CountryId;
        bank.NameAr = request.NameAr;
        bank.NameEn = request.NameEn;
        bank.SwiftCode = string.IsNullOrWhiteSpace(request.SwiftCode)
            ? null
            : request.SwiftCode.Trim().ToUpperInvariant();
        bank.SortOrder = request.SortOrder;
        bank.IsActive = request.IsActive;
        bank.UpdatedAtUtc = DateTime.UtcNow;

        await _lookups.SaveAsync(cancellationToken);
        await _lookups.AuditAsync(
            "Bank.Saved",
            nameof(Bank),
            bank.Id,
            new { bank.NameEn, bank.CountryId, bank.IsActive },
            cancellationToken);

        // Reloaded so the country name travels back with the row the editor just saved.
        var saved = await _db.Banks
            .AsNoTracking()
            .Include(b => b.Country)
            .FirstAsync(b => b.Id == bank.Id, cancellationToken);

        return BankDto.From(saved, _lookups.Language);
    }

    public Task<LookupDeleteOutcome> Handle(
        DeleteBankCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Deactivated rather than removed while a receiving account still names it — deleting
        // would leave an IBAN with no bank against it.
        return _lookups.DeleteOrDeactivateAsync(
            _db.Banks,
            request.Id,
            ct => _db.PaymentMethodAccounts.AnyAsync(a => a.BankId == request.Id, ct),
            "Bank",
            cancellationToken);
    }
}
