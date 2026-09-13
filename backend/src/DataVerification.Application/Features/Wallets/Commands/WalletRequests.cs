using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Features.Payments;
using DataVerification.Domain.Authorization;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DataVerification.Application.Features.Wallets.Commands;

/// <summary>
/// Raises a deposit or withdrawal request against the caller's own wallet.
/// </summary>
/// <remarks>
/// A withdrawal holds the money immediately: the wallet is debited when the request is created, so
/// the same balance cannot also be spent on applications while an operator decides. A deposit moves
/// nothing until it is approved — the funds do not exist yet.
/// </remarks>
public sealed record CreateWalletRequestCommand(
    WalletRequestType Type,
    decimal Amount,
    string? Note) : IRequest<WalletRequestDto>;

public sealed class CreateWalletRequestCommandValidator : AbstractValidator<CreateWalletRequestCommand>
{
    public CreateWalletRequestCommandValidator(IOptions<WalletOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var limits = options.Value;

        RuleFor(c => c.Type).IsInEnum();

        RuleFor(c => c.Amount)
            .GreaterThanOrEqualTo(limits.MinimumRequestAmount)
            .WithMessage($"The amount must be at least {limits.MinimumRequestAmount:0.00}.")
            .LessThanOrEqualTo(limits.MaximumRequestAmount)
            .WithMessage($"The amount may not exceed {limits.MaximumRequestAmount:0.00}.");

        RuleFor(c => c.Note).MaximumLength(1000);
    }
}

public sealed class CreateWalletRequestCommandHandler
    : IRequestHandler<CreateWalletRequestCommand, WalletRequestDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _auditLogger;
    private readonly WalletOptions _options;

    public CreateWalletRequestCommandHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IAuditLogger auditLogger,
        IOptions<WalletOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _db = db;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
        _options = options.Value;
    }

    public async Task<WalletRequestDto> Handle(
        CreateWalletRequestCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var orderId = _currentUser.OrderId
            ?? throw new ForbiddenAccessException("This endpoint is only available to applicants.");

        var allowed = request.Type == WalletRequestType.Deposit
            ? _options.AllowDepositRequests
            : _options.AllowWithdrawalRequests;

        if (!allowed)
        {
            throw new ConflictException(
                "wallet_request.disabled",
                $"{request.Type} requests are not available at the moment.");
        }

        // The hold and the request row must land together, or a crash between them would take the
        // applicant's money without leaving anything to refund it against.
        return await _db.ExecuteInTransactionAsync(
            ct => CreateAsync(orderId, request, ct),
            cancellationToken);
    }

    private async Task<WalletRequestDto> CreateAsync(
        Guid orderId,
        CreateWalletRequestCommand request,
        CancellationToken cancellationToken)
    {
        var order = await _db.Orders
            .Include(o => o.Wallet)
            .ThenInclude(w => w!.Currency)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new NotFoundException("Order", orderId);

        var wallet = order.Wallet
            ?? throw new ConflictException("order.wallet_missing", "This order has no wallet.");

        // One decision at a time per direction: a queue of overlapping payouts is impossible to
        // reason about, and a second top-up request usually means the first was forgotten.
        var hasPending = await _db.WalletRequests.AnyAsync(
            r => r.OrderId == orderId
                && r.Type == request.Type
                && r.Status == WalletRequestStatus.Pending,
            cancellationToken);

        if (hasPending)
        {
            throw new ConflictException(
                "wallet_request.already_pending",
                "A request of this kind is already awaiting a decision.");
        }

        var walletRequest = new WalletRequest
        {
            OrderId = orderId,
            WalletId = wallet.Id,
            Type = request.Type,
            Amount = request.Amount,
            ApplicantNote = request.Note,
            RequestedByName = _currentUser.DisplayName ?? order.OrderNumber,
        };

        if (request.Type == WalletRequestType.Withdrawal)
        {
            // Throws InsufficientFundsException (422) when the balance will not cover the payout.
            var hold = wallet.Debit(
                request.Amount,
                _currentUser.ToActor(),
                null,
                request.Note ?? "Withdrawal requested",
                WalletTransactionType.Withdrawal);

            walletRequest.WalletTransactionId = hold.Id;
        }

        _db.WalletRequests.Add(walletRequest);
        await _db.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            "WalletRequest.Created",
            "WalletRequest",
            walletRequest.Id,
            new { orderId, request.Type, request.Amount, wallet.Balance },
            cancellationToken);

        return WalletRequestDto.From(walletRequest, order.OrderNumber, wallet.Currency?.Code ?? string.Empty);
    }
}

/// <summary>Called off by the applicant before anyone decided. Releases a withdrawal's hold.</summary>
public sealed record CancelWalletRequestCommand(Guid RequestId) : IRequest<WalletRequestDto>;

