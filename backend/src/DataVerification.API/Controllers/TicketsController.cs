using Asp.Versioning;
using DataVerification.API.Authorization;
using DataVerification.API.Infrastructure;
using DataVerification.Application.Features.Tickets;
using DataVerification.Application.Features.Tickets.Commands;
using DataVerification.Application.Features.Tickets.Queries;
using DataVerification.Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DataVerification.API.Controllers;

/// <summary>
/// The public "contact us" form: the categories it offers, and the enquiries it raises.
///
/// Anonymous by design — someone who cannot sign in is exactly the person most likely to need
/// support — which is why both endpoints are rate limited and the response says nothing back about
/// what the platform now holds.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/tickets")]
[Produces("application/json")]
public sealed class TicketsController : ControllerBase
{
    private readonly ISender _sender;

    public TicketsController(ISender sender) => _sender = sender;

    /// <summary>The subjects the form offers, resolved to the caller's language.</summary>
    [HttpGet("categories")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IReadOnlyList<TicketCategoryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TicketCategoryDto>>> GetCategories(
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetTicketCategoriesQuery(), cancellationToken));

    /// <summary>The signed-in applicant's own enquiries, newest first.</summary>
    [HttpGet("mine")]
    [Authorize(Policy = PolicyNames.Applicant)]
    [ProducesResponseType(typeof(IReadOnlyList<MyTicketListItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MyTicketListItemDto>>> Mine(
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetMyTicketsQuery(), cancellationToken));

    /// <summary>
    /// One of the applicant's enquiries, with the replies support has actually sent them.
    /// Internal notes are filtered in the query and can never reach this response.
    /// </summary>
    [HttpGet("mine/{id:guid}")]
    [Authorize(Policy = PolicyNames.Applicant)]
    [ProducesResponseType(typeof(MyTicketDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MyTicketDetailsDto>> Mine(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetMyTicketQuery(id), cancellationToken));

    /// <summary>
    /// The applicant writing back on their own ticket. Refused once the ticket is closed.
    /// </summary>
    [HttpPost("mine/{id:guid}/replies")]
    [Authorize(Policy = PolicyNames.Applicant)]
    [Consumes("multipart/form-data")]
    [EnableRateLimiting(RateLimitPolicies.Uploads)]
    [RequestSizeLimit((TicketFile.MaxFileSizeBytes * TicketFile.MaxFilesPerTicket) + 8192)]
    [RequestFormLimits(
        MultipartBodyLengthLimit = (TicketFile.MaxFileSizeBytes * TicketFile.MaxFilesPerTicket) + 8192)]
    [ProducesResponseType(typeof(MyTicketDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MyTicketDetailsDto>> Reply(
        Guid id,
        [FromForm] ReplyToMyTicketRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var streams = new List<Stream>();

        try
        {
            var files = new List<TicketFileUpload>();

            foreach (var file in request.Files.Where(candidate => candidate.Length > 0))
            {
                var stream = file.OpenReadStream();
                streams.Add(stream);
                files.Add(new TicketFileUpload(file.FileName, file.Length, stream));
            }

            await _sender.Send(new ReplyToMyTicketCommand(id, request.Body, files), cancellationToken);

            // The thread comes back so the page redraws from the server's version rather than
            // guessing what it now looks like.
            return Ok(await _sender.Send(new GetMyTicketQuery(id), cancellationToken));
        }
        finally
        {
            foreach (var stream in streams)
            {
                await stream.DisposeAsync();
            }
        }
    }

    /// <summary>Downloads a file the applicant may read — their own, or one sent to them.</summary>
    [HttpGet("mine/{id:guid}/files/{fileId:guid}")]
    [Authorize(Policy = PolicyNames.Applicant)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadMine(
        Guid id,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        var file = await _sender.Send(new GetMyTicketFileQuery(id, fileId), cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }

    /// <summary>
    /// Raises a ticket. Multipart so the sender can attach what they are describing.
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [Consumes("multipart/form-data")]
    [EnableRateLimiting(RateLimitPolicies.Registration)]
    [RequestSizeLimit((TicketFile.MaxFileSizeBytes * TicketFile.MaxFilesPerTicket) + 8192)]
    [RequestFormLimits(
        MultipartBodyLengthLimit = (TicketFile.MaxFileSizeBytes * TicketFile.MaxFilesPerTicket) + 8192)]
    [ProducesResponseType(typeof(TicketSubmittedDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TicketSubmittedDto>> Create(
        [FromForm] CreateTicketRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var streams = new List<Stream>();

        try
        {
            var files = new List<TicketFileUpload>();

            foreach (var file in request.Files.Where(candidate => candidate.Length > 0))
            {
                var stream = file.OpenReadStream();
                streams.Add(stream);
                files.Add(new TicketFileUpload(file.FileName, file.Length, stream));
            }

            var command = new CreateTicketCommand(
                request.TicketCategoryId,
                request.Name ?? string.Empty,
                request.Email ?? string.Empty,
                request.PhoneCountryCode,
                request.PhoneNumber,
                request.Subject ?? string.Empty,
                request.Description ?? string.Empty,
                files);

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
}

/// <summary>The applicant's reply as it arrives over the wire.</summary>
public sealed class ReplyToMyTicketRequest
{
    public string? Body { get; set; }

    public List<IFormFile> Files { get; set; } = [];
}

/// <summary>The contact form as it arrives over the wire.</summary>
public sealed class CreateTicketRequest
{
    public Guid TicketCategoryId { get; set; }

    public string? Name { get; set; }

    public string? Email { get; set; }

    public string? PhoneCountryCode { get; set; }

    public string? PhoneNumber { get; set; }

    public string? Subject { get; set; }

    public string? Description { get; set; }

    public List<IFormFile> Files { get; set; } = [];
}
