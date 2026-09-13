using Asp.Versioning;
using DataVerification.API.Authorization;
using DataVerification.API.Infrastructure;
using DataVerification.Application.Common.Models;
using DataVerification.Application.Features.Review;
using DataVerification.Application.Features.Review.Commands;
using DataVerification.Application.Features.Review.Queries;
using DataVerification.Domain.Authorization;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DataVerification.API.Controllers.Admin;

/// <summary>The review queue and the actions a reviewer can take on an application.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/applications")]
[Produces("application/json")]
public sealed class AdminApplicationsController : ControllerBase
{
    private readonly ISender _sender;

    public AdminApplicationsController(ISender sender) => _sender = sender;

    /// <summary>Filterable review queue.</summary>
    [HttpGet]
    [RequirePermission(Permissions.ApplicationsView)]
    [ProducesResponseType(typeof(PagedResult<AdminApplicationListItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AdminApplicationListItemDto>>> GetQueue(
        [FromQuery] GetAdminApplicationsQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(query, cancellationToken));

    /// <summary>Full application detail, including internal comments.</summary>
    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.ApplicationsView)]
    [ProducesResponseType(typeof(AdminApplicationDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminApplicationDetailsDto>> GetById(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetAdminApplicationDetailsQuery(id), cancellationToken));

    /// <summary>The merged activity feed, internal entries included.</summary>
    [HttpGet("{id:guid}/timeline")]
    [RequirePermission(Permissions.ApplicationsView)]
    [ProducesResponseType(typeof(IReadOnlyList<TimelineEntryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TimelineEntryDto>>> GetTimeline(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetApplicationTimelineQuery(id), cancellationToken));

    /// <summary>Moves the application through the review lifecycle.</summary>
    [HttpPost("{id:guid}/status")]
    [Consumes("application/json")]
    [RequirePermission(Permissions.ApplicationsReview)]
    [ProducesResponseType(typeof(ApplicationStatusResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApplicationStatusResultDto>> ChangeStatus(
        Guid id,
        [FromBody] ChangeStatusRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Ok(await _sender.Send(
            new ChangeApplicationStatusCommand(
                id,
                request.ToStatus,
                request.UserComment,
                request.InternalComment),
            cancellationToken));
    }

    /// <summary>
    /// The same transition, carrying documents. Selected by content type rather than a separate
    /// route so both callers post to one URL — the JSON form above stays the simple case, and
    /// nothing that already posts JSON has to change.
    ///
    /// Both ceilings have to be raised explicitly: Kestrel and the form reader are capped globally
    /// at a single file's worth, which is the whole point of that cap for every other endpoint.
    /// </summary>
    [HttpPost("{id:guid}/status")]
    [Consumes("multipart/form-data")]
    [RequirePermission(Permissions.ApplicationsReview)]
    [RequestSizeLimit(AttachedDocumentLimits.MaxRequestBodyBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = AttachedDocumentLimits.MaxRequestBodyBytes)]
    [EnableRateLimiting(RateLimitPolicies.Uploads)]
    [ProducesResponseType(typeof(ApplicationStatusResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApplicationStatusResultDto>> ChangeStatusWithDocuments(
        Guid id,
        [FromForm] ChangeStatusWithDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Form files are buffered by the framework — to memory when small, to a temp file when not —
        // so the streams are seekable and the whole request never has to be held in memory at once.
        // They stay open until the command has read them, and are closed on the way out.
        var streams = new List<Stream>();

        try
        {
            return Ok(await _sender.Send(
                new ChangeApplicationStatusCommand(
                    id,
                    request.ToStatus,
                    request.UserComment,
                    request.InternalComment,
                    ToInputs(request.Documents, streams)),
                cancellationToken));
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
    /// Maps the bound form onto the command's inputs. Every stream opened is added to
    /// <paramref name="streams"/> so the caller can close them once the command has read them —
    /// they are read during handling, so closing here would hand the writer a disposed stream.
    /// </summary>
    private static List<AttachedDocumentInput> ToInputs(
        List<AttachedDocumentForm> documents,
        List<Stream> streams)
    {
        var inputs = new List<AttachedDocumentInput>(documents.Count);

        foreach (var document in documents)
        {
            var files = new List<AttachedFileInput>(document.Files.Count);

            foreach (var file in document.Files.Where(f => f.Length > 0))
            {
                var stream = file.OpenReadStream();
                streams.Add(stream);
                files.Add(new AttachedFileInput(file.FileName, file.Length, stream));
            }

            inputs.Add(new AttachedDocumentInput(
                document.NameAr,
                document.NameEn,
                document.IsVisibleToApplicant,
                files,
                document.Fields.Select(field => new AttachedDocumentFieldInput(
                    field.NameAr,
                    field.NameEn,
                    field.FieldType,
                    field.IsRequired,
                    field.SortOrder,
                    field.Value,
                    field.MinLength,
                    field.MaxLength,
                    field.Pattern,
                    field.MinValue,
                    field.MaxValue,
                    field.DateRule,
                    field.MinDate,
                    field.MaxDate,
                    field.Options
                        .Select(option => new AttachedDocumentFieldOptionInput(
                            option.Value,
                            option.LabelAr,
                            option.LabelEn))
                        .ToList()))
                    .ToList()));
        }

        return inputs;
    }

    /// <summary>
    /// Posts a comment. A <c>ForUser</c> comment on an in-progress application also moves it to
    /// MissedInfo; an <c>Internal</c> note never changes state and never reaches the applicant.
    /// </summary>
    [HttpPost("{id:guid}/comments")]
    [RequirePermission(Permissions.ApplicationsReview)]
    [ProducesResponseType(typeof(CommentDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CommentDto>> AddComment(
        Guid id,
        [FromBody] AddCommentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Ok(await _sender.Send(
            new AddAdminCommentCommand(id, request.Body, request.Visibility),
            cancellationToken));
    }

    /// <summary>
    /// Attaches documents without moving the application — the same payload as the transition
    /// above, minus the status. Used when the current status offers no transition to carry them.
    /// </summary>
    [HttpPost("{id:guid}/documents")]
    [Consumes("multipart/form-data")]
    [RequirePermission(Permissions.ApplicationsReview)]
    [RequestSizeLimit(AttachedDocumentLimits.MaxRequestBodyBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = AttachedDocumentLimits.MaxRequestBodyBytes)]
    [EnableRateLimiting(RateLimitPolicies.Uploads)]
    [ProducesResponseType(typeof(AttachDocumentsResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AttachDocumentsResultDto>> AttachDocuments(
        Guid id,
        [FromForm] AttachDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var streams = new List<Stream>();

        try
        {
            return Ok(await _sender.Send(
                new AttachApplicationDocumentsCommand(id, ToInputs(request.Documents, streams)),
                cancellationToken));
        }
        finally
        {
            foreach (var stream in streams)
            {
                await stream.DisposeAsync();
            }
        }
    }

    /// <summary>Attaches a verified deliverable once the application has succeeded.</summary>
    [HttpPost("{id:guid}/results")]
    [RequirePermission(Permissions.ApplicationsAttachResults)]
    [RequestSizeLimit(ApplicationFile.MaxFileSizeBytes + 8192)]
    [EnableRateLimiting(RateLimitPolicies.Uploads)]
    [ProducesResponseType(typeof(ResultFileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ResultFileDto>> UploadResult(
        Guid id,
        [FromForm] UploadResultRequest request,
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

        await using var buffer = new MemoryStream();
        await request.File.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        return Ok(await _sender.Send(
            new UploadResultFileCommand(id, request.File.FileName, request.File.Length, buffer),
            cancellationToken));
    }

    /// <summary>Streams any file on the application, for inline preview in the admin panel.</summary>
    [HttpGet("{id:guid}/files/{fileId:guid}")]
    [RequirePermission(Permissions.ApplicationsView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadFile(
        Guid id,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        var download = await _sender.Send(new DownloadAdminFileQuery(id, fileId), cancellationToken);
        return File(download.Content, download.ContentType, download.FileName);
    }
}

public sealed record ChangeStatusRequest(
    ApplicationStatus ToStatus,
    string? UserComment,
    string? InternalComment);

/// <summary>
/// The multipart form behind a status change that carries documents. Bound by name, so the client
/// posts <c>Documents[0].Files</c>, <c>Documents[0].Fields[1].Value</c> and so on. Classes with
/// settable properties rather than records: form binding needs a parameterless constructor.
/// </summary>
public sealed class ChangeStatusWithDocumentsRequest
{
    public ApplicationStatus ToStatus { get; set; }

    public string? UserComment { get; set; }

    public string? InternalComment { get; set; }

    public List<AttachedDocumentForm> Documents { get; set; } = [];
}

/// <summary>The same documents, with no transition to carry them.</summary>
public sealed class AttachDocumentsRequest
{
    public List<AttachedDocumentForm> Documents { get; set; } = [];
}

public sealed class AttachedDocumentForm
{
    public string NameAr { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    /// <summary>Defaults to withheld: an omitted flag must never publish a document by accident.</summary>
    public bool IsVisibleToApplicant { get; set; }

    public List<IFormFile> Files { get; set; } = [];

    public List<AttachedDocumentFieldForm> Fields { get; set; } = [];
}

public sealed class AttachedDocumentFieldForm
{
    public string NameAr { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public RequiredFieldType FieldType { get; set; }

    public bool IsRequired { get; set; }

    public int SortOrder { get; set; }

    public string? Value { get; set; }

    public int? MinLength { get; set; }

    public int? MaxLength { get; set; }

    public string? Pattern { get; set; }

    public decimal? MinValue { get; set; }

    public decimal? MaxValue { get; set; }

    public RequiredFieldDateRule DateRule { get; set; }

    public DateOnly? MinDate { get; set; }

    public DateOnly? MaxDate { get; set; }

    public List<AttachedDocumentFieldOptionForm> Options { get; set; } = [];
}

public sealed class AttachedDocumentFieldOptionForm
{
    public string Value { get; set; } = string.Empty;

    public string LabelAr { get; set; } = string.Empty;

    public string LabelEn { get; set; } = string.Empty;
}

public sealed record AddCommentRequest(string Body, CommentVisibility Visibility);

public sealed class UploadResultRequest
{
    public IFormFile? File { get; set; }
}
