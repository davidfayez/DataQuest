using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Common.Models;
using DataVerification.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Lookups.Admin;

// ---------------------------------------------------------------- Countries

public sealed record ListCountriesQuery : PagedQuery, IRequest<PagedResult<CountryDto>>;

public sealed record UpsertCountryCommand(
    Guid? Id,
    string Code,
    string PhoneCode,
    string NameAr,
    string NameEn,
    bool IsActive) : IRequest<CountryDto>;

public sealed record DeleteCountryCommand(Guid Id) : IRequest<LookupDeleteOutcome>;

public sealed class UpsertCountryCommandValidator : AbstractValidator<UpsertCountryCommand>
{
    public UpsertCountryCommandValidator()
    {
        RuleFor(c => c.Code).NotEmpty().Length(2).Matches("^[A-Za-z]{2}$")
            .WithMessage("Country code must be a two-letter ISO 3166-1 alpha-2 code.");
        // Optional. The seeded world catalogue carries a calling prefix for almost none of its
        // countries, the column defaults to empty and the admin list renders a dash for a missing
        // one — so requiring it here made every seeded country impossible to edit at all. Only the
        // format is enforced, and only when a prefix is actually supplied.
        RuleFor(c => c.PhoneCode).MaximumLength(8);

        RuleFor(c => c.PhoneCode)
            .Matches(@"^\+[1-9]\d{0,6}$")
            .When(c => !IsBlank(c.PhoneCode))
            .WithMessage("Phone code must start with + and contain digits, for example +20.");
        RuleFor(c => c.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(c => c.NameEn).NotEmpty().MaximumLength(200);
    }

    /// <summary>
    /// Treats a lone <c>+</c> as "not supplied": the edit form pre-fills the prefix character, so
    /// an operator who never touches the field would otherwise submit an invalid value.
    /// </summary>
    internal static bool IsBlank(string? phoneCode) =>
        string.IsNullOrWhiteSpace(phoneCode) || phoneCode.Trim() == "+";
}

public sealed class CountryAdminHandlers :
    IRequestHandler<ListCountriesQuery, PagedResult<CountryDto>>,
    IRequestHandler<UpsertCountryCommand, CountryDto>,
    IRequestHandler<DeleteCountryCommand, LookupDeleteOutcome>
{
    private readonly IApplicationDbContext _db;
    private readonly AdminLookupService _lookups;

    public CountryAdminHandlers(IApplicationDbContext db, AdminLookupService lookups)
    {
        _db = db;
        _lookups = lookups;
    }

    public Task<PagedResult<CountryDto>> Handle(
        ListCountriesQuery request,
        CancellationToken cancellationToken) =>
        // Each country carries its mapped currency codes, so the admin list shows them without a
        // follow-up request per row. One collection Include, a handful of currencies each — no
        // cartesian blow-up.
        _lookups.ListAsync(
            _db.Countries
                .Include(c => c.CountryCurrencies)
                .ThenInclude(cc => cc.Currency),
            request,
            c => CountryDto.From(
                c,
                _lookups.Language,
                c.CountryCurrencies
                    .Where(cc => cc.Currency != null)
                    .Select(cc => cc.Currency!.Code)),
            cancellationToken);

    public async Task<CountryDto> Handle(UpsertCountryCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var code = request.Code.ToUpperInvariant();

        await _lookups.EnsureUniqueAsync(
            _db.Countries,
            c => c.Code == code && (request.Id == null || c.Id != request.Id),
            "country.duplicate_code",
            $"A country with code '{code}' already exists.",
            cancellationToken);

        Country country;
        if (request.Id is { } id)
        {
            country = await _lookups.RequireAsync(_db.Countries, id, cancellationToken);
        }
        else
        {
            country = new Country { Code = code, NameAr = request.NameAr, NameEn = request.NameEn };
            _db.Countries.Add(country);
        }

        country.Code = code;
        // A blank or bare-"+" prefix is stored as empty rather than rejected, so the column keeps
        // one representation of "no calling prefix".
        country.PhoneCode = UpsertCountryCommandValidator.IsBlank(request.PhoneCode)
            ? string.Empty
            : request.PhoneCode.Trim();
        country.NameAr = request.NameAr.Trim();
        country.NameEn = request.NameEn.Trim();
        country.IsActive = request.IsActive;

        await _lookups.SaveAsync(cancellationToken);
        await _lookups.AuditAsync(
            request.Id is null ? "Country.Created" : "Country.Updated",
            nameof(Country),
            country.Id,
            new { country.Code, country.PhoneCode, country.NameEn },
            cancellationToken);

        return CountryDto.From(country, _lookups.Language);
    }

    public Task<LookupDeleteOutcome> Handle(
        DeleteCountryCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _lookups.DeleteOrDeactivateAsync(
            _db.Countries,
            request.Id,
            // A country in use by an order, a transaction type or an authority is retired, not removed.
            async ct => await _db.Orders.AnyAsync(o => o.VerificationCountryId == request.Id, ct)
                        || await _db.TransactionTypeCountries.AnyAsync(t => t.CountryId == request.Id, ct)
                        || await _db.VerificationAuthorities.AnyAsync(a => a.CountryId == request.Id, ct),
            "Country",
            cancellationToken);
    }
}

// ---------------------------------------------------------------- Currencies

public sealed record ListCurrenciesQuery : PagedQuery, IRequest<PagedResult<CurrencyDto>>;

public sealed record UpsertCurrencyCommand(
    Guid? Id,
    string Code,
    string Symbol,
    string NameAr,
    string NameEn,
    bool IsActive,
    IReadOnlyList<Guid> CountryIds) : IRequest<CurrencyDto>;

public sealed record DeleteCurrencyCommand(Guid Id) : IRequest<LookupDeleteOutcome>;

public sealed class UpsertCurrencyCommandValidator : AbstractValidator<UpsertCurrencyCommand>
{
    public UpsertCurrencyCommandValidator()
    {
        RuleFor(c => c.Code).NotEmpty().Length(3).Matches("^[A-Za-z]{3}$")
            .WithMessage("Currency code must be a three-letter ISO 4217 code.");
        RuleFor(c => c.Symbol).NotEmpty().MaximumLength(10);
        RuleFor(c => c.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(c => c.NameEn).NotEmpty().MaximumLength(200);
        RuleFor(c => c.CountryIds).NotNull();
    }
}

public sealed class CurrencyAdminHandlers :
    IRequestHandler<ListCurrenciesQuery, PagedResult<CurrencyDto>>,
    IRequestHandler<UpsertCurrencyCommand, CurrencyDto>,
    IRequestHandler<DeleteCurrencyCommand, LookupDeleteOutcome>
{
    private readonly IApplicationDbContext _db;
    private readonly AdminLookupService _lookups;

    public CurrencyAdminHandlers(IApplicationDbContext db, AdminLookupService lookups)
    {
        _db = db;
        _lookups = lookups;
    }

    public Task<PagedResult<CurrencyDto>> Handle(
        ListCurrenciesQuery request,
        CancellationToken cancellationToken) =>
        _lookups.ListAsync(
            _db.Currencies.Include(c => c.CountryCurrencies),
            request,
            c => CurrencyDto.From(
                c,
                _lookups.Language,
                c.CountryCurrencies.Select(link => link.CountryId)),
            cancellationToken);

    public async Task<CurrencyDto> Handle(
        UpsertCurrencyCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var code = request.Code.ToUpperInvariant();

        await _lookups.EnsureUniqueAsync(
            _db.Currencies,
            c => c.Code == code && (request.Id == null || c.Id != request.Id),
            "currency.duplicate_code",
            $"A currency with code '{code}' already exists.",
            cancellationToken);

        Currency currency;
        if (request.Id is { } id)
        {
            currency = await _db.Currencies
                .Include(c => c.CountryCurrencies)
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(Currency), id);
        }
        else
        {
            currency = new Currency
            {
                Code = code,
                Symbol = request.Symbol,
                NameAr = request.NameAr,
                NameEn = request.NameEn,
            };
            _db.Currencies.Add(currency);
        }

        currency.Code = code;
        currency.Symbol = request.Symbol.Trim();
        currency.NameAr = request.NameAr.Trim();
        currency.NameEn = request.NameEn.Trim();
        currency.IsActive = request.IsActive;

        var requestedCountries = request.CountryIds.Distinct().ToList();
        var knownCountryCount = await _db.Countries
            .CountAsync(c => requestedCountries.Contains(c.Id), cancellationToken);
        if (knownCountryCount != requestedCountries.Count)
        {
            throw new NotFoundException("One or more of the countries supplied do not exist.");
        }

        // A mapping already used by a wallet cannot be removed, because the wallet's denomination
        // and order country must remain a valid pair.
        var removedCountryIds = currency.CountryCurrencies
            .Where(link => !requestedCountries.Contains(link.CountryId))
            .Select(link => link.CountryId)
            .ToList();
        if (removedCountryIds.Count > 0)
        {
            var mappingInUse = await _db.Wallets.AnyAsync(
                wallet => wallet.CurrencyId == currency.Id
                          && wallet.Order != null
                          && wallet.Order.VerificationCountryId != null
                          && removedCountryIds.Contains(wallet.Order.VerificationCountryId.Value),
                cancellationToken);
            if (mappingInUse)
            {
                throw new ConflictException(
                    "country_currency.in_use",
                    "A country mapping used by an existing wallet cannot be removed.");
            }
        }

        foreach (var link in currency.CountryCurrencies
                     .Where(link => !requestedCountries.Contains(link.CountryId))
                     .ToList())
        {
            _db.CountryCurrencies.Remove(link);
        }

        var existingCountryIds = currency.CountryCurrencies.Select(link => link.CountryId).ToHashSet();
        foreach (var countryId in requestedCountries.Where(id => !existingCountryIds.Contains(id)))
        {
            currency.CountryCurrencies.Add(new CountryCurrency
            {
                CountryId = countryId,
                CurrencyId = currency.Id,
            });
        }

        await _lookups.SaveAsync(cancellationToken);
        await _lookups.AuditAsync(
            request.Id is null ? "Currency.Created" : "Currency.Updated",
            nameof(Currency),
            currency.Id,
            new { currency.Code, CountryIds = requestedCountries },
            cancellationToken);

        return CurrencyDto.From(currency, _lookups.Language, requestedCountries);
    }

