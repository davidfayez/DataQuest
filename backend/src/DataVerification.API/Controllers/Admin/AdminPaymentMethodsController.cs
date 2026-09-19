using Asp.Versioning;
using DataVerification.API.Authorization;
using DataVerification.API.Infrastructure;
using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Common.Models;
using DataVerification.Application.Features.Lookups.Admin;
using DataVerification.Application.Features.Payments;
using DataVerification.Application.Features.Payments.Admin;
using DataVerification.Domain.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DataVerification.API.Controllers.Admin;

/// <summary>
/// Configuration of the channels applicants may add funds through: the methods themselves, their
/// receiving accounts, and the payment-type catalogue that decides what either one needs.
///
/// All of it sits behind one permission set. The three are edited on a single screen and are
/// meaningless apart — a role that could add a method but not its accounts could only ever produce
/// a method nobody can pay through.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/payment-methods")]
[Produces("application/json")]
public sealed class AdminPaymentMethodsController : ControllerBase
{
    private readonly ISender _sender;
    private readonly ICurrentUser _currentUser;

    public AdminPaymentMethodsController(ISender sender, ICurrentUser currentUser)
    {
        _sender = sender;
        _currentUser = currentUser;
    }

    /// <summary>
    /// The write endpoints are upserts, so create and update cannot be told apart by a route
    /// attribute; the required permission is resolved from the id at request time.
    /// </summary>
    private void RequireUpsert(Guid? id)
    {
        var required = id is null || id == Guid.Empty
            ? Permissions.PaymentMethodsCreate
            : Permissions.PaymentMethodsUpdate;

        if (!_currentUser.Permissions.Contains(required))
        {
            throw new ForbiddenAccessException($"This action requires the '{required}' permission.");
        }
    }

    // ------------------------------------------------------------- Methods

