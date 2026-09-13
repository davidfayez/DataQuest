using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DataVerification.Application.Features.Orders.Commands;

/// <summary>Exchanges OrderNumber + password for an order-scoped JWT.</summary>
public sealed record LoginOrderCommand(string OrderNumber, string Password) : IRequest<LoginOrderResult>;

public sealed record LoginOrderResult(
    string AccessToken,
    DateTime ExpiresAtUtc,
    Guid OrderId,
    string OrderNumber,
    bool IsSetupComplete,
    string LanguageCode);

/// <summary>Signals bad credentials or a locked order without saying which. Surfaced as 401.</summary>
public sealed class InvalidCredentialsException : Exception
{
    public InvalidCredentialsException() : base("The credentials provided are not valid.") { }

    public InvalidCredentialsException(string message) : base(message) { }

    public InvalidCredentialsException(string message, Exception innerException)
        : base(message, innerException) { }
}

public sealed class LoginOrderCommandValidator : AbstractValidator<LoginOrderCommand>
{
    public LoginOrderCommandValidator()
    {
        // A client code (1–20 characters) followed by nine digits. The range also still admits the
        // fixed 12-character numbers issued before the format changed.
        RuleFor(c => c.OrderNumber).NotEmpty().MinimumLength(10).MaximumLength(29);
        RuleFor(c => c.Password).NotEmpty().MaximumLength(128);
    }
}

public sealed class LoginOrderCommandHandler : IRequestHandler<LoginOrderCommand, LoginOrderResult>
{
    /// <summary>Five failures then a cool-down, per the onboarding acceptance criteria.</summary>
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly IApplicationDbContext _db;
    private readonly IPasswordHashingService _passwordHasher;
    private readonly IJwtTokenService _tokenService;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<LoginOrderCommandHandler> _logger;

    public LoginOrderCommandHandler(
        IApplicationDbContext db,
        IPasswordHashingService passwordHasher,
        IJwtTokenService tokenService,
        IDateTimeProvider clock,
        ILogger<LoginOrderCommandHandler> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _clock = clock;
        _logger = logger;
    }

    public async Task<LoginOrderResult> Handle(
        LoginOrderCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var orderNumber = request.OrderNumber.Trim().ToUpperInvariant();

        var order = await _db.Orders
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber, cancellationToken);

        // A missing order and a wrong password produce the same response, so the endpoint cannot
        // be used to discover which order numbers exist.
        if (order is null)
        {
            throw new InvalidCredentialsException();
        }

        if (order.IsLockedOut(_clock.UtcNow))
        {
            _logger.LogWarning("Login attempt against locked-out order {OrderNumber}.", orderNumber);
            throw new InvalidCredentialsException();
        }

        var now = _clock.UtcNow;

        // The password already in the applicant's hands is tried first, so a reset they did not
        // ask for changes nothing for them.
        if (_passwordHasher.Verify(order.PasswordHash, request.Password))
        {
            // Signing in with the old one settles it: whatever the reset was for, it is not needed,
            // and an offer nobody wants should not sit there waiting to be used by whoever asked
            // for it.
            if (order.PendingPasswordHash is not null)
            {
                await RetireOfferAsync(order, PasswordResetOutcome.Superseded, now, cancellationToken);
                order.ClearPendingPassword();
            }
        }
        else if (order.HasPendingPassword(now)
                 && _passwordHasher.Verify(order.PendingPasswordHash!, request.Password))
        {
            // Using the emailed password is what makes it the real one. Until this moment the old
            // password was still the account's.
            order.CommitPendingPassword(now);
            await RetireOfferAsync(order, PasswordResetOutcome.Used, now, cancellationToken);

            _logger.LogInformation(
                "Order {OrderNumber} signed in with a reset password; it is now the account's.",
                orderNumber);
        }
        else
        {
            order.RegisterFailedLogin(now, MaxFailedAttempts, LockoutDuration);
            await _db.SaveChangesAsync(cancellationToken);
            throw new InvalidCredentialsException();
        }

        order.RegisterSuccessfulLogin(now);
        await _db.SaveChangesAsync(cancellationToken);

        var token = _tokenService.CreateApplicantToken(order);

        return new LoginOrderResult(
            token.Token,
            token.ExpiresAtUtc,
            order.Id,
            order.OrderNumber,
            order.IsSetupComplete,
            order.LanguageCode);
    }

    /// <summary>
    /// Closes the security-log entry for the offer this sign-in settled, so the admin panel shows
    /// what became of it rather than leaving every request reading "pending" for ever.
    /// </summary>
    private async Task RetireOfferAsync(
        Order order,
        PasswordResetOutcome outcome,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var open = await _db.PasswordResetRequests
            .Where(r => r.OrderId == order.Id && r.Outcome == PasswordResetOutcome.Pending)
            .ToListAsync(cancellationToken);

        foreach (var row in open)
        {
            row.Outcome = outcome;
            if (outcome == PasswordResetOutcome.Used) row.UsedAtUtc = now;
        }
    }
}

/// <summary>Rebuilds an applicant session from a valid refresh token, minting a fresh access token.</summary>
public sealed record RefreshOrderSessionCommand(Guid OrderId) : IRequest<LoginOrderResult>;

public sealed class RefreshOrderSessionCommandHandler
    : IRequestHandler<RefreshOrderSessionCommand, LoginOrderResult>
{
    private readonly IApplicationDbContext _db;
    private readonly IJwtTokenService _tokenService;

    public RefreshOrderSessionCommandHandler(IApplicationDbContext db, IJwtTokenService tokenService)
    {
        _db = db;
        _tokenService = tokenService;
    }

    public async Task<LoginOrderResult> Handle(
        RefreshOrderSessionCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var order = await _db.Orders
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken)
            ?? throw new InvalidCredentialsException();

        var token = _tokenService.CreateApplicantToken(order);

        return new LoginOrderResult(
            token.Token,
            token.ExpiresAtUtc,
            order.Id,
            order.OrderNumber,
            order.IsSetupComplete,
            order.LanguageCode);
    }
}
