using Asp.Versioning;
using DataVerification.API.Authorization;
using DataVerification.API.Infrastructure;
using DataVerification.Application.Common.Models;
using DataVerification.Application.Features.Applications;
using DataVerification.Application.Features.Applications.Commands;
using DataVerification.Application.Features.Applications.Queries;
using DataVerification.Application.Features.Review;
using DataVerification.Application.Features.Review.Commands;
using DataVerification.Application.Features.Review.Queries;
using DataVerification.Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DataVerification.API.Controllers;

/// <summary>
/// The applicant's applications. Every action is scoped to the order on the caller's token — an
/// application belonging to another order is reported as not found, never as forbidden.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/applications")]
[Authorize(Policy = PolicyNames.Applicant)]
[Produces("application/json")]
public sealed class ApplicationsController : ControllerBase
{
    private readonly ISender _sender;

    public ApplicationsController(ISender sender) => _sender = sender;

    /// <summary>Lists the caller's applications with status, totals and capability flags.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ApplicationListItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ApplicationListItemDto>>> GetMine(
        [FromQuery] GetMyApplicationsQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(query, cancellationToken));

    /// <summary>
    /// The filter values the caller's own applications contain, each with a count. Drives the
    /// grid's dropdowns, so no option can be chosen that returns nothing.
    /// </summary>
    [HttpGet("filters")]
    [ProducesResponseType(typeof(ApplicationFiltersDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApplicationFiltersDto>> GetFilters(
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetMyApplicationFiltersQuery(), cancellationToken));

    /// <summary>Every status transition this application went through, oldest first.</summary>
    [HttpGet("{id:guid}/status-log")]
    [ProducesResponseType(typeof(IReadOnlyList<ApplicationStatusLogEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ApplicationStatusLogEntryDto>>> GetStatusLog(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetApplicationStatusLogQuery(id), cancellationToken));

    /// <summary>
    /// Everything anyone changed on this application, newest first. Internal reviewer activity is
    /// filtered out — the applicant sees what happened to their request, not the back office's.
    /// </summary>
    [HttpGet("{id:guid}/change-log")]
    [ProducesResponseType(typeof(PagedResult<ApplicationChangeLogEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<ApplicationChangeLogEntryDto>>> GetChangeLog(
        Guid id,
        [FromQuery] GetApplicationChangeLogQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(query with { ApplicationId = id }, cancellationToken));

    /// <summary>The full application, including the services summary and required-file checklist.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApplicationDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApplicationDetailsDto>> GetById(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetApplicationDetailsQuery(id), cancellationToken));

    /// <summary>Creates a Draft application.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApplicationDetailsDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApplicationDetailsDto>> Create(
        [FromBody] CreateApplicationCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id, version = "1.0" }, result);
    }

    /// <summary>Updates an unpaid application. Returns 409 once it has been paid for.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApplicationDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApplicationDetailsDto>> Update(
        Guid id,
        [FromBody] UpdateApplicationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new UpdateApplicationCommand(
            id,
            request.AddressedTo,
            request.BirthDate,
            request.ApplicantEmail,
            request.ApplicantPhoneCountry,
            request.ApplicantPhoneCode,
            request.ApplicantPhoneNumber,
            request.Names,
            request.TransactionTypeId,
            request.SubTransactionTypeId,
            request.VerificationAuthorityId,
            request.Services);

        return Ok(await _sender.Send(command, cancellationToken));
    }

    /// <summary>Deletes an unpaid application. Returns 409 once it has been paid for.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _sender.Send(new DeleteApplicationCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Saves the applicant's answers to the custom fields configured on the required documents.
    /// The whole set is sent at once and replaces whatever was held before.
    /// </summary>
    [HttpPut("{id:guid}/document-values")]
    [ProducesResponseType(typeof(ApplicationDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApplicationDetailsDto>> SaveDocumentValues(
        Guid id,
        [FromBody] SaveDocumentValuesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Ok(await _sender.Send(
            new SaveDocumentValuesCommand(id, request.Values),
            cancellationToken));
    }

    /// <summary>Moves a Draft to PendingPayment once every mandatory file is attached.</summary>
    [HttpPost("{id:guid}/submit")]
    [ProducesResponseType(typeof(ApplicationDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApplicationDetailsDto>> Submit(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new SubmitApplicationCommand(id), cancellationToken));

    /// <summary>Uploads evidence. Accepts PDF, JPG, JPEG and PNG up to 5 MB.</summary>
    [HttpPost("{id:guid}/files")]
    [RequestSizeLimit(ApplicationFile.MaxFileSizeBytes + 8192)]
    [EnableRateLimiting(RateLimitPolicies.Uploads)]
    [ProducesResponseType(typeof(ApplicationFileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApplicationFileDto>> UploadFile(
        Guid id,
        [FromForm] UploadApplicationFileRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.File is null || request.File.Length == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "No file supplied",
                Detail = "Attach a file to the 'file' form field.",
            });
        }

        // Buffered to a seekable stream so the type validator can read the signature and the
        // storage provider can then write the same bytes from the start.
        await using var buffer = new MemoryStream();
        await request.File.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        var command = new UploadApplicationFileCommand(
            id,
            request.ApplicationServiceId,
            request.RequiredFileId,
            request.File.FileName,
            request.File.Length,
            buffer);

        return Ok(await _sender.Send(command, cancellationToken));
    }

    /// <summary>
    /// The activity feed: comments, status changes and file events in one chronological thread.
    /// Internal comments are filtered out in the query layer, so they never reach this response.
    /// </summary>
    [HttpGet("{id:guid}/timeline")]
    [ProducesResponseType(typeof(IReadOnlyList<TimelineEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<TimelineEntryDto>>> GetTimeline(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetApplicationTimelineQuery(id), cancellationToken));

    /// <summary>Replies to a reviewer. An applicant's comment is always user-visible.</summary>
    [HttpPost("{id:guid}/comments")]
    [ProducesResponseType(typeof(CommentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CommentDto>> AddComment(
        Guid id,
        [FromBody] ApplicantCommentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Ok(await _sender.Send(
            new AddApplicantCommentCommand(id, request.Body),
            cancellationToken));
    }

    /// <summary>Returns a MissedInfo application to the queue once the request has been answered.</summary>
    [HttpPost("{id:guid}/resubmit")]
    [ProducesResponseType(typeof(ApplicationStatusResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApplicationStatusResultDto>> Resubmit(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new ResubmitApplicationCommand(id), cancellationToken));

    /// <summary>The verified deliverables, available once the application has succeeded.</summary>
    [HttpGet("{id:guid}/results")]
    [ProducesResponseType(typeof(IReadOnlyList<ResultFileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ResultFileDto>>> GetResults(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetApplicationResultsQuery(id), cancellationToken));

    /// <summary>Streams a file back to its owner.</summary>
    [HttpGet("{id:guid}/files/{fileId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadFile(
        Guid id,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        var download = await _sender.Send(
            new DownloadApplicationFileQuery(id, fileId),
            cancellationToken);

        return File(download.Content, download.ContentType, download.FileName);
    }
}

/// <summary>
/// A draft may be saved from any step, so every field a later step fills in is optional here.
/// Completeness is enforced when the application is submitted, not when it is saved.
/// </summary>
/// <param name="ApplicantPhoneCountry">ISO 3166-1 alpha-2 code of the dial code chosen, e.g. <c>EG</c>.</param>
/// <param name="ApplicantPhoneCode">The dial prefix itself, e.g. <c>+20</c>.</param>
/// <param name="ApplicantPhoneNumber">The national number, digits only, without the prefix.</param>
public sealed record UpdateApplicationRequest(
    string AddressedTo,
    DateOnly? BirthDate,
    string? ApplicantEmail,
    string? ApplicantPhoneCountry,
    string? ApplicantPhoneCode,
    string? ApplicantPhoneNumber,
    IReadOnlyList<ApplicationNameInput> Names,
    Guid? TransactionTypeId,
    Guid? SubTransactionTypeId,
    Guid? VerificationAuthorityId,
    IReadOnlyList<ApplicationServiceInput> Services);

public sealed record ApplicantCommentRequest(string Body);

public sealed class UploadApplicationFileRequest
{
    public IFormFile? File { get; set; }

    /// <summary>The service line this document belongs to, when it satisfies a requirement.</summary>
    public Guid? ApplicationServiceId { get; set; }

    public Guid? RequiredFileId { get; set; }
}

public sealed record SaveDocumentValuesRequest(IReadOnlyList<DocumentValueInput> Values);
