using Asp.Versioning;
using DataVerification.API.Authorization;
using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Common.Models;
using DataVerification.Application.Features.Lookups.Admin;
using DataVerification.Application.Features.Payments;
using DataVerification.Application.Features.Payments.Admin;
using DataVerification.Domain.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace DataVerification.API.Controllers.Admin;

/// <summary>
/// The bank catalogue behind bank-transfer payment methods, scoped by country.
///
/// Sits on the payment-methods permission set rather than its own: it exists to be picked from
/// while configuring a method, and a role that could add a method but not the bank it pays into
/// could only ever produce a channel nobody can pay through.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/banks")]
[Produces("application/json")]
public sealed class AdminBanksController : ControllerBase
{
    private readonly ISender _sender;
    private readonly ICurrentUser _currentUser;

    public AdminBanksController(ISender sender, ICurrentUser currentUser)
    {
        _sender = sender;
        _currentUser = currentUser;
    }

    [HttpGet]
    [RequirePermission(Permissions.PaymentMethodsView)]
    [ProducesResponseType(typeof(PagedResult<BankDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<BankDto>>> List(
        [FromQuery] ListBanksQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(query, cancellationToken));

    /// <summary>
    /// The write endpoint is an upsert, so the required permission is resolved from the id at
    /// request time rather than by a route attribute.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(BankDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BankDto>> Upsert(
        [FromBody] UpsertBankCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var required = command.Id is null || command.Id == Guid.Empty
            ? Permissions.PaymentMethodsCreate
            : Permissions.PaymentMethodsUpdate;

        if (!_currentUser.Permissions.Contains(required))
        {
            throw new ForbiddenAccessException($"This action requires the '{required}' permission.");
        }

        return Ok(await _sender.Send(command, cancellationToken));
    }

    /// <summary>Removes the bank, or deactivates it when a receiving account still names it.</summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.PaymentMethodsDelete)]
    [ProducesResponseType(typeof(LookupDeleteOutcome), StatusCodes.Status200OK)]
    public async Task<ActionResult<LookupDeleteOutcome>> Delete(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new DeleteBankCommand(id), cancellationToken));
}
