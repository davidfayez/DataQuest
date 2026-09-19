using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DataVerification.Application.Features.Wallets.Commands;

/// <summary>
/// Credits the caller's own wallet with no operator in the loop, so a demo or a test run can pay
/// for applications without a second account.
/// </summary>
/// <remarks>
/// This mints money. It exists only where <c>Wallet:AllowSimulatedDeposits</c> is switched on —
/// off by default, and the handler refuses outright rather than trusting the caller not to reach
/// the endpoint. Never enable it in production.
/// </remarks>
/// <param name="CurrencyId">Which balance to top up; the order's main currency when omitted.</param>
public sealed record SimulateDepositCommand(decimal Amount, Guid? CurrencyId = null) : IRequest<WalletSummaryDto>;

public sealed class SimulateDepositCommandValidator : AbstractValidator<SimulateDepositCommand>
{
    public SimulateDepositCommandValidator(IOptions<WalletOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var limits = options.Value;

        RuleFor(c => c.Amount)
            .GreaterThan(0).WithMessage("The amount must be greater than zero.")
            .LessThanOrEqualTo(limits.MaximumSimulatedDeposit)
            .WithMessage($"A test top-up may not exceed {limits.MaximumSimulatedDeposit:0.00}.");
    }
}

public sealed class SimulateDepositCommandHandler : IRequestHandler<SimulateDepositCommand, WalletSummaryDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _auditLogger;
    private readonly WalletOptions _options;
    private readonly WalletBook _wallets;

    public SimulateDepositCommandHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IAuditLogger auditLogger,
        IOptions<WalletOptions> options,
        WalletBook wallets)
    {
        ArgumentNullException.ThrowIfNull(options);

        _db = db;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
        _options = options.Value;
        _wallets = wallets;
    }

    public async Task<WalletSummaryDto> Handle(
        SimulateDepositCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_options.AllowSimulatedDeposits)
        {
            throw new ForbiddenAccessException("Simulated deposits are not enabled on this environment.");
        }

        var orderId = _currentUser.OrderId
            ?? throw new ForbiddenAccessException("This endpoint is only available to applicants.");

        var wallet = await _wallets.OpenAsync(orderId, request.CurrencyId, cancellationToken);

        wallet.Credit(
            request.Amount,
            WalletTransactionType.TopUp,
            _currentUser.ToActor(),
            null,
            "Simulated deposit (test environment)");

        await _db.SaveChangesAsync(cancellationToken);

        // Logged like any other credit, so a simulated top-up is never mistaken for a real one.
        await _auditLogger.LogAsync(
            "Wallet.SimulatedDeposit",
            "Wallet",
            wallet.Id,
            new { orderId, request.Amount, wallet.Balance },
            cancellationToken);

        return new WalletSummaryDto(
            wallet.Id,
            wallet.Balance,
            wallet.CurrencyId,
            wallet.Currency?.Code ?? string.Empty,
            wallet.Currency?.Symbol ?? string.Empty);
    }
}
