using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Wallets.Commands;

/// <summary>
/// Refunds a paid application back to the order's wallet. Allowed only while the application is
/// still Pending — once a reviewer has started work the money is committed.
/// </summary>
/// <param name="OrderId">
/// Supplied by admin-initiated refunds. Applicant refunds resolve the order from the token.
/// </param>
public sealed record RefundApplicationCommand(Guid ApplicationId, string? Note = null, Guid? OrderId = null)
    : IRequest<RefundResultDto>;

public sealed class RefundApplicationCommandHandler
    : IRequestHandler<RefundApplicationCommand, RefundResultDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _auditLogger;
    private readonly WalletBook _wallets;

    public RefundApplicationCommandHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IAuditLogger auditLogger,
        WalletBook wallets)
    {
        _db = db;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
        _wallets = wallets;
    }

    public async Task<RefundResultDto> Handle(
        RefundApplicationCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // An applicant may only refund their own application; an admin acting under Wallet.Refund
        // names the order explicitly.
        var orderId = request.OrderId
            ?? _currentUser.OrderId
            ?? throw new ForbiddenAccessException("An order must be identified to process a refund.");

        return await _db.ExecuteInTransactionAsync(
            ct => ProcessRefundAsync(orderId, request, ct),
            cancellationToken);
    }

    private async Task<RefundResultDto> ProcessRefundAsync(
        Guid orderId,
        RefundApplicationCommand request,
        CancellationToken cancellationToken)
    {
        var application = await _db.Applications
            .FirstOrDefaultAsync(
                a => a.Id == request.ApplicationId && a.OrderId == orderId,
                cancellationToken)
            ?? throw new NotFoundException("Application", request.ApplicationId);

        // Back into the balance it was paid from: the application's own currency.
        var wallet = await _wallets.OpenAsync(orderId, application.CurrencyId, cancellationToken);

        var amount = application.TotalCost;

        // Domain guard: throws application.not_refundable for anything past Pending.
        application.Refund(_currentUser.ToActor(), request.Note);

        var ledgerEntry = wallet.Credit(
            amount,
            WalletTransactionType.Refund,
            _currentUser.ToActor(),
            [application.Id],
            request.Note ?? $"Refund for {application.ApplicationNumber}");

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(
                "refund.concurrent_modification",
                "This application changed while the refund was being processed. Try again.");
        }

        await _auditLogger.LogAsync(
            "Wallet.Refunded",
            "Application",
            application.Id,
            new { application.ApplicationNumber, Amount = amount, wallet.Balance },
            cancellationToken);

        return new RefundResultDto(
            ledgerEntry.Id,
            application.Id,
            application.ApplicationNumber,
            amount,
            wallet.Balance,
            application.Status);
    }
}

/// <summary>
/// Admin top-up. v1 has no payment gateway, so funds are credited by an operator — into the
/// order's balance in <paramref name="CurrencyId"/>, or its main currency when none is named.
/// </summary>
public sealed record CreditWalletCommand(Guid OrderId, decimal Amount, string? Note, Guid? CurrencyId = null)
    : IRequest<WalletSummaryDto>;

public sealed class CreditWalletCommandValidator : AbstractValidator<CreditWalletCommand>
{
    public CreditWalletCommandValidator()
    {
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.Amount).GreaterThan(0).WithMessage("The amount must be greater than zero.");
        RuleFor(c => c.Note).MaximumLength(1000);
    }
}

public sealed class CreditWalletCommandHandler : IRequestHandler<CreditWalletCommand, WalletSummaryDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _auditLogger;

    private readonly WalletBook _wallets;

    public CreditWalletCommandHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IAuditLogger auditLogger,
        WalletBook wallets)
    {
        _db = db;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
        _wallets = wallets;
    }

    public async Task<WalletSummaryDto> Handle(
        CreditWalletCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var wallet = await _wallets.OpenAsync(request.OrderId, request.CurrencyId, cancellationToken);

        wallet.Credit(
            request.Amount,
            WalletTransactionType.TopUp,
            _currentUser.ToActor(),
            null,
            request.Note);

        await _db.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            "Wallet.Credited",
            "Wallet",
            wallet.Id,
            new { request.OrderId, request.Amount, Currency = wallet.Currency?.Code, wallet.Balance, request.Note },
            cancellationToken);

        return new WalletSummaryDto(
            wallet.Id,
            wallet.Balance,
            wallet.CurrencyId,
            wallet.Currency?.Code ?? string.Empty,
            wallet.Currency?.Symbol ?? string.Empty);
    }
}