public sealed class CancelWalletRequestCommandHandler
    : IRequestHandler<CancelWalletRequestCommand, WalletRequestDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _auditLogger;

    public CancelWalletRequestCommandHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IAuditLogger auditLogger)
    {
        _db = db;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
    }

    public async Task<WalletRequestDto> Handle(
        CancelWalletRequestCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var orderId = _currentUser.OrderId
            ?? throw new ForbiddenAccessException("This endpoint is only available to applicants.");

        return await _db.ExecuteInTransactionAsync(
            ct => CancelAsync(orderId, request.RequestId, ct),
            cancellationToken);
    }

    private async Task<WalletRequestDto> CancelAsync(
        Guid orderId,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        var context = await WalletRequestContext.LoadAsync(
            _db,
            r => r.Id == requestId && r.OrderId == orderId,
            requestId,
            cancellationToken);

        // Cancel first: the domain refuses anything already decided, so no money moves on a replay.
        context.Request.Cancel(_currentUser.ToActor());
        context.ReleaseHoldIfAny(_currentUser.ToActor(), "Withdrawal request cancelled");

        await context.SaveAsync(cancellationToken);

        await _auditLogger.LogAsync(
            "WalletRequest.Cancelled",
            "WalletRequest",
            requestId,
            new { orderId, context.Request.Type, context.Request.Amount },
            cancellationToken);

        return context.ToDto();
    }
}

/// <summary>
/// An administrator's decision on a request. Approving a deposit credits the wallet; approving a
/// withdrawal simply confirms the hold taken at request time, because the money already left the
/// balance. Rejecting either one releases anything that was held.
/// </summary>
/// <param name="ConfirmedAmount">
/// What the reviewer confirmed actually arrived. Required to approve — it is what the wallet is
/// credited with, in place of the applicant's claim. Optional on a rejection, where nothing moves.
/// </param>
/// <param name="ConfirmedReference">
/// The reference the reviewer matched against the statement. Required to approve, and unique among
/// approved requests so one receipt can never be credited twice.
/// </param>
public sealed record DecideWalletRequestCommand(
    Guid RequestId,
    bool Approve,
    decimal? ConfirmedAmount,
    string? ConfirmedReference,
    string? ReviewerNote) : IRequest<WalletRequestDto>;

public sealed class DecideWalletRequestCommandValidator : AbstractValidator<DecideWalletRequestCommand>
{
    public DecideWalletRequestCommandValidator()
    {
        RuleFor(c => c.RequestId).NotEmpty();
        RuleFor(c => c.ReviewerNote).MaximumLength(1000);
        RuleFor(c => c.ConfirmedReference).MaximumLength(100);

        // Approving moves money, so the two figures it moves on are not optional.
        When(c => c.Approve, () =>
        {
            RuleFor(c => c.ConfirmedAmount)
                .NotNull().WithMessage("Enter the amount you confirmed.")
                .GreaterThan(0).WithMessage("The confirmed amount must be greater than zero.");

            RuleFor(c => c.ConfirmedReference)
                .NotEmpty().WithMessage("Enter the reference you matched against the statement.");
        });

        RuleFor(c => c.ConfirmedAmount)
            .GreaterThan(0).When(c => c.ConfirmedAmount.HasValue)
            .WithMessage("The confirmed amount must be greater than zero.");
    }
}

