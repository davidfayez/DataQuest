using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Lookups.Queries;

/// <summary>Active countries an applicant may pick during order setup.</summary>
public sealed record GetCountriesQuery : IRequest<IReadOnlyList<CountryDto>>;

/// <summary>The currencies a country exposes. Drives the currency select on the setup screen.</summary>
public sealed record GetCountryCurrenciesQuery(Guid CountryId) : IRequest<IReadOnlyList<CurrencyDto>>;

/// <summary>
/// The addressees offered on a new application. A suggestion list rather than a constraint — the
/// applicant may type one that is not here — so it needs no order and no country scope.
/// </summary>
public sealed record GetAddresseesQuery : IRequest<IReadOnlyList<AddresseeDto>>;

/// <summary>Level 1 of the cascade, scoped to the caller's order country.</summary>
public sealed record GetTransactionTypesQuery : IRequest<IReadOnlyList<TransactionTypeDto>>;

/// <summary>Level 2 — the sub-types of one transaction type.</summary>
public sealed record GetSubTransactionTypesQuery(Guid TransactionTypeId)
    : IRequest<IReadOnlyList<SubTransactionTypeDto>>;

/// <summary>Level 3 — authorities mapped to a sub-type <em>and</em> present in the order's country.</summary>
public sealed record GetAuthoritiesQuery(Guid SubTransactionTypeId)
    : IRequest<IReadOnlyList<VerificationAuthorityDto>>;

/// <summary>Level 4 — the priced services an authority offers for a sub-type, with required files.</summary>
public sealed record GetServiceTypesQuery(Guid VerificationAuthorityId, Guid SubTransactionTypeId)
    : IRequest<IReadOnlyList<ServiceTypeDto>>;

