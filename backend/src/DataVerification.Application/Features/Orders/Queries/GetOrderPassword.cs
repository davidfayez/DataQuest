using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Orders.Queries;

/// <summary>
/// Reveals an order's sign-in password to a back-office caller holding
/// <c>Orders.ViewPassword</c>. The password is decrypted from the reversible copy written at
/// registration; the hash sign-in uses is never touched, and every successful reveal is audited.
/// </summary>
public sealed record GetOrderPasswordQuery(Guid OrderId) : IRequest<OrderPasswordDto>;

/// <summary>
/// The password is optional by design — an order registered before the encrypted copy existed, or
/// while no key was configured, has nothing to reveal. <paramref name="Reason"/> says which case
/// this is so the caller can show something better than an empty field.
/// </summary>
/// <param name="Reason">
/// <c>not_configured</c> — no encryption key is set on the server;
/// <c>not_stored</c> — this order predates the encrypted copy, or was created without a key;
/// <c>unreadable</c> — stored under a key that is no longer configured.
/// Null when the password was revealed.
/// </param>
public sealed record OrderPasswordDto(
    Guid OrderId,
    string OrderNumber,
    string Email,
    string? Password,
    bool IsAvailable,
    string? Reason,
    /// <summary>
    /// True when this is a password emailed by "forgot password" that has not been used yet. The
    /// applicant's own password is still the previous one until they sign in with this.
    /// </summary>
    bool IsPending = false,
    DateTime? PendingExpiresAtUtc = null);

public sealed class GetOrderPasswordQueryHandler
    : IRequestHandler<GetOrderPasswordQuery, OrderPasswordDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _passwordProtector;
    private readonly IAuditLogger _auditLogger;

    public GetOrderPasswordQueryHandler(
        IApplicationDbContext db,
        ISecretProtector passwordProtector,
        IAuditLogger auditLogger)
    {
        _db = db;
        _passwordProtector = passwordProtector;
        _auditLogger = auditLogger;
    }

    public async Task<OrderPasswordDto> Handle(
        GetOrderPasswordQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var order = await _db.Orders
            .AsNoTracking()
            .Where(o => o.Id == request.OrderId)
            .Select(o => new
            {
                o.Id,
                o.OrderNumber,
                o.Email,
                o.PasswordSecret,
                o.PendingPasswordSecret,
                o.PendingPasswordExpiresAtUtc,
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(Order), request.OrderId);

        if (!_passwordProtector.IsEnabled)
        {
            return Unavailable(order.Id, order.OrderNumber, order.Email, "not_configured");
        }

        // An offer still outstanding is what support is almost always being asked about — it is the
        // password in the email the applicant is looking at. Reported as pending, because the
        // account's password is still the old one until they sign in with this one.
        var isPending = order.PendingPasswordSecret is not null
            && order.PendingPasswordExpiresAtUtc > DateTime.UtcNow;

        var secret = isPending ? order.PendingPasswordSecret : order.PasswordSecret;

        if (string.IsNullOrEmpty(secret))
        {
            return Unavailable(order.Id, order.OrderNumber, order.Email, "not_stored");
        }

        var password = _passwordProtector.Unprotect(secret);

        if (password is null)
        {
            return Unavailable(order.Id, order.OrderNumber, order.Email, "unreadable");
        }

        // A credential leaving the platform is exactly the kind of thing the trail exists for, so
        // the reveal is recorded — never the password itself.
        await _auditLogger.LogAsync(
            "Order.PasswordRevealed",
            nameof(Order),
            order.Id,
            new { order.OrderNumber },
            cancellationToken);

        return new OrderPasswordDto(
            order.Id,
            order.OrderNumber,
            order.Email,
            password,
            true,
            null,
            isPending,
            isPending ? order.PendingPasswordExpiresAtUtc : null);
    }

    private static OrderPasswordDto Unavailable(
        Guid orderId,
        string orderNumber,
        string email,
        string reason) =>
        new(orderId, orderNumber, email, null, false, reason);
}
