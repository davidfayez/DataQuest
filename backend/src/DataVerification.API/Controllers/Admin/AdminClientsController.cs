using Asp.Versioning;
using DataVerification.API.Authorization;
using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Common.Models;
using DataVerification.Application.Features.Clients;
using DataVerification.Application.Features.Lookups.Admin;
using DataVerification.Domain.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace DataVerification.API.Controllers.Admin;

/// <summary>
/// Admin CRUD for tenants (clients). Reads and deletes are attribute-gated; the write endpoint is
/// an upsert, so the required permission (create vs update) is resolved from the id at request time.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/clients")]
[Produces("application/json")]
public sealed class AdminClientsController : ControllerBase
{
    private readonly ISender _sender;
    private readonly ICurrentUser _currentUser;

    public AdminClientsController(ISender sender, ICurrentUser currentUser)
    {
        _sender = sender;
        _currentUser = currentUser;
    }

    private void RequireUpsert(Guid? id)
    {
        var required = id is null || id == Guid.Empty
            ? Permissions.ClientsCreate
            : Permissions.ClientsUpdate;

        if (!_currentUser.Permissions.Contains(required))
        {
            throw new ForbiddenAccessException($"This action requires the '{required}' permission.");
        }
    }

    [HttpGet]
    [RequirePermission(Permissions.ClientsView)]
    [ProducesResponseType(typeof(PagedResult<ClientDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ClientDto>>> List(
        [FromQuery] ListClientsQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(query, cancellationToken));

    [HttpPost]
    [ProducesResponseType(typeof(ClientDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ClientDto>> Upsert(
        [FromBody] UpsertClientCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireUpsert(command.Id);
        return Ok(await _sender.Send(command, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.ClientsDelete)]
    [ProducesResponseType(typeof(LookupDeleteOutcome), StatusCodes.Status200OK)]
    public async Task<ActionResult<LookupDeleteOutcome>> Delete(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new DeleteClientCommand(id), cancellationToken));
}