/// <summary>
/// Every applicant-facing cascade query lives here so the country scoping rule is applied in one
/// place. A parent that belongs to a different country is reported as not found rather than
/// returning an empty list, which keeps a tampered id from looking like a valid-but-empty branch.
/// </summary>
public sealed class CascadeQueryHandlers :
    IRequestHandler<GetCountriesQuery, IReadOnlyList<CountryDto>>,
    IRequestHandler<GetCountryCurrenciesQuery, IReadOnlyList<CurrencyDto>>,
    IRequestHandler<GetAddresseesQuery, IReadOnlyList<AddresseeDto>>,
    IRequestHandler<GetTransactionTypesQuery, IReadOnlyList<TransactionTypeDto>>,
    IRequestHandler<GetSubTransactionTypesQuery, IReadOnlyList<SubTransactionTypeDto>>,
    IRequestHandler<GetAuthoritiesQuery, IReadOnlyList<VerificationAuthorityDto>>,
    IRequestHandler<GetServiceTypesQuery, IReadOnlyList<ServiceTypeDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public CascadeQueryHandlers(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    private string Language => _currentUser.LanguageCode;

    public async Task<IReadOnlyList<CountryDto>> Handle(
        GetCountriesQuery request,
        CancellationToken cancellationToken)
    {
        // Only countries an order can actually be set up in. Choosing the country fixes the
        // wallet's currency, so one with no active currency behind it is a dead end: the applicant
        // picks it, the currency list comes back empty, and there is nothing to do but go back.
        var countries = await _db.Countries
            .AsNoTracking()
            .Where(c => c.IsActive && c.CountryCurrencies.Any(cc => cc.Currency!.IsActive))
            .OrderBy(c => c.NameEn)
            .ToListAsync(cancellationToken);

        return countries.Select(c => CountryDto.From(c, Language)).ToList();
    }

    public async Task<IReadOnlyList<AddresseeDto>> Handle(
        GetAddresseesQuery request,
        CancellationToken cancellationToken)
    {
        var addressees = await _db.Addressees
            .AsNoTracking()
            .Where(a => a.IsActive)
            .OrderBy(a => a.SortOrder)
            .ThenBy(a => a.NameEn)
            .ToListAsync(cancellationToken);

        return addressees.Select(a => AddresseeDto.From(a, Language)).ToList();
    }

    public async Task<IReadOnlyList<CurrencyDto>> Handle(
        GetCountryCurrenciesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var countryExists = await _db.Countries
            .AsNoTracking()
            .AnyAsync(c => c.Id == request.CountryId && c.IsActive, cancellationToken);

        if (!countryExists)
        {
            throw new NotFoundException("Country", request.CountryId);
        }

        var links = await _db.CountryCurrencies
            .AsNoTracking()
            .Include(cc => cc.Currency)
            .Where(cc => cc.CountryId == request.CountryId && cc.Currency!.IsActive)
            .OrderBy(cc => cc.Currency!.Code)
            .ToListAsync(cancellationToken);

        return links.Select(cc => CurrencyDto.From(cc.Currency!, Language, isDefault: cc.IsDefault)).ToList();
    }


    public async Task<IReadOnlyList<TransactionTypeDto>> Handle(
        GetTransactionTypesQuery request,
        CancellationToken cancellationToken)
    {
        var countryId = await RequireOrderCountryAsync(cancellationToken);

        var types = await _db.TransactionTypes
            .AsNoTracking()
            .Include(t => t.CountryLinks)
            .Where(t => t.IsActive && t.CountryLinks.Any(link => link.CountryId == countryId))
            .OrderBy(t => t.NameEn)
            .ToListAsync(cancellationToken);

        return types.Select(t => TransactionTypeDto.From(t, Language)).ToList();
    }

    public async Task<IReadOnlyList<SubTransactionTypeDto>> Handle(
        GetSubTransactionTypesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var countryId = await RequireOrderCountryAsync(cancellationToken);

        // The parent must belong to the caller's country, otherwise the cascade could be walked
        // sideways into another country's data by supplying a foreign id.
        var parentInScope = await _db.TransactionTypes
            .AsNoTracking()
            .AnyAsync(
                t => t.Id == request.TransactionTypeId
                     && t.IsActive
                     && t.CountryLinks.Any(link => link.CountryId == countryId),
                cancellationToken);

        if (!parentInScope)
        {
            throw new NotFoundException("TransactionType", request.TransactionTypeId);
        }

        var subTypes = await _db.SubTransactionTypes
            .AsNoTracking()
            .Where(s => s.TransactionTypeId == request.TransactionTypeId && s.IsActive)
            .OrderBy(s => s.NameEn)
            .ToListAsync(cancellationToken);

        return subTypes.Select(s => SubTransactionTypeDto.From(s, Language)).ToList();
    }

    public async Task<IReadOnlyList<VerificationAuthorityDto>> Handle(
        GetAuthoritiesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var countryId = await RequireOrderCountryAsync(cancellationToken);

        var subTypeInScope = await _db.SubTransactionTypes
            .AsNoTracking()
            .AnyAsync(
                s => s.Id == request.SubTransactionTypeId
                     && s.IsActive
                     && s.TransactionType!.CountryLinks.Any(link => link.CountryId == countryId),
                cancellationToken);

        if (!subTypeInScope)
        {
            throw new NotFoundException("SubTransactionType", request.SubTransactionTypeId);
        }

        // Both conditions matter: the authority must be mapped to this sub-type and operate in
        // the order's country. Queried from the authority side because EF Core cannot apply an
        // Include after a projection.
        var authorities = await _db.VerificationAuthorities
            .AsNoTracking()
            .Include(a => a.SubTransactionTypeLinks)
            .Where(a => a.IsActive
                        && a.CountryId == countryId
                        && a.SubTransactionTypeLinks.Any(link =>
                            link.SubTransactionTypeId == request.SubTransactionTypeId))
            .OrderBy(a => a.NameEn)
            .ToListAsync(cancellationToken);

        return authorities.Select(a => VerificationAuthorityDto.From(a, Language)).ToList();
    }

    public async Task<IReadOnlyList<ServiceTypeDto>> Handle(
        GetServiceTypesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (countryId, currencyId) = await RequireOrderSetupAsync(cancellationToken);

        var authorityInScope = await _db.VerificationAuthorities
            .AsNoTracking()
            .AnyAsync(
                a => a.Id == request.VerificationAuthorityId && a.CountryId == countryId && a.IsActive,
                cancellationToken);

        if (!authorityInScope)
        {
            throw new NotFoundException("VerificationAuthority", request.VerificationAuthorityId);
        }

        var subTypeInScope = await _db.SubTransactionTypes
            .AsNoTracking()
            .AnyAsync(s => s.Id == request.SubTransactionTypeId && s.IsActive, cancellationToken);
        if (!subTypeInScope)
        {
            throw new NotFoundException("SubTransactionType", request.SubTransactionTypeId);
        }

        // Split, for the same reason as the admin service-types list: documents, fields, options,
        // formats, languages and prices joined into one result set made SQL Server hold this
        // query for a large memory grant, and the wizard's service step took 25 seconds.
        var serviceTypes = await _db.ServiceTypes
            .AsNoTracking()
            .AsSplitQuery()
            .Include(s => s.RequiredFiles).ThenInclude(f => f.Fields).ThenInclude(f => f.Options)
            .Include(s => s.RequiredFiles).ThenInclude(f => f.AllowedFileTypes)
            .Include(s => s.RequiredFiles).ThenInclude(f => f.Samples)
            .Include(s => s.OutputLanguages)
            .Include(s => s.Costs)
            .ThenInclude(c => c.Currency)
            .Where(s =>
                s.VerificationAuthorityId == request.VerificationAuthorityId
                && s.SubTransactionTypeId == request.SubTransactionTypeId
                && s.IsActive
                && s.Costs.Any(c => c.CurrencyId == currencyId && c.IsActive))
            .OrderBy(s => s.NameEn)
            .ToListAsync(cancellationToken);

        return serviceTypes
            .Select(s => ServiceTypeDto.From(s, Language, currencyId, forApplicant: true))
            .ToList();
    }

    /// <summary>
    /// Resolves the verification country locked onto the caller's order. Every level of the
    /// cascade is filtered by it, so an applicant only ever sees their own country's lookups.
    /// </summary>
    private async Task<Guid> RequireOrderCountryAsync(CancellationToken cancellationToken)
    {
        var (countryId, _) = await RequireOrderSetupAsync(cancellationToken);
        return countryId;
    }

    private async Task<(Guid CountryId, Guid CurrencyId)> RequireOrderSetupAsync(
        CancellationToken cancellationToken)
    {
        var orderId = _currentUser.OrderId
            ?? throw new ForbiddenAccessException("This endpoint is only available to applicants.");

        var order = await _db.Orders
            .AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(o => new { o.VerificationCountryId, o.CurrencyId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Order", orderId);

        if (order.VerificationCountryId is null || order.CurrencyId is null)
        {
            throw new ConflictException(
                "order.setup_incomplete",
                "Choose a verification country and currency before browsing the lookups.");
        }

        return (order.VerificationCountryId.Value, order.CurrencyId.Value);
    }
}