public sealed class DecideWalletRequestCommandHandler
    : IRequestHandler<DecideWalletRequestCommand, WalletRequestDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _auditLogger;
    private readonly PaymentNotifier _notifier;

    public DecideWalletRequestCommandHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IAuditLogger auditLogger,
        PaymentNotifier notifier)
    {
        _db = db;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
        _notifier = notifier;
    }

    public async Task<WalletRequestDto> Handle(
        DecideWalletRequestCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _db.ExecuteInTransactionAsync(
            ct => DecideAsync(request, ct),
            cancellationToken);
    }

    /// <summary>
    /// One receipt may only be credited once. Scoped to approved requests: a rejected claim never
    /// took money, so its reference stays free for the applicant to quote correctly next time.
    /// </summary>
    private async Task EnsureReferenceNotAlreadyCreditedAsync(
        Guid requestId,
        string reference,
        CancellationToken cancellationToken)
    {
        var alreadyUsed = await _db.WalletRequests.AnyAsync(
            r => r.Id != requestId
                && r.Status == WalletRequestStatus.Approved
                && r.ConfirmedReference == reference,
            cancellationToken);

        if (alreadyUsed)
        {
            throw new ConflictException(
                "wallet_request.reference_already_credited",
                "That reference has already been credited on another request.");
        }
    }

    private async Task<WalletRequestDto> DecideAsync(
        DecideWalletRequestCommand command,
        CancellationToken cancellationToken)
    {
        var context = await WalletRequestContext.LoadAsync(
            _db,
            r => r.Id == command.RequestId,
            command.RequestId,
            cancellationToken);

        // Which permission is needed depends on the direction of the money, so it cannot be pinned
        // to the route: crediting a wallet and paying one out are deliberately separate grants.
        var required = context.Request.Type == WalletRequestType.Deposit
            ? Permissions.OrdersCredit
            : Permissions.OrdersWithdraw;

        if (!_currentUser.Permissions.Contains(required))
        {
            throw new ForbiddenAccessException($"This action requires the {required} permission.");
        }

        var actor = _currentUser.ToActor();

        // The state transition runs first, so a request decided by another administrator a moment
        // ago is rejected before any money moves.
        if (command.Approve)
        {
            var confirmedAmount = command.ConfirmedAmount!.Value;
            var confirmedReference = command.ConfirmedReference!.Trim();

            await EnsureReferenceNotAlreadyCreditedAsync(
                context.Request.Id, confirmedReference, cancellationToken);

            context.Request.Approve(actor, confirmedAmount, confirmedReference, command.ReviewerNote);

            if (context.Request.Type == WalletRequestType.Deposit)
            {
                // The reviewer's figure, not the applicant's claim: what arrived is what is credited.
                var credit = context.Wallet.Credit(
                    confirmedAmount,
                    WalletTransactionType.TopUp,
                    actor,
                    null,
                    command.ReviewerNote ?? $"Top-up approved (ref {confirmedReference})");

                context.Request.WalletTransactionId = credit.Id;
            }
        }
        else
        {
            context.Request.Reject(actor, command.ConfirmedAmount, command.ReviewerNote);
            context.ReleaseHoldIfAny(actor, command.ReviewerNote ?? "Withdrawal request rejected");
        }

        await context.SaveAsync(cancellationToken);

        await _auditLogger.LogAsync(
            command.Approve ? "WalletRequest.Approved" : "WalletRequest.Rejected",
            "WalletRequest",
            command.RequestId,
            new
            {
                context.Request.OrderId,
                context.Request.Type,
                ClaimedAmount = context.Request.Amount,
                ClaimedReference = context.Request.ReferenceNumber,
                context.Request.ConfirmedAmount,
                context.Request.ConfirmedReference,
                context.Wallet.Balance,
                command.ReviewerNote,
            },
            cancellationToken);

        // Whoever reconciles this method hears the outcome. Only a deposit raised through a method
        // has recipients to tell; a payout carries none.
        if (context.Request.PaymentMethod is { } method)
        {
            await _notifier.NotifyAsync(
                method,
                context.Request.Status,
                context.OrderNumber,
                context.Request.Amount,
                context.Wallet.Currency?.Code ?? string.Empty,
                context.Request.ReferenceNumber,
                context.Request.ReviewedByName,
                command.ReviewerNote,
                cancellationToken);
        }

        return context.ToDto();
    }
}

/// <summary>
/// The request, its wallet and its order loaded together — every decision path needs all three,
/// and the money-releasing rule is identical whether the applicant cancelled or an admin refused.
/// </summary>
internal sealed class WalletRequestContext
{
    private readonly IApplicationDbContext _db;
    private readonly Order _order;

    private WalletRequestContext(IApplicationDbContext db, WalletRequest request, Wallet wallet, Order order)
    {
        _db = db;
        _order = order;
        Request = request;
        Wallet = wallet;
    }

    public WalletRequest Request { get; }

    public Wallet Wallet { get; }

    public string OrderNumber => _order.OrderNumber;

    public static async Task<WalletRequestContext> LoadAsync(
        IApplicationDbContext db,
        System.Linq.Expressions.Expression<Func<WalletRequest, bool>> predicate,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        var request = await db.WalletRequests
            .Include(r => r.PaymentMethod)
            .ThenInclude(m => m!.Type)
            .Include(r => r.PaymentMethod)
            .ThenInclude(m => m!.NotificationEmails)
            .Include(r => r.PaymentMethodAccount)
            .Include(r => r.Files)
            .FirstOrDefaultAsync(predicate, cancellationToken)
            ?? throw new NotFoundException("Wallet request", requestId);

        var wallet = await db.Wallets
            .Include(w => w.Currency)
            .FirstOrDefaultAsync(w => w.Id == request.WalletId, cancellationToken)
            ?? throw new ConflictException("order.wallet_missing", "This order has no wallet.");

        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken)
            ?? throw new NotFoundException("Order", request.OrderId);

        return new WalletRequestContext(db, request, wallet, order);
    }

    /// <summary>Puts a withdrawal's held funds back. A deposit never held anything, so this is a no-op.</summary>
    public void ReleaseHoldIfAny(Domain.Common.Actor actor, string note)
    {
        if (Request.Type != WalletRequestType.Withdrawal)
        {
            return;
        }

        Wallet.Credit(
            Request.Amount,
            WalletTransactionType.WithdrawalReversal,
            actor,
            null,
            note);
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(
                "wallet_request.concurrent_modification",
                "This request changed while your decision was being saved. Reload and try again.");
        }
    }

    public WalletRequestDto ToDto() =>
        WalletRequestDto.From(Request, _order.OrderNumber, Wallet.Currency?.Code ?? string.Empty);
}
