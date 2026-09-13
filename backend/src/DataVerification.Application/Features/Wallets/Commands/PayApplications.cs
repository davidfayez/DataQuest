using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Wallets.Commands;

/// <summary>
/// Settles one or more applications from the order's wallet in a single transaction. The whole
/// batch succeeds or none of it does — there is no partial payment.
/// </summary>
public sealed record PayApplicationsCommand(IReadOnlyList<Guid> ApplicationIds)
    : IRequest<PaymentResultDto>;

public sealed class PayApplicationsCommandValidator : AbstractValidator<PayApplicationsCommand>
{
    public PayApplicationsCommandValidator()
    {
        RuleFor(c => c.ApplicationIds)
            .NotEmpty().WithMessage("Select at least one application to pay for.")
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .WithMessage("The same application was listed more than once.");
    }
}

public sealed class PayApplicationsCommandHandler
    : IRequestHandler<PayApplicationsCommand, PaymentResultDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IAuditLogger _auditLogger;

    public PayApplicationsCommandHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        IAuditLogger auditLogger)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _auditLogger = auditLogger;
    }

    public async Task<PaymentResultDto> Handle(
        PayApplicationsCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var orderId = _currentUser.OrderId
            ?? throw new ForbiddenAccessException("This endpoint is only available to applicants.");

        // Every read and write happens inside the retriable transaction, so a failure part way
        // through cannot leave the wallet charged for work that was never queued.
        return await _db.ExecuteInTransactionAsync(
            ct => SettleAsync(orderId, request, ct),
            cancellationToken);
    }

    private async Task<PaymentResultDto> SettleAsync(
        Guid orderId,
        PayApplicationsCommand request,
        CancellationToken cancellationToken)
    {
        var wallet = await _db.Wallets
            .Include(w => w.Currency)
            .FirstOrDefaultAsync(w => w.OrderId == orderId, cancellationToken)
            ?? throw new ConflictException(
                "order.setup_incomplete",
                "Complete order setup before paying for applications.");

        var applications = await _db.Applications
            .Where(a => request.ApplicationIds.Contains(a.Id) && a.OrderId == orderId)
            .ToListAsync(cancellationToken);

        // Anything missing here either does not exist or belongs to another order; both are
        // reported the same way so ids cannot be probed.
        if (applications.Count != request.ApplicationIds.Count)
        {
            throw new NotFoundException(
                "One or more of the applications selected could not be found on this order.");
        }

        var notPayable = applications
            .Where(a => a.Status != ApplicationStatus.PendingPayment)
            .ToList();

        if (notPayable.Count > 0)
        {
            throw new ConflictException(
                "payment.application_not_payable",
                "Only applications awaiting payment can be paid for: "
                + string.Join(", ", notPayable.Select(a => $"{a.ApplicationNumber} ({a.Status})")));
        }

        var total = applications.Sum(a => a.TotalCost);

        if (total <= 0)
        {
            throw new ConflictException(
                "payment.nothing_to_pay",
                "The selected applications have no outstanding balance.");
        }

        // Throws InsufficientFundsException (surfaced as 422 with required vs available) rather
        // than letting the balance go negative.
        var ledgerEntry = wallet.Debit(
            total,
            _currentUser.ToActor(),
            applications.Select(a => a.Id).ToList(),
            $"Payment for {applications.Count} application(s)");

        var paidAt = _clock.UtcNow;

        foreach (var application in applications)
        {
            application.MarkPaid(_currentUser.ToActor(), paidAt);
        }

        try
        {
            // The wallet and each application carry row versions, so a concurrent double-submit
            // fails here instead of charging twice. The transaction rolls back on the way out.
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(
                "payment.concurrent_modification",
                "These applications were modified while the payment was being processed. Try again.");
        }

        await _auditLogger.LogAsync(
            "Wallet.Payment",
            "Wallet",
            wallet.Id,
            new
            {
                Amount = total,
                wallet.Balance,
                Applications = applications.Select(a => a.ApplicationNumber),
            },
            cancellationToken);

        // Also recorded against each application, so the audit trail for a single application is
        // complete on its own without cross-referencing the wallet.
        foreach (var application in applications)
        {
            await _auditLogger.LogAsync(
                "Application.StatusChanged",
                "Application",
                application.Id,
                new
                {
                    application.ApplicationNumber,
                    From = ApplicationStatus.PendingPayment,
                    To = application.Status,
                    Reason = "Paid from wallet",
                    Amount = application.TotalCost,
                },
                cancellationToken);
        }

        return new PaymentResultDto(
            ledgerEntry.Id,
            total,
            wallet.Balance,
            wallet.Currency?.Code ?? string.Empty,
            applications
                .Select(a => new PaidApplicationDto(
                    a.Id,
                    a.ApplicationNumber,
                    a.TotalCost,
                    a.Status,
                    a.PaidAtUtc ?? paidAt))
                .ToList());
    }
}
