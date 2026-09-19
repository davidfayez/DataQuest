using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Wallets.Queries;

/// <summary>What paying the given applications would cost from each of the order's balances.</summary>
public sealed record GetPaymentQuoteQuery(IReadOnlyList<Guid> ApplicationIds) : IRequest<PaymentQuoteDto>;

/// <param name="Total">Null when a selected service is not sold in this currency.</param>
/// <param name="IsCurrent">The applications are already priced in this currency.</param>
/// <param name="CanPay">Priced in it and the balance covers it.</param>
public sealed record PaymentQuoteOptionDto(
    Guid CurrencyId,
    string CurrencyCode,
    string CurrencySymbol,
    bool IsMain,
    decimal Balance,
    decimal? Total,
    bool IsCurrent,
    bool CanPay);

public sealed record PaymentQuoteDto(IReadOnlyList<PaymentQuoteOptionDto> Options);

public sealed class GetPaymentQuoteQueryHandler : IRequestHandler<GetPaymentQuoteQuery, PaymentQuoteDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly WalletBook _wallets;

    public GetPaymentQuoteQueryHandler(IApplicationDbContext db, ICurrentUser currentUser, WalletBook wallets)
    {
        _db = db;
        _currentUser = currentUser;
        _wallets = wallets;
    }

    public async Task<PaymentQuoteDto> Handle(GetPaymentQuoteQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var orderId = _currentUser.OrderId
            ?? throw new ForbiddenAccessException("This endpoint is only available to applicants.");

        var ids = request.ApplicationIds.Distinct().ToList();
        if (ids.Count == 0) return new PaymentQuoteDto([]);

        var mainCurrencyId = await _db.Orders
            .Where(o => o.Id == orderId)
            .Select(o => o.CurrencyId)
            .FirstOrDefaultAsync(cancellationToken);
        if (mainCurrencyId is null) return new PaymentQuoteDto([]);

        var applications = await _db.Applications
            .AsNoTracking()
            .Include(a => a.Services)
            .ThenInclude(s => s.ServiceType)
            .ThenInclude(t => t!.Costs)
            .AsSplitQuery()
            .Where(a => ids.Contains(a.Id) && a.OrderId == orderId)
            .ToListAsync(cancellationToken);

        // Ids from another order are reported like ids that do not exist, so they cannot be probed.
        if (applications.Count != ids.Count)
        {
            throw new NotFoundException("One or more of the applications selected could not be found on this order.");
        }

        var balances = await _wallets.BalancesAsync(orderId, cancellationToken);

        var options = balances
            .Select(balance =>
            {
                decimal? total = 0m;
                foreach (var application in applications)
                {
                    var one = CurrencyPricing.TotalIn(application, balance.CurrencyId, mainCurrencyId.Value);
                    if (one is null) { total = null; break; }
                    total += one;
                }

                var isCurrent = applications.All(a => (a.CurrencyId ?? mainCurrencyId) == balance.CurrencyId);

                return new PaymentQuoteOptionDto(
                    balance.CurrencyId,
                    balance.CurrencyCode,
                    balance.CurrencySymbol,
                    balance.IsMain,
                    balance.Balance,
                    total,
                    isCurrent,
                    total is > 0 && balance.Balance >= total);
            })
            .ToList();

        return new PaymentQuoteDto(options);
    }
}