    public Task<LookupDeleteOutcome> Handle(
        DeleteCurrencyCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _lookups.DeleteOrDeactivateAsync(
            _db.Currencies,
            request.Id,
            // A currency backing a wallet can never be removed — the ledger is denominated in it.
            async ct => await _db.Wallets.AnyAsync(w => w.CurrencyId == request.Id, ct)
                        || await _db.CountryCurrencies.AnyAsync(cc => cc.CurrencyId == request.Id, ct),
            "Currency",
            cancellationToken);
    }
}

// ------------------------------------------------- Country ↔ Currency mapping

/// <summary>
/// The currencies mapped to a country, for the admin mapping editor. The applicant-facing
/// equivalent lives on the Applicant policy and cannot be called with an admin token.
/// </summary>
public sealed record GetCountryCurrenciesForAdminQuery(Guid CountryId)
    : IRequest<IReadOnlyList<CurrencyDto>>;

public sealed class GetCountryCurrenciesForAdminQueryHandler
    : IRequestHandler<GetCountryCurrenciesForAdminQuery, IReadOnlyList<CurrencyDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetCountryCurrenciesForAdminQueryHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<CurrencyDto>> Handle(
        GetCountryCurrenciesForAdminQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var exists = await _db.Countries
            .AsNoTracking()
            .AnyAsync(c => c.Id == request.CountryId, cancellationToken);

        if (!exists)
        {
            throw new NotFoundException("Country", request.CountryId);
        }

        var currencies = await _db.CountryCurrencies
            .AsNoTracking()
            .Where(cc => cc.CountryId == request.CountryId)
            .Select(cc => cc.Currency!)
            .OrderBy(c => c.Code)
            .ToListAsync(cancellationToken);

        return currencies.Select(c => CurrencyDto.From(c, _currentUser.LanguageCode)).ToList();
    }
}

