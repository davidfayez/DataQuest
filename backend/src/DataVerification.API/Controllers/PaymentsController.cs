using Asp.Versioning;
using DataVerification.API.Authorization;
using DataVerification.API.Infrastructure;
using DataVerification.Application.Common.Models;
using DataVerification.Application.Features.Payments;
using DataVerification.Application.Features.Payments.Queries;
using DataVerification.Application.Features.Wallets;
using DataVerification.Application.Features.Wallets.Commands;
using DataVerification.Application.Features.Wallets.Queries;
using DataVerification.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DataVerification.API.Controllers;

/// <summary>Applicant wallet and payments.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}")]
[Authorize(Policy = PolicyNames.Applicant)]
[Produces("application/json")]
public sealed class PaymentsController : ControllerBase
{
    private readonly ISender _sender;

    public PaymentsController(ISender sender) => _sender = sender;

    /// <summary>Wallet balance and a page of the ledger.</summary>
    [HttpGet("orders/me/wallet")]
    [ProducesResponseType(typeof(WalletStatementDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WalletStatementDto>> GetMyWallet(
        [FromQuery] GetMyWalletQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(query, cancellationToken));

    /// <summary>
    /// Pays for several applications at once. The batch is atomic: either every application is
    /// settled and the wallet debited once, or nothing changes.
    /// </summary>
    [HttpPost("payments")]
    [ProducesResponseType(typeof(PaymentResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PaymentResultDto>> Pay(
        [FromBody] PayApplicationsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Ok(await _sender.Send(
            new PayApplicationsCommand(request.ApplicationIds, request.CurrencyId),
            cancellationToken));
    }

    /// <summary>
    /// What the selected applications would cost from each of the order's balances, so the pay
    /// screen can offer the ones that cover it.
    /// </summary>
    [HttpGet("payments/quote")]
    [ProducesResponseType(typeof(PaymentQuoteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PaymentQuoteDto>> Quote(
        [FromQuery] List<Guid> applicationIds,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetPaymentQuoteQuery(applicationIds), cancellationToken));

    /// <summary>Refunds a paid application that has not been started. Returns 409 otherwise.</summary>
    [HttpPost("applications/{id:guid}/refund")]
    [ProducesResponseType(typeof(RefundResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RefundResultDto>> Refund(
        Guid id,
        [FromBody] RefundApplicationRequest? request,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new RefundApplicationCommand(id, request?.Note), cancellationToken));

    /// <summary>The caller's deposit and withdrawal requests, newest first.</summary>
    [HttpGet("orders/me/wallet/requests")]
    [ProducesResponseType(typeof(PagedResult<WalletRequestDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<WalletRequestDto>>> GetMyWalletRequests(
        [FromQuery] GetMyWalletRequestsQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(query, cancellationToken));

    /// <summary>
    /// Asks an operator to move money in or out of the wallet. A withdrawal holds the amount
    /// immediately, so 422 comes back when the balance will not cover it.
    /// </summary>
    [HttpPost("orders/me/wallet/requests")]
    [ProducesResponseType(typeof(WalletRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<WalletRequestDto>> CreateWalletRequest(
        [FromBody] CreateWalletRequestBody request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Ok(await _sender.Send(
            new CreateWalletRequestCommand(request.Type, request.Amount, request.Note, request.CurrencyId),
            cancellationToken));
    }

    /// <summary>
    /// The payment methods this order may use, already narrowed to its verification country and
    /// wallet currency. Each one carries the flags the deposit form is built from, so the client
    /// never decides what a provider requires.
    /// </summary>
    [HttpGet("orders/me/payment-methods")]
    [ProducesResponseType(typeof(IReadOnlyList<PaymentMethodOptionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PaymentMethodOptionDto>>> GetPaymentMethods(
        [FromQuery] Guid? currencyId,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetOrderPaymentMethodsQuery(currencyId), cancellationToken));

    /// <summary>The scannable code for one receiving account, if the order may pay through it.</summary>
    [HttpGet("orders/me/payment-methods/accounts/{accountId:guid}/barcode")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAccountBarcode(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var download = await _sender.Send(new GetOrderAccountBarcodeQuery(accountId), cancellationToken);
        return File(download.Content, download.ContentType);
    }

    /// <summary>
    /// Raises a top-up through a payment method, with the transfer reference and proof attached in
    /// the same call. Multipart rather than JSON because the receipt is part of the request, not a
    /// follow-up: a pending claim with no evidence behind it is not something to leave lying around.
    ///
    /// 409 comes back when the reference has already been submitted, when the method is not
    /// available to this order, or when something the method's type requires is missing.
    /// </summary>
    [HttpPost("orders/me/wallet/requests/deposit")]
    [RequestSizeLimit(
        (CreateDepositRequestCommandHandler.MaxProofBytes * CreateDepositRequestCommandHandler.MaxProofFiles)
        + (Domain.Entities.ApplicationFile.MaxFileSizeBytes * CreateDepositRequestCommandHandler.MaxDocumentFiles)
        + 65536)]
    [EnableRateLimiting(RateLimitPolicies.Uploads)]
    [ProducesResponseType(typeof(WalletRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WalletRequestDto>> CreateDepositRequest(
        [FromForm] CreateDepositRequestBody request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Every upload is buffered to a seekable stream so its signature can be read and the same
        // bytes then written to storage from the start.
        var buffers = new List<MemoryStream>();

        try
        {
            var uploads = new List<DepositProofUpload>();

            foreach (var file in request.Files.Where(f => f.Length > 0))
            {
                var buffer = new MemoryStream();
                buffers.Add(buffer);
                await file.CopyToAsync(buffer, cancellationToken);
                buffer.Position = 0;
                uploads.Add(new DepositProofUpload(file.FileName, file.Length, buffer));
            }

            var documentUploads = new List<DepositDocumentUpload>();

            foreach (var document in request.Documents)
            {
                foreach (var file in document.Files.Where(f => f.Length > 0))
                {
                    var buffer = new MemoryStream();
                    buffers.Add(buffer);
                    await file.CopyToAsync(buffer, cancellationToken);
                    buffer.Position = 0;
                    documentUploads.Add(new DepositDocumentUpload(
                        document.RequiredFileId, file.FileName, file.Length, buffer));
                }
            }

            var values = request.Values
                .Select(value => new DepositDocumentValue(value.FieldId, value.Value))
                .ToList();

            return Ok(await _sender.Send(
                new CreateDepositRequestCommand(
                    request.PaymentMethodId,
                    request.PaymentMethodAccountId,
                    request.Amount,
                    request.ReferenceNumber,
                    request.Note,
                    uploads,
                    documentUploads,
                    values,
                    request.CurrencyId),
                cancellationToken));
        }
        finally
        {
            foreach (var buffer in buffers)
            {
                await buffer.DisposeAsync();
            }
        }
    }

    /// <summary>Downloads one of the caller's own attached receipts.</summary>
    /// <summary>One of the applicant's own requests, for its details page.</summary>
    [HttpGet("orders/me/wallet/requests/{id:guid}")]
    [ProducesResponseType(typeof(WalletRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WalletRequestDto>> GetMyWalletRequest(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetMyWalletRequestQuery(id), cancellationToken));

    [HttpGet("orders/me/wallet/requests/{id:guid}/files/{fileId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRequestFile(
        Guid id,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        var download = await _sender.Send(new GetWalletRequestFileQuery(id, fileId), cancellationToken);
        return File(download.Content, download.ContentType, download.FileName);
    }

    /// <summary>Calls off a request nobody has decided yet, releasing any held funds.</summary>
    [HttpPost("orders/me/wallet/requests/{id:guid}/cancel")]
    [ProducesResponseType(typeof(WalletRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WalletRequestDto>> CancelWalletRequest(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new CancelWalletRequestCommand(id), cancellationToken));

    /// <summary>
    /// Credits the caller's own wallet without an operator. Available only where
    /// <c>Wallet:AllowSimulatedDeposits</c> is enabled; 403 everywhere else.
    /// </summary>
    [HttpPost("orders/me/wallet/simulate-deposit")]
    [ProducesResponseType(typeof(WalletSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<WalletSummaryDto>> SimulateDeposit(
        [FromBody] SimulateDepositBody request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Ok(await _sender.Send(new SimulateDepositCommand(request.Amount, request.CurrencyId), cancellationToken));
    }
}

/// <param name="CurrencyId">Which balance pays; see PayApplicationsCommand.</param>
public sealed record PayApplicationsRequest(IReadOnlyList<Guid> ApplicationIds, Guid? CurrencyId = null);

public sealed record RefundApplicationRequest(string? Note);

/// <param name="CurrencyId">Which balance; the order's main currency when omitted.</param>
public sealed record CreateWalletRequestBody(WalletRequestType Type, decimal Amount, string? Note, Guid? CurrencyId = null);

public sealed record SimulateDepositBody(decimal Amount, Guid? CurrencyId = null);

/// <summary>
/// The deposit form, posted as multipart. <see cref="Files"/> carries the receipts; which of the
/// other fields are required is decided by the chosen method's type, not by this shape.
/// </summary>
public sealed class CreateDepositRequestBody
{
    /// <summary>Which balance the money is for; the order's main currency when omitted.</summary>
    public Guid? CurrencyId { get; set; }

    public Guid PaymentMethodId { get; set; }

    /// <summary>Which receiving account was paid. Required by types that configure accounts.</summary>
    public Guid? PaymentMethodAccountId { get; set; }

    public decimal Amount { get; set; }

    public string? ReferenceNumber { get; set; }

    public string? Note { get; set; }

    public List<IFormFile> Files { get; set; } = [];

    /// <summary>
    /// Files for the method's required documents, one entry per document — posted as
    /// <c>documents[0].requiredFileId</c> and <c>documents[0].files</c>.
    /// </summary>
    public List<DepositDocumentForm> Documents { get; set; } = [];

    /// <summary>
    /// The details typed beside those documents — posted as <c>values[0].fieldId</c> and
    /// <c>values[0].value</c>.
    /// </summary>
    public List<DepositValueForm> Values { get; set; } = [];
}

/// <summary>The files sent for one of the method's required documents.</summary>
public sealed class DepositDocumentForm
{
    public Guid RequiredFileId { get; set; }

    public List<IFormFile> Files { get; set; } = [];
}

/// <summary>One detail typed beside a required document.</summary>
public sealed class DepositValueForm
{
    public Guid FieldId { get; set; }

    public string? Value { get; set; }
}
