using Asp.Versioning;
using DataVerification.API.Authorization;
using DataVerification.Application.Common.Models;
using DataVerification.Application.Features.Payments.Queries;
using DataVerification.Application.Features.Wallets;
using DataVerification.Application.Features.Wallets.Commands;
using DataVerification.Application.Features.Wallets.Queries;
using DataVerification.Domain.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace DataVerification.API.Controllers.Admin;

/// <summary>
/// The queue of applicant deposit and withdrawal requests. Seeing the queue needs only
/// <c>Orders.View</c>; deciding a request needs <c>Orders.Credit</c> for a deposit or
/// <c>Orders.Withdraw</c> for a payout, which the handler enforces because the required permission
/// depends on the request being decided rather than on the route.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/wallet-requests")]
[Produces("application/json")]
public sealed class AdminWalletRequestsController : ControllerBase
{
    private readonly ISender _sender;

    public AdminWalletRequestsController(ISender sender) => _sender = sender;

    /// <summary>Pending requests first, then the decided ones newest-first.</summary>
    [HttpGet]
    [RequirePermission(Permissions.OrdersView)]
    [ProducesResponseType(typeof(PagedResult<WalletRequestDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<WalletRequestDto>>> List(
        [FromQuery] GetWalletRequestsQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(query, cancellationToken));

    /// <summary>One request in full, for its own page.</summary>
    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.OrdersView)]
    [ProducesResponseType(typeof(WalletRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WalletRequestDto>> Get(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetWalletRequestQuery(id), cancellationToken));

    /// <summary>
    /// Accepts the request: a deposit credits the wallet, a withdrawal confirms the hold taken when
    /// it was raised. Returns 409 if someone already decided it.
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    [RequirePermission(Permissions.OrdersView)]
    [ProducesResponseType(typeof(WalletRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WalletRequestDto>> Approve(
        Guid id,
        [FromBody] DecideWalletRequestBody? request,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(
            new DecideWalletRequestCommand(
                id,
                Approve: true,
                request?.ConfirmedAmount,
                request?.ConfirmedReference,
                request?.Note),
            cancellationToken));

    /// <summary>
    /// Downloads a receipt attached to the request. Reading the evidence is part of deciding, so it
    /// needs only <c>Orders.View</c> — the same grant that opens the queue.
    /// </summary>
    [HttpGet("{id:guid}/files/{fileId:guid}")]
    [RequirePermission(Permissions.OrdersView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFile(
        Guid id,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        var download = await _sender.Send(new GetWalletRequestFileQuery(id, fileId), cancellationToken);
        return File(download.Content, download.ContentType, download.FileName);
    }

    /// <summary>
    /// The request's own trail: raised, decided, and by whom. Read from the audit log but scoped to
    /// this one request, so a reviewer sees its history without needing global audit access.
    /// </summary>
    [HttpGet("{id:guid}/history")]
    [RequirePermission(Permissions.OrdersView)]
    [ProducesResponseType(typeof(IReadOnlyList<WalletRequestHistoryEntryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<WalletRequestHistoryEntryDto>>> GetHistory(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetWalletRequestHistoryQuery(id), cancellationToken));

    /// <summary>Refuses the request, releasing a withdrawal's held funds back to the wallet.</summary>
    [HttpPost("{id:guid}/reject")]
    [RequirePermission(Permissions.OrdersView)]
    [ProducesResponseType(typeof(WalletRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WalletRequestDto>> Reject(
        Guid id,
        [FromBody] DecideWalletRequestBody? request,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(
            new DecideWalletRequestCommand(
                id,
                Approve: false,
                request?.ConfirmedAmount,
                request?.ConfirmedReference,
                request?.Note),
            cancellationToken));
}

/// <param name="ConfirmedAmount">
/// What the reviewer confirmed arrived. Required to approve; it is credited in place of the
/// applicant's claim.
/// </param>
/// <param name="ConfirmedReference">The reference matched against the statement. Required to approve.</param>
public sealed record DecideWalletRequestBody(
    decimal? ConfirmedAmount,
    string? ConfirmedReference,
    string? Note);
