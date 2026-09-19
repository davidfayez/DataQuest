using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Common.Models;
using DataVerification.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DataVerification.Application.Features.Wallets.Queries;

/// <summary>Balances plus a page of one currency's ledger, for the caller's own order.</summary>
public sealed record GetMyWalletQuery : PagedQuery, IRequest<WalletStatementDto>
{
    /// <summary>Narrows the ledger to one kind of entry. Null returns everything.</summary>
    public WalletTransactionType? Type { get; init; }

    /// <summary>Whose ledger to show; the order's main currency when omitted.</summary>
    public Guid? CurrencyId { get; init; }
}

/// <summary>Same statement, for an admin looking at a specific order.</summary>
public sealed record GetOrderWalletQuery : PagedQuery, IRequest<WalletStatementDto>
{
    public Guid OrderId { get; init; }

    public WalletTransactionType? Type { get; init; }

    public Guid? CurrencyId { get; init; }
}

/// <param name="PendingRequests">
/// Deposits and withdrawals still awaiting a decision. Small by construction — one per direction —
/// so the statement carries them rather than making the wallet page issue a second call.
/// </param>
/// <param name="Wallet">The balance whose ledger is shown.</param>
/// <param name="Balances">Every balance the order can hold, main currency first.</param>
public sealed record WalletStatementDto(
    WalletSummaryDto Wallet,
    PagedResult<WalletTransactionDto> Ledger,
    WalletFeaturesDto Features,
    IReadOnlyList<WalletRequestDto> PendingRequests,
    IReadOnlyList<WalletBalanceDto> Balances);

public sealed class WalletQueryHandlers :
    IRequestHandler<GetMyWalletQuery, WalletStatementDto>,
    IRequestHandler<GetOrderWalletQuery, WalletStatementDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly WalletOptions _options;
    private readonly WalletBook _wallets;

    public WalletQueryHandlers(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IOptions<WalletOptions> options,
        WalletBook wallets)
    {
        ArgumentNullException.ThrowIfNull(options);

        _db = db;
        _currentUser = currentUser;
        _options = options.Value;
        _wallets = wallets;
    }

    public Task<WalletStatementDto> Handle(GetMyWalletQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var orderId = _currentUser.OrderId
            ?? throw new ForbiddenAccessException("This endpoint is only available to applicants.");

        return LoadAsync(orderId, request, request.Type, request.CurrencyId, cancellationToken);
    }

    public Task<WalletStatementDto> Handle(
        GetOrderWalletQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return LoadAsync(request.OrderId, request, request.Type, request.CurrencyId, cancellationToken);
    }

    private async Task<WalletStatementDto> LoadAsync(
        Guid orderId,
        PagedQuery paging,
        WalletTransactionType? type,
        Guid? currencyId,
        CancellationToken cancellationToken)
    {
        var balances = await _wallets.BalancesAsync(orderId, cancellationToken);
        if (balances.Count == 0)
        {
            throw new NotFoundException("Wallet for order", orderId);
        }

        // The balance being looked at: the one asked for, else the main one. A currency with no
        // money moved yet has no wallet row, and simply shows an empty ledger.
        var shown = balances.FirstOrDefault(b => b.CurrencyId == currencyId)
            ?? balances.FirstOrDefault(b => b.IsMain)
            ?? balances[0];
        var walletId = shown.WalletId ?? Guid.Empty;

        var ledgerQuery = _db.WalletTransactions
            .AsNoTracking()
            .Where(t => t.WalletId == walletId)
            .Where(t => type == null || t.Type == type)
            .OrderByDescending(t => t.CreatedAtUtc);

        var page = await ledgerQuery.ToPagedResultAsync(paging, t => t, cancellationToken);

        // Ledger rows store application ids; the statement shows the human-facing numbers, so the
        // referenced applications are resolved in one query rather than per row.
        var referencedIds = page.Items
            .SelectMany(t => t.ReferenceApplicationIds)
            .Distinct()
            .ToList();

        var applicationNumbers = await _db.Applications
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => referencedIds.Contains(a.Id))
            .Select(a => new { a.Id, a.ApplicationNumber })
            .ToDictionaryAsync(a => a.Id, a => a.ApplicationNumber, cancellationToken);

        var ledger = new PagedResult<WalletTransactionDto>(
            page.Items.Select(t => WalletTransactionDto.From(t, applicationNumbers)).ToList(),
            page.Page,
            page.PageSize,
            page.TotalCount);

        var orderNumber = await _db.Orders
            .AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(o => o.OrderNumber)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var pendingRequests = await _db.WalletRequests
            .AsNoTracking()
            // Each in its own balance's currency.
            .Include(r => r.Wallet)
            .ThenInclude(w => w!.Currency)
            // The applicant's own page repeats back which method and reference they submitted, so
            // they can tell what a pending top-up is waiting on.
            .Include(r => r.PaymentMethod)
            .ThenInclude(m => m!.Type)
            .Include(r => r.PaymentMethodAccount)
            .Include(r => r.Files)
            .Where(r => r.OrderId == orderId && r.Status == WalletRequestStatus.Pending)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return new WalletStatementDto(
            new WalletSummaryDto(
                walletId,
                shown.Balance,
                shown.CurrencyId,
                shown.CurrencyCode,
                shown.CurrencySymbol),
            ledger,
            new WalletFeaturesDto(
                _options.AllowDepositRequests,
                _options.AllowWithdrawalRequests,
                _options.AllowSimulatedDeposits,
                _options.MinimumRequestAmount,
                _options.MaximumRequestAmount),
            pendingRequests
                .Select(r => WalletRequestDto.From(
                    r,
                    orderNumber,
                    r.Wallet?.Currency?.Code ?? string.Empty,
                    _currentUser.LanguageCode))
                .ToList(),
            balances);
    }
}
