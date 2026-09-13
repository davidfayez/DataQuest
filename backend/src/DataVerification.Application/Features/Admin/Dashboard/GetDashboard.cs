using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Admin.Dashboard;

/// <summary>Headline numbers for the admin landing page.</summary>
public sealed record GetDashboardQuery : IRequest<DashboardDto>;

public sealed record DashboardDto(
    IReadOnlyList<StatusCountDto> StatusCounts,
    int TotalApplications,
    int OpenQueue,
    int OrderCount,
    decimal RevenueCollected,
    decimal RevenueRefunded,
    decimal RevenueNet,
    decimal WalletFloat,
    IReadOnlyList<RecentActivityDto> RecentActivity);

public sealed record StatusCountDto(ApplicationStatus Status, string StatusName, int Count);

public sealed record RecentActivityDto(
    string Action,
    string EntityType,
    Guid? EntityId,
    string? ActorName,
    DateTime CreatedAtUtc);

public sealed class GetDashboardQueryHandler : IRequestHandler<GetDashboardQuery, DashboardDto>
{
    private const int RecentActivityCount = 15;

    private readonly IApplicationDbContext _db;

    public GetDashboardQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<DashboardDto> Handle(
        GetDashboardQuery request,
        CancellationToken cancellationToken)
    {
        var counts = await _db.Applications
            .AsNoTracking()
            .GroupBy(a => a.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var byStatus = counts.ToDictionary(entry => entry.Status, entry => entry.Count);

        // Every status is represented, including the ones with no rows, so the dashboard's tiles
        // do not appear and disappear as data changes.
        var statusCounts = Enum.GetValues<ApplicationStatus>()
            .Select(status => new StatusCountDto(
                status,
                status.ToString(),
                byStatus.TryGetValue(status, out var count) ? count : 0))
            .ToList();

        // Revenue is read from the ledger rather than from application totals: the ledger is the
        // record of money that actually moved.
        var collected = await _db.WalletTransactions
            .AsNoTracking()
            .Where(t => t.Type == WalletTransactionType.Payment)
            .SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0m;

        var refunded = await _db.WalletTransactions
            .AsNoTracking()
            .Where(t => t.Type == WalletTransactionType.Refund)
            .SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0m;

        var walletFloat = await _db.Wallets
            .AsNoTracking()
            .SumAsync(w => (decimal?)w.Balance, cancellationToken) ?? 0m;

        var recent = await _db.AuditLog
            .AsNoTracking()
            .OrderByDescending(entry => entry.CreatedAtUtc)
            .Take(RecentActivityCount)
            .Select(entry => new RecentActivityDto(
                entry.Action,
                entry.EntityType,
                entry.EntityId,
                entry.ActorName,
                entry.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        var openQueue = statusCounts
            .Where(entry => entry.Status is ApplicationStatus.Pending
                or ApplicationStatus.InProgress
                or ApplicationStatus.MissedInfo)
            .Sum(entry => entry.Count);

        return new DashboardDto(
            statusCounts,
            statusCounts.Sum(entry => entry.Count),
            openQueue,
            await _db.Orders.AsNoTracking().CountAsync(cancellationToken),
            collected,
            refunded,
            collected - refunded,
            walletFloat,
            recent);
    }
}
