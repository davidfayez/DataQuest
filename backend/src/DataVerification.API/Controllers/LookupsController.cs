using Asp.Versioning;
using DataVerification.API.Authorization;
using DataVerification.Application.Features.Lookups;
using DataVerification.Application.Features.Lookups.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataVerification.API.Controllers;

/// <summary>
/// The applicant-facing cascade. Every level below the country is filtered server-side by the
/// verification country locked onto the caller's order — the client cannot widen that scope.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}")]
[Authorize(Policy = PolicyNames.Applicant)]
[Produces("application/json")]
public sealed class LookupsController : ControllerBase
{
    private readonly ISender _sender;

    public LookupsController(ISender sender) => _sender = sender;

    /// <summary>Active countries available for order setup.</summary>
    [HttpGet("countries")]
    [ProducesResponseType(typeof(IReadOnlyList<CountryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CountryDto>>> GetCountries(
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetCountriesQuery(), cancellationToken));

    /// <summary>
    /// A reference file an administrator attached to a required document, shown beside the upload.
    /// The same for every applicant, so any signed-in applicant may read it — but only while its
    /// document and service are active.
    /// </summary>
    [HttpGet("required-files/samples/{sampleId:guid}/file")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRequiredFileSample(
        Guid sampleId,
        CancellationToken cancellationToken)
    {
        var file = await _sender.Send(
            new Application.Features.Lookups.Admin.GetRequiredFileSampleQuery(sampleId, ForApplicant: true),
            cancellationToken);

        Response.Headers.CacheControl = "private, max-age=300";
        return File(file.Content, file.ContentType, file.FileName);
    }

    /// <summary>The currencies the given country exposes.</summary>
    [HttpGet("countries/{countryId:guid}/currencies")]
    [ProducesResponseType(typeof(IReadOnlyList<CurrencyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<CurrencyDto>>> GetCountryCurrencies(
        Guid countryId,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetCountryCurrenciesQuery(countryId), cancellationToken));

    /// <summary>
    /// The addressees offered on a new application. A suggestion list: an applicant may submit an
    /// addressee that is not on it.
    /// </summary>
    [HttpGet("addressees")]
    [ProducesResponseType(typeof(IReadOnlyList<AddresseeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AddresseeDto>>> GetAddressees(
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetAddresseesQuery(), cancellationToken));

    /// <summary>Transaction types available in the order's verification country.</summary>
    [HttpGet("transaction-types")]
    [ProducesResponseType(typeof(IReadOnlyList<TransactionTypeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<IReadOnlyList<TransactionTypeDto>>> GetTransactionTypes(
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetTransactionTypesQuery(), cancellationToken));

    /// <summary>Sub-transaction types belonging to a transaction type.</summary>
    [HttpGet("transaction-types/{transactionTypeId:guid}/sub-types")]
    [ProducesResponseType(typeof(IReadOnlyList<SubTransactionTypeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<SubTransactionTypeDto>>> GetSubTransactionTypes(
        Guid transactionTypeId,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetSubTransactionTypesQuery(transactionTypeId), cancellationToken));

    /// <summary>Authorities mapped to the sub-type and operating in the order's country.</summary>
    [HttpGet("sub-types/{subTransactionTypeId:guid}/authorities")]
    [ProducesResponseType(typeof(IReadOnlyList<VerificationAuthorityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<VerificationAuthorityDto>>> GetAuthorities(
        Guid subTransactionTypeId,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetAuthoritiesQuery(subTransactionTypeId), cancellationToken));

    /// <summary>Priced services offered by an authority for a sub-type, including required files.</summary>
    [HttpGet("authorities/{authorityId:guid}/service-types")]
    [ProducesResponseType(typeof(IReadOnlyList<ServiceTypeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ServiceTypeDto>>> GetServiceTypes(
        Guid authorityId,
        [FromQuery] Guid subTransactionTypeId,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(
            new GetServiceTypesQuery(authorityId, subTransactionTypeId),
            cancellationToken));
}
