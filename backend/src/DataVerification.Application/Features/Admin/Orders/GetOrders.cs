using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Common.Models;
using DataVerification.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Admin.Orders;

/// <summary>Admin order search, by order number or email, with optional country/currency filters.</summary>
public sealed record GetOrdersQuery : PagedQuery, IRequest<PagedResult<AdminOrderListItemDto>>
{
    /// <summary>When set, only orders whose verification country is one of these ids.</summary>
    public Guid[]? CountryIds { get; init; }

    /// <summary>When set, only orders whose wallet currency is one of these ids.</summary>
    public Guid[]? CurrencyIds { get; init; }
}

public sealed record AdminOrderListItemDto(
    Guid Id,
    string OrderNumber,
    string Email,
    string LanguageCode,
    string? CountryName,
    string? CurrencyCode,
    decimal WalletBalance,
    int ApplicationCount,
    DateTime CreatedAtUtc,
    DateTime? LastLoginAtUtc);

public sealed class GetOrdersQueryHandler
    : IRequestHandler<GetOrdersQuery, PagedResult<AdminOrderListItemDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetOrdersQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public Task<PagedResult<AdminOrderListItemDto>> Handle(
        GetOrdersQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var language = _currentUser.LanguageCode;

        var query = _db.Orders
            .AsNoTracking()
            .Include(o => o.VerificationCountry)
            .Include(o => o.Currency)
            .Include(o => o.Wallet)
            .Include(o => o.Applications)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(o =>
                EF.Functions.Like(o.OrderNumber, $"%{term}%") || EF.Functions.Like(o.Email, $"%{term}%"));
        }

        if (request.CountryIds is { Length: > 0 } countryIds)
        {
            query = query.Where(o =>
                o.VerificationCountryId != null && countryIds.Contains(o.VerificationCountryId.Value));
        }

        if (request.CurrencyIds is { Length: > 0 } currencyIds)
        {
            query = query.Where(o =>
                o.CurrencyId != null && currencyIds.Contains(o.CurrencyId.Value));
        }

        query = query.OrderByDescending(o => o.CreatedAtUtc);

        return query.ToPagedResultAsync(
            request,
            order => new AdminOrderListItemDto(
                order.Id,
                order.OrderNumber,
                order.Email,
                order.LanguageCode,
                order.VerificationCountry?.ResolveName(language),
                order.Currency?.Code,
                order.Wallet?.Balance ?? 0m,
                order.Applications.Count,
                order.CreatedAtUtc,
                order.LastLoginAtUtc),
            cancellationToken);
    }
}

/// <summary>One order with its applications, for the admin order detail screen.</summary>
public sealed record GetOrderDetailsQuery(Guid OrderId) : IRequest<AdminOrderDetailsDto>;

public sealed record AdminOrderDetailsDto(
    Guid Id,
    string OrderNumber,
    string Email,
    string LanguageCode,
    string? CountryName,
    string? CurrencyCode,
    decimal WalletBalance,
    DateTime CreatedAtUtc,
    DateTime? LastLoginAtUtc,
    IReadOnlyList<AdminOrderApplicationDto> Applications);

/// <summary>
/// One application on the order. <c>CanRefund</c> mirrors the domain rule, so the admin UI offers
/// the refund action only where it is actually legal.
/// </summary>
public sealed record AdminOrderApplicationDto(
    Guid Id,
    string ApplicationNumber,
    string AddressedTo,
    ApplicationStatus Status,
    string StatusName,
    bool IsPaid,
    decimal TotalCost,
    bool CanRefund,
    DateTime CreatedAtUtc);

public sealed class GetOrderDetailsQueryHandler
    : IRequestHandler<GetOrderDetailsQuery, AdminOrderDetailsDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetOrderDetailsQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<AdminOrderDetailsDto> Handle(
        GetOrderDetailsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var order = await _db.Orders
            .AsNoTracking()
            .Include(o => o.VerificationCountry)
            .Include(o => o.Currency)
            .Include(o => o.Wallet)
            .Include(o => o.Applications)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken)
            ?? throw new NotFoundException("Order", request.OrderId);

        return new AdminOrderDetailsDto(
            order.Id,
            order.OrderNumber,
            order.Email,
            order.LanguageCode,
            order.VerificationCountry?.ResolveName(_currentUser.LanguageCode),
            order.Currency?.Code,
            order.Wallet?.Balance ?? 0m,
            order.CreatedAtUtc,
            order.LastLoginAtUtc,
            order.Applications
                .OrderByDescending(a => a.CreatedAtUtc)
                .Select(a => new AdminOrderApplicationDto(
                    a.Id,
                    a.ApplicationNumber,
                    a.AddressedTo,
                    a.Status,
                    a.Status.ToString(),
                    a.IsPaid,
                    a.TotalCost,
                    a.CanRefund(),
                    a.CreatedAtUtc))
                .ToList());
    }
}
