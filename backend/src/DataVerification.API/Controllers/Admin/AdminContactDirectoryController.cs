using Asp.Versioning;
using DataVerification.API.Authorization;
using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Common.Models;
using DataVerification.Application.Features.Content.Admin;
using DataVerification.Domain.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace DataVerification.API.Controllers.Admin;

/// <summary>
/// The agents and office details shown on the public contact page.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/contact-directory")]
[Produces("application/json")]
public sealed class AdminContactDirectoryController : ControllerBase
{
    private readonly ISender _sender;
    private readonly ICurrentUser _currentUser;

    public AdminContactDirectoryController(ISender sender, ICurrentUser currentUser)
    {
        _sender = sender;
        _currentUser = currentUser;
    }

    [HttpGet]
    [RequirePermission(Permissions.ContactDirectoryView)]
    [ProducesResponseType(typeof(PagedResult<AdminContactDirectoryEntryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AdminContactDirectoryEntryDto>>> List(
        [FromQuery] ListContactDirectoryQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(query, cancellationToken));

    /// <summary>
    /// An upsert, so the permission it needs is resolved from the id at request time rather than
    /// by a route attribute.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(AdminContactDirectoryEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminContactDirectoryEntryDto>> Upsert(
        [FromBody] UpsertContactDirectoryEntryCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var required = command.Id is null || command.Id == Guid.Empty
            ? Permissions.ContactDirectoryCreate
            : Permissions.ContactDirectoryUpdate;

        if (!_currentUser.Permissions.Contains(required))
        {
            throw new ForbiddenAccessException($"This action requires the '{required}' permission.");
        }

        return Ok(await _sender.Send(command, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.ContactDirectoryDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _sender.Send(new DeleteContactDirectoryEntryCommand(id), cancellationToken);
        return NoContent();
    }
}