/// <summary>Replaces the whole currency list for a country in one call.</summary>
public sealed record SetCountryCurrenciesCommand(Guid CountryId, IReadOnlyList<Guid> CurrencyIds)
    : IRequest<IReadOnlyList<CurrencyDto>>;

public sealed class SetCountryCurrenciesCommandValidator
    : AbstractValidator<SetCountryCurrenciesCommand>
{
    public SetCountryCurrenciesCommandValidator()
    {
        RuleFor(c => c.CountryId).NotEmpty();
        RuleFor(c => c.CurrencyIds).NotNull();
    }
}

public sealed class SetCountryCurrenciesCommandHandler
    : IRequestHandler<SetCountryCurrenciesCommand, IReadOnlyList<CurrencyDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly AdminLookupService _lookups;

    public SetCountryCurrenciesCommandHandler(IApplicationDbContext db, AdminLookupService lookups)
    {
        _db = db;
        _lookups = lookups;
    }

    public async Task<IReadOnlyList<CurrencyDto>> Handle(
        SetCountryCurrenciesCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        _ = await _lookups.RequireAsync(_db.Countries, request.CountryId, cancellationToken);

        var requested = request.CurrencyIds.Distinct().ToList();

        var known = await _db.Currencies
            .Where(c => requested.Contains(c.Id))
            .ToListAsync(cancellationToken);

        if (known.Count != requested.Count)
        {
            throw new NotFoundException("One or more of the currencies supplied do not exist.");
        }

        var existing = await _db.CountryCurrencies
            .Where(cc => cc.CountryId == request.CountryId)
            .ToListAsync(cancellationToken);

        // Removing a currency an order already settled on would strand that wallet, so a mapping
        // still backing a wallet is kept regardless of what the admin submitted.
        var walletCurrencies = await _db.Wallets
            .Where(w => w.Order!.VerificationCountryId == request.CountryId)
            .Select(w => w.CurrencyId)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var mapping in existing.Where(m => !requested.Contains(m.CurrencyId)))
        {
            if (walletCurrencies.Contains(mapping.CurrencyId))
            {
                throw new ConflictException(
                    "country_currency.in_use",
                    "A currency that an existing wallet is denominated in cannot be unmapped.");
            }

            _db.CountryCurrencies.Remove(mapping);
        }

        var existingIds = existing.Select(m => m.CurrencyId).ToHashSet();
        foreach (var currencyId in requested.Where(id => !existingIds.Contains(id)))
        {
            _db.CountryCurrencies.Add(new CountryCurrency
            {
                CountryId = request.CountryId,
                CurrencyId = currencyId,
            });
        }

        await _lookups.SaveAsync(cancellationToken);
        await _lookups.AuditAsync(
            "CountryCurrencies.Updated",
            nameof(Country),
            request.CountryId,
            new { CurrencyIds = requested },
            cancellationToken);

        return known
            .OrderBy(c => c.Code)
            .Select(c => CurrencyDto.From(c, _lookups.Language))
            .ToList();
    }
}
