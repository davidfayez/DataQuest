using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Wallets;

/// <summary>One of an order's balances, as the wallet page lists them.</summary>
/// <param name="IsMain">The order's main currency — the one applications are first priced in.</param>
/// <param name="WalletId">Null until money has moved in this currency.</param>
public sealed record WalletBalanceDto(
    Guid CurrencyId,
    string CurrencyCode,
    string CurrencySymbol,
    string CurrencyName,
    decimal Balance,
    bool IsMain,
    Guid? WalletId);

/// <summary>
/// Finds an order's wallet in a currency, opening it the first time money moves in that currency.
///
/// An order holds one wallet per currency its country offers. Every money flow — deposits,
/// withdrawals, payments, refunds, admin credits — goes through here, so they all agree on which
/// currencies an order may hold and on what "no currency given" means: the order's main currency.
/// </summary>
public sealed class WalletBook
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public WalletBook(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    /// <summary>
    /// The order's wallet in <paramref name="currencyId"/> (or its main currency), opened if needed.
    /// A new wallet is added to the context; the caller's save persists it with whatever else moved.
    /// </summary>
    public async Task<Wallet> OpenAsync(Guid orderId, Guid? currencyId, CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(orderId, tracked: true, cancellationToken);

        if (!order.IsSetupComplete)
        {
            throw new ConflictException(
                "order.setup_incomplete",
                "Complete order setup before using the wallet.");
        }

        var id = currencyId ?? order.CurrencyId!.Value;
        var existing = order.WalletFor(id);
        if (existing is not null) return existing;

        var currency = await _db.Currencies
            .FirstOrDefaultAsync(c => c.Id == id && c.IsActive, cancellationToken)
            ?? throw new NotFoundException("Currency", id);

        // Throws wallet.currency_not_available for a currency the order's country does not offer.
        var wallet = order.OpenWallet(currency);
        _db.Wallets.Add(wallet);
        return wallet;
    }

    /// <summary>
    /// The order's wallet in a currency, without opening one. Null when no money has moved in it —
    /// which, for a statement, simply means a zero balance.
    /// </summary>
    public async Task<Wallet?> FindAsync(Guid orderId, Guid? currencyId, CancellationToken cancellationToken)
    {
        var mainCurrencyId = currencyId ?? await _db.Orders
            .Where(o => o.Id == orderId)
            .Select(o => o.CurrencyId)
            .FirstOrDefaultAsync(cancellationToken);

        return await _db.Wallets
            .Include(w => w.Currency)
            .FirstOrDefaultAsync(w => w.OrderId == orderId && w.CurrencyId == mainCurrencyId, cancellationToken);
    }

    /// <summary>
    /// Every balance the order can hold: each currency its country offers, main first, plus any
    /// currency it still holds money in that the country has since stopped offering.
    /// </summary>
    public async Task<IReadOnlyList<WalletBalanceDto>> BalancesAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(orderId, tracked: false, cancellationToken);
        if (!order.IsSetupComplete || order.VerificationCountry is null) return [];

        var offered = order.VerificationCountry.CountryCurrencies
            .Where(link => link.Currency is { IsActive: true })
            .Select(link => link.Currency!)
            .ToList();

        var held = order.Wallets
            .Where(wallet => wallet.Currency is not null && offered.All(c => c.Id != wallet.CurrencyId))
            .Select(wallet => wallet.Currency!);

        return offered
            .Concat(held)
            .OrderByDescending(currency => currency.Id == order.CurrencyId)
            .ThenBy(currency => currency.Code, StringComparer.Ordinal)
            .Select(currency =>
            {
                var wallet = order.WalletFor(currency.Id);
                return new WalletBalanceDto(
                    currency.Id,
                    currency.Code,
                    currency.Symbol,
                    currency.ResolveName(_currentUser.LanguageCode),
                    wallet?.Balance ?? 0m,
                    currency.Id == order.CurrencyId,
                    wallet?.Id);
            })
            .ToList();
    }

    private async Task<Order> LoadOrderAsync(Guid orderId, bool tracked, CancellationToken cancellationToken)
    {
        var query = _db.Orders
            .Include(o => o.VerificationCountry)
            .ThenInclude(c => c!.CountryCurrencies)
            .ThenInclude(link => link.Currency)
            .Include(o => o.Wallets)
            .ThenInclude(w => w.Currency)
            .AsSplitQuery();

        return await (tracked ? query : query.AsNoTracking())
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new NotFoundException("Order", orderId);
    }
}
