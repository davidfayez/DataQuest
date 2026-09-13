using Asp.Versioning;
using DataVerification.API.Authorization;
using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Common.Models;
using DataVerification.Application.Features.Lookups.Admin;
using DataVerification.Application.Features.Tickets;
using DataVerification.Application.Features.Tickets.Admin;
using DataVerification.Domain.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace DataVerification.API.Controllers.Admin;

/// <summary>
/// The categories the public contact form offers. An ordinary admin lookup, with its own
/// permissions: deciding what the outside world may write in about is not the same job as
/// answering what they wrote.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/ticket-categories")]
[Produces("application/json")]
public sealed class AdminTicketCategoriesController : ControllerBase
{
    private readonly ISender _sender;
    private readonly ICurrentUser _currentUser;

    public AdminTicketCategoriesController(ISender sender, ICurrentUser currentUser)
    {
        _sender = sender;
        _currentUser = currentUser;
    }

    [HttpGet]
    [RequirePermission(Permissions.TicketCategoriesView)]
    [ProducesResponseType(typeof(PagedResult<TicketCategoryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<TicketCategoryDto>>> List(
        [FromQuery] ListTicketCategoriesQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(query, cancellationToken));

    /// <summary>
    /// An upsert, so the permission it needs is resolved from the id at request time rather than
    /// by a route attribute.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(TicketCategoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TicketCategoryDto>> Upsert(
        [FromBody] UpsertTicketCategoryCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var required = command.Id is null || command.Id == Guid.Empty
            ? Permissions.TicketCategoriesCreate
            : Permissions.TicketCategoriesUpdate;

        if (!_currentUser.Permissions.Contains(required))
        {
            throw new ForbiddenAccessException($"This action requires the '{required}' permission.");
        }

        return Ok(await _sender.Send(command, cancellationToken));
    }

    /// <summary>Removes the category, or deactivates it when tickets have been filed under it.</summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.TicketCategoriesDelete)]
    [ProducesResponseType(typeof(LookupDeleteOutcome), StatusCodes.Status200OK)]
    public async Task<ActionResult<LookupDeleteOutcome>> Delete(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new DeleteTicketCategoryCommand(id), cancellationToken));
}
