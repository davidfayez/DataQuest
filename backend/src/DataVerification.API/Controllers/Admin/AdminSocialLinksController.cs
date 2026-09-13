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
/// The "Follow us" and "Message us" channels in the public site footer.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/social-links")]
[Produces("application/json")]
public sealed class AdminSocialLinksController : ControllerBase
{
    private readonly ISender _sender;
    private readonly ICurrentUser _currentUser;

    public AdminSocialLinksController(ISender sender, ICurrentUser currentUser)
    {
        _sender = sender;
        _currentUser = currentUser;
    }

    [HttpGet]
    [RequirePermission(Permissions.SocialLinksView)]
    [ProducesResponseType(typeof(PagedResult<AdminSocialLinkDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AdminSocialLinkDto>>> List(
        [FromQuery] ListSocialLinksQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(query, cancellationToken));

    /// <summary>
    /// An upsert, so the permission it needs is resolved from the id at request time rather than
    /// by a route attribute.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(AdminSocialLinkDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminSocialLinkDto>> Upsert(
        [FromBody] UpsertSocialLinkCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var required = command.Id is null || command.Id == Guid.Empty
            ? Permissions.SocialLinksCreate
            : Permissions.SocialLinksUpdate;

        if (!_currentUser.Permissions.Contains(required))
        {
            throw new ForbiddenAccessException($"This action requires the '{required}' permission.");
        }

        return Ok(await _sender.Send(command, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.SocialLinksDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _sender.Send(new DeleteSocialLinkCommand(id), cancellationToken);
        return NoContent();
    }
}
