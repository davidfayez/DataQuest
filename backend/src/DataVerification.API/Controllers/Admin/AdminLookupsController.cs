using Asp.Versioning;
using DataVerification.API.Authorization;
using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Common.Models;
using DataVerification.Application.Features.Lookups;
using DataVerification.Application.Features.Lookups.Admin;
using DataVerification.Domain.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace DataVerification.API.Controllers.Admin;

/// <summary>
/// Admin CRUD for every lookup. Each action is gated by its own permission, so an admin who may
/// view a lookup but not delete it is refused by the server regardless of what the sidebar shows.
///
/// The write endpoints are upserts (one body, id present means update), so create and update cannot
/// be told apart by a route attribute. <see cref="RequireUpsert"/> resolves the right permission
/// from the id at request time; reads and deletes stay attribute-gated.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/lookups")]
[Produces("application/json")]
public sealed class AdminLookupsController : ControllerBase
{
    private readonly ISender _sender;
    private readonly ICurrentUser _currentUser;

    public AdminLookupsController(ISender sender, ICurrentUser currentUser)
    {
        _sender = sender;
        _currentUser = currentUser;
    }

    /// <summary>Requires the create or the update permission depending on whether this is a new row.</summary>
    private void RequireUpsert(Guid? id, string createPermission, string updatePermission)
    {
        var required = id is null || id == Guid.Empty ? createPermission : updatePermission;
        if (!_currentUser.Permissions.Contains(required))
        {
            throw new ForbiddenAccessException($"This action requires the '{required}' permission.");
        }
    }

    // ------------------------------------------------------------ Countries

