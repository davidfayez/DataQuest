using Asp.Versioning;
using DataVerification.API.Authorization;
using DataVerification.API.Infrastructure;
using DataVerification.Application.Common.Models;
using DataVerification.Application.Features.Tickets;
using DataVerification.Application.Features.Tickets.Admin;
using DataVerification.Application.Features.Tickets.Commands;
using DataVerification.Application.Features.Tickets.Queries;
using DataVerification.Domain.Authorization;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DataVerification.API.Controllers.Admin;

/// <summary>
/// The support queue: reading tickets, answering them, and linking them to the order or
/// application they turn out to be about.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/tickets")]
[Produces("application/json")]
public sealed class AdminTicketsController : ControllerBase
{
    /// <summary>A reply's documents, capped the same way the sender's own upload is.</summary>
    private const long MaxReplyBodyBytes = (TicketFile.MaxFileSizeBytes * 10) + 8192;

    private readonly ISender _sender;

    public AdminTicketsController(ISender sender) => _sender = sender;

    [HttpGet]
    [RequirePermission(Permissions.TicketsView)]
    [ProducesResponseType(typeof(PagedResult<TicketListItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<TicketListItemDto>>> List(
        [FromQuery] ListTicketsQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(query, cancellationToken));

    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.TicketsView)]
    [ProducesResponseType(typeof(TicketDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TicketDetailsDto>> Get(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetTicketQuery(id), cancellationToken));

    /// <summary>Downloads a file attached to the enquiry or to one of its replies.</summary>
    [HttpGet("{id:guid}/files/{fileId:guid}")]
    [RequirePermission(Permissions.TicketsView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(
        Guid id,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        var file = await _sender.Send(new GetTicketFileQuery(id, fileId), cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }

    /// <summary>The support users a ticket may be handed to.</summary>
    [HttpGet("assignees")]
    [RequirePermission(Permissions.TicketsView)]
    [ProducesResponseType(typeof(IReadOnlyList<TicketAssigneeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TicketAssigneeDto>>> Assignees(
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetTicketAssigneesQuery(), cancellationToken));

    /// <summary>Orders matching what has been typed into the link picker.</summary>
    [HttpGet("orders")]
    [RequirePermission(Permissions.TicketsView)]
    [ProducesResponseType(typeof(IReadOnlyList<TicketOrderOptionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TicketOrderOptionDto>>> SearchOrders(
        [FromQuery] string? search,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new SearchTicketOrdersQuery(search), cancellationToken));

    /// <summary>The applications belonging to one order, for the second half of the link.</summary>
    [HttpGet("orders/{orderId:guid}/applications")]
    [RequirePermission(Permissions.TicketsView)]
    [ProducesResponseType(typeof(IReadOnlyList<TicketApplicationOptionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TicketApplicationOptionDto>>> OrderApplications(
        Guid orderId,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetTicketOrderApplicationsQuery(orderId), cancellationToken));

    [HttpPost("{id:guid}/assign")]
    [RequirePermission(Permissions.TicketsUpdate)]
    [ProducesResponseType(typeof(TicketDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TicketDetailsDto>> Assign(
        Guid id,
        [FromBody] AssignTicketRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Ok(await _sender.Send(
            new AssignTicketCommand(id, request.AdminUserId), cancellationToken));
    }

    [HttpPost("{id:guid}/link")]
    [RequirePermission(Permissions.TicketsUpdate)]
    [ProducesResponseType(typeof(TicketDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TicketDetailsDto>> Link(
        Guid id,
        [FromBody] LinkTicketRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Ok(await _sender.Send(
            new LinkTicketCommand(id, request.OrderId, request.ApplicationId), cancellationToken));
    }

    [HttpPost("{id:guid}/status")]
    [RequirePermission(Permissions.TicketsUpdate)]
    [ProducesResponseType(typeof(TicketDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TicketDetailsDto>> ChangeStatus(
        Guid id,
        [FromBody] ChangeTicketStatusRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Ok(await _sender.Send(
            new ChangeTicketStatusCommand(id, request.Status), cancellationToken));
    }

    /// <summary>
    /// Records what support did: a reply for the sender, a note for the team, or both at once.
    /// Multipart, because a reply carries files.
    /// </summary>
    /// <remarks>
    /// A reply is visible in the applicant's account as soon as it is saved. Emailing them a copy
    /// is a separate, deliberate step — see <see cref="Notify"/>.
    /// </remarks>
    [HttpPost("{id:guid}/actions")]
    [Consumes("multipart/form-data")]
    [RequirePermission(Permissions.TicketsUpdate)]
    [EnableRateLimiting(RateLimitPolicies.Uploads)]
    [RequestSizeLimit(MaxReplyBodyBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxReplyBodyBytes)]
    [ProducesResponseType(typeof(TicketDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TicketDetailsDto>> CreateAction(
        Guid id,
        [FromForm] CreateTicketActionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var streams = new List<Stream>();

        try
        {
            var documents = new List<TicketDocumentInput>(request.Documents.Count);

            foreach (var document in request.Documents)
            {
                var files = new List<TicketFileUpload>();

                foreach (var file in document.Files.Where(candidate => candidate.Length > 0))
                {
                    var stream = file.OpenReadStream();
                    streams.Add(stream);
                    files.Add(new TicketFileUpload(file.FileName, file.Length, stream));
                }

                documents.Add(new TicketDocumentInput(
                    document.Title ?? string.Empty,
                    document.Description,
                    files,
                    document.IsInternal));
            }

            var command = new CreateTicketActionCommand(
                id,
                request.ReplyToSender,
                request.InternalNote,
                request.ChangeStatusTo,
                documents);

            return Ok(await _sender.Send(command, cancellationToken));
        }
        finally
        {
            foreach (var stream in streams)
            {
                await stream.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Emails an action to the person who raised the ticket.
    /// </summary>
    /// <remarks>
    /// Its own permission, separate from writing the reply: composing an answer stays inside the
    /// platform, and this is the step that puts it in somebody's inbox. The handler refuses an
    /// internal note outright, whatever this caller holds.
    /// </remarks>
    [HttpPost("{id:guid}/actions/{actionId:guid}/notify")]
    [RequirePermission(Permissions.TicketsNotify)]
    [ProducesResponseType(typeof(TicketDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TicketDetailsDto>> Notify(
        Guid id,
        Guid actionId,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new NotifyTicketActionCommand(id, actionId), cancellationToken));
}

public sealed class AssignTicketRequest
{
    /// <summary>Null takes the ticket back off whoever is carrying it.</summary>
    public Guid? AdminUserId { get; set; }
}

public sealed class LinkTicketRequest
{
    public Guid? OrderId { get; set; }

    public Guid? ApplicationId { get; set; }
}

public sealed class ChangeTicketStatusRequest
{
    public TicketStatus Status { get; set; }
}

public sealed class CreateTicketActionRequest
{
    /// <summary>What the applicant reads. Either this, the note, or both may be filled in.</summary>
    public string? ReplyToSender { get; set; }

    /// <summary>What the team reads. Never shown to the applicant.</summary>
    public string? InternalNote { get; set; }

    public TicketStatus? ChangeStatusTo { get; set; }

    public List<TicketDocumentForm> Documents { get; set; } = [];
}

public sealed class TicketDocumentForm
{
    public string? Title { get; set; }

    public string? Description { get; set; }

    /// <summary>True to keep the document with the team's note instead of sending it.</summary>
    public bool IsInternal { get; set; }

    public List<IFormFile> Files { get; set; } = [];
}
