using Asp.Versioning;
using DataVerification.API.Authorization;
using DataVerification.API.Controllers;
using DataVerification.Application.Features.Wallets;
using DataVerification.Application.Features.Wallets.Commands;
using DataVerification.Application.Features.Orders.Queries;
using DataVerification.Application.Features.Wallets.Queries;
using DataVerification.Domain.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace DataVerification.API.Controllers.Admin;

/// <summary>Back-office access to orders, their wallets and refunds.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/orders")]
[Produces("application/json")]
public sealed class AdminOrdersController : ControllerBase
{
    private readonly ISender _sender;

    public AdminOrdersController(ISender sender) => _sender = sender;

    /// <summary>
    /// Reveals the order's sign-in password. It is decrypted from the copy stored at registration,
    /// so orders created before that copy existed — or while no encryption key was configured —
    /// come back with <c>isAvailable: false</c> and a reason rather than a password. Every
    /// successful reveal is written to the audit trail.
    /// </summary>
    [HttpGet("{orderId:guid}/password")]
    [RequirePermission(Permissions.OrdersViewPassword)]
    [ProducesResponseType(typeof(OrderPasswordDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderPasswordDto>> GetPassword(
        Guid orderId,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetOrderPasswordQuery(orderId), cancellationToken));

    /// <summary>Wallet balance and ledger for a specific order.</summary>
    [HttpGet("{orderId:guid}/wallet")]
    [RequirePermission(Permissions.OrdersView)]
    [ProducesResponseType(typeof(WalletStatementDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WalletStatementDto>> GetWallet(
        Guid orderId,
        [FromQuery] GetOrderWalletQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(query with { OrderId = orderId }, cancellationToken));

    /// <summary>Tops the wallet up. v1 has no gateway, so funds are credited by an operator.</summary>
    [HttpPost("{orderId:guid}/wallet/credit")]
    [RequirePermission(Permissions.OrdersCredit)]
    [ProducesResponseType(typeof(WalletSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WalletSummaryDto>> CreditWallet(
        Guid orderId,
        [FromBody] CreditWalletRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Ok(await _sender.Send(
            new CreditWalletCommand(orderId, request.Amount, request.Note),
            cancellationToken));
    }

    /// <summary>
    /// Admin-initiated refund. Subject to exactly the same Pending-only rule as the applicant's
    /// own refund — the permission changes who may ask, not what is allowed.
    /// </summary>
    [HttpPost("{orderId:guid}/applications/{applicationId:guid}/refund")]
    [RequirePermission(Permissions.OrdersRefund)]
    [ProducesResponseType(typeof(RefundResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RefundResultDto>> Refund(
        Guid orderId,
        Guid applicationId,
        [FromBody] RefundApplicationRequest? request,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(
            new RefundApplicationCommand(applicationId, request?.Note, orderId),
            cancellationToken));
}

public sealed record CreditWalletRequest(decimal Amount, string? Note);