    [HttpGet("countries")]
    [RequirePermission(Permissions.CountriesView)]
    [ProducesResponseType(typeof(PagedResult<CountryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<CountryDto>>> ListCountries(
        [FromQuery] ListCountriesQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(query, cancellationToken));

    [HttpPost("countries")]
    [ProducesResponseType(typeof(CountryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CountryDto>> UpsertCountry(
        [FromBody] UpsertCountryCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireUpsert(command.Id, Permissions.CountriesCreate, Permissions.CountriesUpdate);
        return Ok(await _sender.Send(command, cancellationToken));
    }

    [HttpDelete("countries/{id:guid}")]
    [RequirePermission(Permissions.CountriesDelete)]
    [ProducesResponseType(typeof(LookupDeleteOutcome), StatusCodes.Status200OK)]
    public async Task<ActionResult<LookupDeleteOutcome>> DeleteCountry(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new DeleteCountryCommand(id), cancellationToken));

    /// <summary>
    /// The currencies currently mapped to a country. Separate from the applicant-facing endpoint,
    /// which is scoped to the Applicant realm and therefore unreachable with an admin token.
    /// </summary>
    [HttpGet("countries/{id:guid}/currencies")]
    [RequirePermission(Permissions.CountriesView)]
    [ProducesResponseType(typeof(IReadOnlyList<CurrencyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<CurrencyDto>>> GetCountryCurrencies(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetCountryCurrenciesForAdminQuery(id), cancellationToken));

    /// <summary>Replaces the set of currencies a country exposes.</summary>
    [HttpPut("countries/{id:guid}/currencies")]
    [RequirePermission(Permissions.CountriesUpdate)]
    [ProducesResponseType(typeof(IReadOnlyList<CurrencyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<IReadOnlyList<CurrencyDto>>> SetCountryCurrencies(
        Guid id,
        [FromBody] SetCountryCurrenciesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Ok(await _sender.Send(
            new SetCountryCurrenciesCommand(id, request.CurrencyIds, request.DefaultCurrencyId),
            cancellationToken));
    }

    // ----------------------------------------------------------- Addressees

    /// <summary>
    /// The list offered on a new application's "Addressed to" field. It only decides what is
    /// suggested: an application stores the text it was addressed to, so editing this list never
    /// changes an application that has already been created.
    /// </summary>
    [HttpGet("addressees")]
    [RequirePermission(Permissions.AddresseesView)]
    [ProducesResponseType(typeof(PagedResult<AddresseeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AddresseeDto>>> ListAddressees(
        [FromQuery] ListAddresseesQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(query, cancellationToken));

    [HttpPost("addressees")]
    [ProducesResponseType(typeof(AddresseeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AddresseeDto>> UpsertAddressee(
        [FromBody] UpsertAddresseeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireUpsert(command.Id, Permissions.AddresseesCreate, Permissions.AddresseesUpdate);
        return Ok(await _sender.Send(command, cancellationToken));
    }

    [HttpDelete("addressees/{id:guid}")]
    [RequirePermission(Permissions.AddresseesDelete)]
    [ProducesResponseType(typeof(LookupDeleteOutcome), StatusCodes.Status200OK)]
    public async Task<ActionResult<LookupDeleteOutcome>> DeleteAddressee(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new DeleteAddresseeCommand(id), cancellationToken));

    // ----------------------------------------------------------- Currencies

    [HttpGet("currencies")]
    [RequirePermission(Permissions.CurrenciesView)]
    [ProducesResponseType(typeof(PagedResult<CurrencyDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<CurrencyDto>>> ListCurrencies(
        [FromQuery] ListCurrenciesQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(query, cancellationToken));

    [HttpPost("currencies")]
    [ProducesResponseType(typeof(CurrencyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CurrencyDto>> UpsertCurrency(
        [FromBody] UpsertCurrencyCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireUpsert(command.Id, Permissions.CurrenciesCreate, Permissions.CurrenciesUpdate);
        return Ok(await _sender.Send(command, cancellationToken));
    }

    [HttpDelete("currencies/{id:guid}")]
    [RequirePermission(Permissions.CurrenciesDelete)]
    [ProducesResponseType(typeof(LookupDeleteOutcome), StatusCodes.Status200OK)]
    public async Task<ActionResult<LookupDeleteOutcome>> DeleteCurrency(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new DeleteCurrencyCommand(id), cancellationToken));

    // ---------------------------------------------------- Transaction types

    [HttpGet("transaction-types")]
    [RequirePermission(Permissions.TransactionTypesView)]
    [ProducesResponseType(typeof(PagedResult<TransactionTypeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<TransactionTypeDto>>> ListTransactionTypes(
        [FromQuery] ListTransactionTypesQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(query, cancellationToken));

    [HttpPost("transaction-types")]
    [ProducesResponseType(typeof(TransactionTypeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TransactionTypeDto>> UpsertTransactionType(
        [FromBody] UpsertTransactionTypeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireUpsert(command.Id, Permissions.TransactionTypesCreate, Permissions.TransactionTypesUpdate);
        return Ok(await _sender.Send(command, cancellationToken));
    }

    [HttpDelete("transaction-types/{id:guid}")]
    [RequirePermission(Permissions.TransactionTypesDelete)]
    [ProducesResponseType(typeof(LookupDeleteOutcome), StatusCodes.Status200OK)]
    public async Task<ActionResult<LookupDeleteOutcome>> DeleteTransactionType(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new DeleteTransactionTypeCommand(id), cancellationToken));

    // ------------------------------------------------ Sub-transaction types

    [HttpGet("sub-transaction-types")]
    [RequirePermission(Permissions.SubTransactionTypesView)]
    [ProducesResponseType(typeof(PagedResult<SubTransactionTypeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<SubTransactionTypeDto>>> ListSubTransactionTypes(
        [FromQuery] ListSubTransactionTypesQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(query, cancellationToken));

    [HttpPost("sub-transaction-types")]
    [ProducesResponseType(typeof(SubTransactionTypeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SubTransactionTypeDto>> UpsertSubTransactionType(
        [FromBody] UpsertSubTransactionTypeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireUpsert(
            command.Id,
            Permissions.SubTransactionTypesCreate,
            Permissions.SubTransactionTypesUpdate);
        return Ok(await _sender.Send(command, cancellationToken));
    }

    [HttpDelete("sub-transaction-types/{id:guid}")]
    [RequirePermission(Permissions.SubTransactionTypesDelete)]
    [ProducesResponseType(typeof(LookupDeleteOutcome), StatusCodes.Status200OK)]
    public async Task<ActionResult<LookupDeleteOutcome>> DeleteSubTransactionType(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new DeleteSubTransactionTypeCommand(id), cancellationToken));

    // -------------------------------------------------------- Authorities

    [HttpGet("authorities")]
    [RequirePermission(Permissions.AuthoritiesView)]
    [ProducesResponseType(typeof(PagedResult<VerificationAuthorityDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<VerificationAuthorityDto>>> ListAuthorities(
        [FromQuery] ListAuthoritiesQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(query, cancellationToken));

    [HttpPost("authorities")]
    [ProducesResponseType(typeof(VerificationAuthorityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<VerificationAuthorityDto>> UpsertAuthority(
        [FromBody] UpsertAuthorityCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireUpsert(command.Id, Permissions.AuthoritiesCreate, Permissions.AuthoritiesUpdate);
        return Ok(await _sender.Send(command, cancellationToken));
    }

    [HttpDelete("authorities/{id:guid}")]
    [RequirePermission(Permissions.AuthoritiesDelete)]
    [ProducesResponseType(typeof(LookupDeleteOutcome), StatusCodes.Status200OK)]
    public async Task<ActionResult<LookupDeleteOutcome>> DeleteAuthority(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new DeleteAuthorityCommand(id), cancellationToken));

    // -------------------------------------------------------- Service types

    [HttpGet("service-types")]
    [RequirePermission(Permissions.ServiceTypesView)]
    [ProducesResponseType(typeof(PagedResult<ServiceTypeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ServiceTypeDto>>> ListServiceTypes(
        [FromQuery] ListServiceTypesQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(query, cancellationToken));

    [HttpPost("service-types")]
    [ProducesResponseType(typeof(ServiceTypeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ServiceTypeDto>> UpsertServiceType(
        [FromBody] UpsertServiceTypeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireUpsert(command.Id, Permissions.ServiceTypesCreate, Permissions.ServiceTypesUpdate);
        return Ok(await _sender.Send(command, cancellationToken));
    }

    [HttpDelete("service-types/{id:guid}")]
    [RequirePermission(Permissions.ServiceTypesDelete)]
    [ProducesResponseType(typeof(LookupDeleteOutcome), StatusCodes.Status200OK)]
    public async Task<ActionResult<LookupDeleteOutcome>> DeleteServiceType(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new DeleteServiceTypeCommand(id), cancellationToken));

    // ------------------------------------------- Reference files on required documents

    /// <summary>
    /// Attaches a labelled reference file — a sample or a template — to one required document.
    /// The document must already be saved, since the file belongs to it.
    /// </summary>
    [HttpPost("service-types/required-files/{requiredFileId:guid}/samples")]
    [RequirePermission(Permissions.ServiceTypesUpdate)]
    [RequestSizeLimit(Domain.Entities.RequiredFileSampleLimits.MaxFileSizeBytes + 16384)]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(RateLimitPolicies.Uploads)]
    [ProducesResponseType(typeof(RequiredFileSampleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RequiredFileSampleDto>> UploadRequiredFileSample(
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
                request.File.Length),
            cancellationToken));
    }

    /// <summary>Removes a reference file, and its bytes.</summary>
    [HttpDelete("service-types/samples/{sampleId:guid}")]
    [RequirePermission(Permissions.ServiceTypesUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteRequiredFileSample(
        Guid sampleId,
        CancellationToken cancellationToken)
    {
        await _sender.Send(new DeleteRequiredFileSampleCommand(sampleId), cancellationToken);
        return NoContent();
    }

    /// <summary>A reference file's bytes, for the panel's preview.</summary>
    [HttpGet("service-types/samples/{sampleId:guid}/file")]
    [RequirePermission(Permissions.ServiceTypesView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRequiredFileSample(
        Guid sampleId,
        CancellationToken cancellationToken)
    {
        var file = await _sender.Send(
            new GetRequiredFileSampleQuery(sampleId, ForApplicant: false, RequiredDocumentOwner.ServiceType),
            cancellationToken);

        Response.Headers.CacheControl = "private, no-store";
        return File(file.Content, file.ContentType, file.FileName);
    }
}

/// <param name="CurrencyIds">Every currency the country offers.</param>
/// <param name="DefaultCurrencyId">
/// The main one — what orders in the country are set up in. Optional: without it the current main
/// currency is kept if still offered, or one is picked.
/// </param>
public sealed record SetCountryCurrenciesRequest(
    IReadOnlyList<Guid> CurrencyIds,
    Guid? DefaultCurrencyId = null);

/// <param name="File">A PDF, JPG, PNG, Word or Excel file.</param>
/// <param name="LabelAr">What the file is, in Arabic.</param>
/// <param name="LabelEn">What the file is, in English.</param>
public sealed record UploadRequiredFileSampleRequest(IFormFile? File, string? LabelAr, string? LabelEn);
