using Asp.Versioning;
using DataVerification.API.Authorization;
using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Common.Models;
using DataVerification.Application.Features.Lookups.Admin;
using DataVerification.Application.Features.Payments.Admin;
using DataVerification.Domain.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace DataVerification.API.Controllers.Admin;

/// <summary>
/// The Payment type integrations page: configured connections to payment gateways, which payment
/// methods then choose from. Behind the payment-methods permissions, since the two are configured
/// together and neither means anything alone.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/payment-integrations")]
[Produces("application/json")]
public sealed class AdminPaymentGatewayIntegrationsController : ControllerBase
{
    private readonly ISender _sender;
    private readonly ICurrentUser _currentUser;

    public AdminPaymentGatewayIntegrationsController(ISender sender, ICurrentUser currentUser)
    {
        _sender = sender;
        _currentUser = currentUser;
    }

    /// <summary>Every gateway an integration can be made for, with the settings each one needs.</summary>
    [HttpGet("gateways")]
    [RequirePermission(Permissions.PaymentMethodsView)]
    [ProducesResponseType(typeof(IReadOnlyList<PaymentGatewayDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PaymentGatewayDto>>> Gateways(CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new ListPaymentGatewaysQuery(), cancellationToken));

    [HttpGet]
    [RequirePermission(Permissions.PaymentMethodsView)]
    [ProducesResponseType(typeof(PagedResult<PaymentGatewayIntegrationDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<PaymentGatewayIntegrationDto>>> List(
        [FromQuery] ListPaymentGatewayIntegrationsQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(query, cancellationToken));

    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.PaymentMethodsView)]
    [ProducesResponseType(typeof(PaymentGatewayIntegrationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PaymentGatewayIntegrationDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetPaymentGatewayIntegrationQuery(id), cancellationToken));

    [HttpPost]
    [ProducesResponseType(typeof(PaymentGatewayIntegrationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PaymentGatewayIntegrationDto>> Upsert(
        [FromBody] UpsertPaymentGatewayIntegrationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // An upsert: create or update is decided by the id.
        var required = command.Id is null || command.Id == Guid.Empty
            ? Permissions.PaymentMethodsCreate
            : Permissions.PaymentMethodsUpdate;
        if (!_currentUser.Permissions.Contains(required))
        {
            throw new ForbiddenAccessException($"This action requires the '{required}' permission.");
        }

        Response.Headers.CacheControl = "no-store";
        return Ok(await _sender.Send(command, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.PaymentMethodsDelete)]
    [ProducesResponseType(typeof(LookupDeleteOutcome), StatusCodes.Status200OK)]
    public async Task<ActionResult<LookupDeleteOutcome>> Delete(Guid id, CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new DeletePaymentGatewayIntegrationCommand(id), cancellationToken));
}