    [HttpGet]
    [RequirePermission(Permissions.PaymentMethodsView)]
    [ProducesResponseType(typeof(PagedResult<AdminPaymentMethodDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AdminPaymentMethodDto>>> List(
        [FromQuery] ListPaymentMethodsQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(query, cancellationToken));

    /// <summary>
    /// One method with its countries, currencies and accounts. The editor opens on this rather than
    /// hunting the row out of a list page, so a bookmarked edit URL works.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.PaymentMethodsView)]
    [ProducesResponseType(typeof(AdminPaymentMethodDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminPaymentMethodDto>> Get(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetPaymentMethodQuery(id), cancellationToken));

    /// <summary>
    /// Saves the method and its whole configuration in one call — countries, currencies and the
    /// account list together, because a method saved without them is not a method anyone can pay.
    /// </summary>
    /// <summary>
    /// One stored gateway secret, in full, so the method's eye button can show it. Only for callers
    /// who may change the method, never cached, and every read is audited.
    /// </summary>
    [HttpGet("{id:guid}/secrets/{key}")]
    [RequirePermission(Permissions.PaymentMethodsUpdate)]
    [ProducesResponseType(typeof(StoredGatewaySecretDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StoredGatewaySecretDto>> GetSecret(
        Guid id,
        string key,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        return Ok(await _sender.Send(new GetPaymentMethodSecretQuery(id, key), cancellationToken));
    }

    [HttpPost]
    [ProducesResponseType(typeof(AdminPaymentMethodDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminPaymentMethodDto>> Upsert(
        [FromBody] UpsertPaymentMethodCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireUpsert(command.Id);
        return Ok(await _sender.Send(command, cancellationToken));
    }

    /// <summary>Removes the method, or deactivates it when requests already quote it.</summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.PaymentMethodsDelete)]
    [ProducesResponseType(typeof(LookupDeleteOutcome), StatusCodes.Status200OK)]
    public async Task<ActionResult<LookupDeleteOutcome>> Delete(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new DeletePaymentMethodCommand(id), cancellationToken));

    // ------------------------------------------------------------ Barcodes

    /// <summary>Attaches the scannable image for one receiving account. JPEG or PNG, up to 2 MB.</summary>
    [HttpPost("accounts/{accountId:guid}/barcode")]
    [RequirePermission(Permissions.PaymentMethodsUpdate)]
    [RequestSizeLimit(PaymentMethodBarcodeHandlers.MaxBarcodeBytes + 8192)]
    [EnableRateLimiting(RateLimitPolicies.Uploads)]
    [ProducesResponseType(typeof(PaymentMethodAccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PaymentMethodAccountDto>> UploadBarcode(
        Guid accountId,
        [FromForm] UploadBarcodeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.File is null || request.File.Length == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "No file supplied",
                Detail = "Attach an image to the 'file' form field.",
            });
        }

        // Buffered to a seekable stream so the signature can be read and the same bytes then
        // written to storage from the start.
        await using var buffer = new MemoryStream();
        await request.File.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        return Ok(await _sender.Send(
            new UploadAccountBarcodeCommand(
                accountId,
                request.File.FileName,
                request.File.Length,
                buffer),
            cancellationToken));
    }

    [HttpDelete("accounts/{accountId:guid}/barcode")]
    [RequirePermission(Permissions.PaymentMethodsUpdate)]
    [ProducesResponseType(typeof(PaymentMethodAccountDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PaymentMethodAccountDto>> DeleteBarcode(
        Guid accountId,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new DeleteAccountBarcodeCommand(accountId), cancellationToken));

    /// <summary>The barcode bytes, so the editor can show what was uploaded.</summary>
    [HttpGet("accounts/{accountId:guid}/barcode")]
    [RequirePermission(Permissions.PaymentMethodsView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBarcode(Guid accountId, CancellationToken cancellationToken)
    {
        var download = await _sender.Send(new GetAccountBarcodeQuery(accountId), cancellationToken);
        return File(download.Content, download.ContentType);
    }

    // --------------------------------------------------------------- Types

    // ------------------------------------------- Reference files on required documents

    /// <summary>
    /// Attaches a labelled reference file — a sample or a template — to one of a method's required
    /// documents. The document must already be saved, since the file belongs to it.
    /// </summary>
    [HttpPost("required-files/{requiredFileId:guid}/samples")]
    [RequirePermission(Permissions.PaymentMethodsUpdate)]
    [RequestSizeLimit(Domain.Entities.RequiredFileSampleLimits.MaxFileSizeBytes + 16384)]
    [EnableRateLimiting(RateLimitPolicies.Uploads)]
    [ProducesResponseType(typeof(Application.Features.Lookups.RequiredFileSampleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Application.Features.Lookups.RequiredFileSampleDto>> UploadRequiredFileSample(
        Guid requiredFileId,
        [FromForm] UploadRequiredFileSampleRequest request,
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

        // Buffered to a seekable stream so the signature can be read and the same bytes then
        // written to storage from the start.
        await using var buffer = new MemoryStream();
        await request.File.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        return Ok(await _sender.Send(
            new UploadRequiredFileSampleCommand(
                requiredFileId,
                request.LabelAr,
                request.LabelEn,
                buffer,
                request.File.FileName,
                request.File.Length,
                RequiredDocumentOwner.PaymentMethod),
            cancellationToken));
    }

    /// <summary>Removes a reference file from a method's document, and its bytes.</summary>
    [HttpDelete("samples/{sampleId:guid}")]
    [RequirePermission(Permissions.PaymentMethodsUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteRequiredFileSample(
        Guid sampleId,
        CancellationToken cancellationToken)
    {
        await _sender.Send(
            new DeleteRequiredFileSampleCommand(sampleId, RequiredDocumentOwner.PaymentMethod),
            cancellationToken);
        return NoContent();
    }

    /// <summary>A reference file's bytes, for the editor's preview.</summary>
    [HttpGet("samples/{sampleId:guid}/file")]
    [RequirePermission(Permissions.PaymentMethodsView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRequiredFileSample(
        Guid sampleId,
        CancellationToken cancellationToken)
    {
        var file = await _sender.Send(
            new GetRequiredFileSampleQuery(sampleId, ForApplicant: false, RequiredDocumentOwner.PaymentMethod),
            cancellationToken);

        Response.Headers.CacheControl = "private, no-store";
        return File(file.Content, file.ContentType, file.FileName);
    }

    // --------------------------------------------------------------- Types

    [HttpGet("types")]
    [RequirePermission(Permissions.PaymentMethodsView)]
    [ProducesResponseType(typeof(PagedResult<PaymentMethodTypeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<PaymentMethodTypeDto>>> ListTypes(
        [FromQuery] ListPaymentMethodTypesQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(query, cancellationToken));

    [HttpPost("types")]
    [ProducesResponseType(typeof(PaymentMethodTypeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PaymentMethodTypeDto>> UpsertType(
        [FromBody] UpsertPaymentMethodTypeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireUpsert(command.Id);
        return Ok(await _sender.Send(command, cancellationToken));
    }

    [HttpDelete("types/{id:guid}")]
    [RequirePermission(Permissions.PaymentMethodsDelete)]
    [ProducesResponseType(typeof(LookupDeleteOutcome), StatusCodes.Status200OK)]
    public async Task<ActionResult<LookupDeleteOutcome>> DeleteType(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new DeletePaymentMethodTypeCommand(id), cancellationToken));
}

/// <param name="File">The JPEG or PNG barcode to attach to the account.</param>
public sealed record UploadBarcodeRequest(IFormFile? File);
