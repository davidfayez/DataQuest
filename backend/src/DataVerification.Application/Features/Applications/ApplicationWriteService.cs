using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Common;
using DataVerification.Application.Features.Lookups;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Applications;

/// <param name="LanguageType">Arabic and English rows are both mandatory on every application.</param>
public sealed record ApplicationNameInput(
    NameLanguageType LanguageType,
    string FirstName,
    string? MiddleName,
    string LastName);

public sealed record ApplicationServiceInput(
    Guid ServiceTypeId,
    int Quantity,
    string LanguageCode,
    bool IsExpress);

/// <summary>
/// The write path shared by create and update. It validates the cascade against the order's
/// country and rebuilds the child collections with server-side pricing, so a client can never
/// choose what it pays or reach lookups outside its own country.
/// </summary>
public sealed class ApplicationWriteService
{
    private readonly IApplicationDbContext _db;

    public ApplicationWriteService(IApplicationDbContext db) => _db = db;

    /// <summary>
    /// Confirms the whole Transaction → Sub-transaction → Authority chain is internally consistent
    /// and belongs to the order's verification country. Each link is checked rather than just the
    /// leaf, otherwise a valid authority id could be paired with an unrelated sub-type.
    /// </summary>
    public async Task ValidateCascadeAsync(
        Guid countryId,
        Guid transactionTypeId,
        Guid subTransactionTypeId,
        Guid verificationAuthorityId,
        CancellationToken cancellationToken)
    {
        var transactionTypeInCountry = await _db.TransactionTypes
            .AsNoTracking()
            .AnyAsync(
                t => t.Id == transactionTypeId
                     && t.IsActive
                     && t.CountryLinks.Any(link => link.CountryId == countryId),
                cancellationToken);

        if (!transactionTypeInCountry)
        {
            throw new ConflictException(
                "application.transaction_type_out_of_scope",
                "The transaction type does not belong to this order's verification country.");
        }

        var subTypeBelongsToParent = await _db.SubTransactionTypes
            .AsNoTracking()
            .AnyAsync(
                s => s.Id == subTransactionTypeId
                     && s.TransactionTypeId == transactionTypeId
                     && s.IsActive,
                cancellationToken);

        if (!subTypeBelongsToParent)
        {
            throw new ConflictException(
                "application.sub_transaction_type_mismatch",
                "The sub-transaction type does not belong to the selected transaction type.");
        }

        var authorityHandlesSubType = await _db.VerificationAuthorities
            .AsNoTracking()
            .AnyAsync(
                a => a.Id == verificationAuthorityId
                     && a.CountryId == countryId
                     && a.IsActive
                     && a.SubTransactionTypeLinks.Any(link =>
                         link.SubTransactionTypeId == subTransactionTypeId),
                cancellationToken);

        if (!authorityHandlesSubType)
        {
            throw new ConflictException(
                "application.authority_not_available",
                "The verification authority does not handle this sub-transaction type in this country.");
        }
    }

    /// <summary>
    /// Stores the applicant's own contact details, normalised. A blank value clears the field
    /// rather than being ignored — a draft may legitimately go back to having none.
    /// </summary>
    public static void ApplyApplicantContact(
        VerificationApplication application,
        string? email,
        string? phoneCountry,
        string? phoneCode,
        string? phoneNumber)
    {
        ArgumentNullException.ThrowIfNull(application);

        application.ApplicantEmail = Blank(email) ? null : email!.Trim();
        // Uppercased so the flag lookup never depends on how the code was sent.
        application.ApplicantPhoneCountry = Blank(phoneCountry)
            ? null
            : phoneCountry!.Trim().ToUpperInvariant();
        application.ApplicantPhoneCode = Blank(phoneCode) ? null : phoneCode!.Trim();
        // The trunk zero is dropped: it is only dialled from inside the country, and this number is
        // stored beside a calling code.
        application.ApplicantPhoneNumber = NationalPhoneNumber.Normalise(phoneNumber);

        static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);
    }

    /// <summary>Replaces the application's name rows, enforcing one Arabic and one English entry.</summary>
    public void ApplyNames(
        VerificationApplication application,
        IReadOnlyList<ApplicationNameInput> names)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(names);

        // A draft saved from step 1 has no names yet; that is not an error, just an empty step.
        if (names.Count == 0)
        {
            application.Names.Clear();
            return;
        }

        var arabic = names.SingleOrDefault(n => n.LanguageType == NameLanguageType.Arabic)
            ?? throw new ConflictException(
                "application.arabic_name_required",
                "An Arabic name is required.");

        var english = names.SingleOrDefault(n => n.LanguageType == NameLanguageType.English)
            ?? throw new ConflictException(
                "application.english_name_required",
                "An English name is required.");

        application.Names.Clear();

        foreach (var input in new[] { arabic, english })
        {
            application.Names.Add(new ApplicationName
            {
                ApplicationId = application.Id,
                LanguageType = input.LanguageType,
                FirstName = input.FirstName.Trim(),
                MiddleName = string.IsNullOrWhiteSpace(input.MiddleName) ? null : input.MiddleName.Trim(),
                LastName = input.LastName.Trim(),
            });
        }
    }

    /// <summary>
    /// Replaces the purchased service lines. Every price is recomputed from the stored service
    /// type — the client's numbers, if it sent any, are ignored entirely.
    /// </summary>
    public async Task ApplyServicesAsync(
        VerificationApplication application,
        IReadOnlyList<ApplicationServiceInput> services,
        Guid verificationAuthorityId,
        Guid subTransactionTypeId,
        Guid currencyId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(services);

        // Likewise, a draft saved before the services step has nothing to price.
        if (services.Count == 0)
        {
            DiscardEvidenceFor(application, application.Services);
            application.Services.Clear();
            application.RecalculateTotal();
            return;
        }

        var requestedIds = services.Select(s => s.ServiceTypeId).Distinct().ToList();

        var serviceTypes = await _db.ServiceTypes
            .AsNoTracking()
            .Include(s => s.Costs)
            .Include(s => s.OutputLanguages)
            .Where(s => requestedIds.Contains(s.Id) && s.IsActive)
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        if (serviceTypes.Count != requestedIds.Count)
        {
            throw new NotFoundException("One or more service types do not exist or are inactive.");
        }

        // A service type from another authority/sub-type would let a caller buy something the
        // chosen cascade does not actually provide.
        var foreign = serviceTypes.Values.FirstOrDefault(s =>
            s.VerificationAuthorityId != verificationAuthorityId
            || s.SubTransactionTypeId != subTransactionTypeId);

        if (foreign is not null)
        {
            throw new ConflictException(
                "application.service_type_not_offered",
                $"Service type '{foreign.NameEn}' is not offered for the selected authority and sub-type.");
        }

        // Lines already on the application, so a service that is still selected keeps the row it
        // already has. Grouped into queues because nothing forbids buying the same service twice:
        // each repeat then reuses a distinct existing row rather than all of them taking the first.
        var reusable = application.Services
            .GroupBy(line => line.ServiceTypeId)
            .ToDictionary(group => group.Key, group => new Queue<ApplicationService>(group));

        var added = new List<ApplicationService>();

        foreach (var input in services)
        {
            var serviceType = serviceTypes[input.ServiceTypeId];
            // A price switched off since the draft was started no longer sells: the draft has to
            // drop that service rather than buy it at a price the administrator withdrew.
            var price = serviceType.FindActiveCost(currencyId)
                ?? throw new ConflictException(
                    "service.currency_price_missing",
                    $"Service type '{serviceType.NameEn}' is not offered in the order's currency.");

            var languageCode = input.LanguageCode.Trim().ToLowerInvariant();

            // The wizard only offers the configured languages, but the check belongs here too:
            // that list is presentation, and a hand-crafted request must not get around it.
            if (!serviceType.OffersLanguage(languageCode))
            {
                throw new ConflictException(
                    "service.language_not_offered",
                    $"Service type '{serviceType.NameEn}' is not issued in '{languageCode}'.");
            }

            // Updated in place where the service survives the edit. Replacing the row instead
            // hands it a new id, and every document already uploaded against the old id is
            // detached by the database — the applicant's evidence silently disappears from the
            // checklist and they are blocked from submitting. Saving a draft must not cost
            // somebody their uploads.
            ApplicationService line;

            if (reusable.TryGetValue(serviceType.Id, out var spare) && spare.Count > 0)
            {
                line = spare.Dequeue();
            }
            else
            {
                line = new ApplicationService
                {
                    ApplicationId = application.Id,
                    ServiceTypeId = serviceType.Id,
                    LanguageCode = languageCode,
                };

                added.Add(line);
            }

            line.Quantity = input.Quantity;
            line.LanguageCode = languageCode;
            line.IsExpress = input.IsExpress;

            // Throws service.express_not_available when express was requested on a service that
            // does not offer it, and service.invalid_quantity for a non-positive quantity.
            line.PriceFrom(serviceType, price);
        }

        // Whatever is left in the queues is a service the applicant has taken off the application.
        var dropped = reusable.Values.SelectMany(spare => spare).ToList();

        DiscardEvidenceFor(application, dropped);

        foreach (var line in dropped)
        {
            application.Services.Remove(line);
        }

        foreach (var line in added)
        {
            application.Services.Add(line);
        }

        application.RecalculateTotal();
    }

    /// <summary>
    /// Deletes the documents uploaded against service lines that are being removed.
    /// </summary>
    /// <remarks>
    /// The link from a file to its service line is severed in memory rather than in the database
    /// (see ApplicationFileConfiguration), so a file left behind here keeps its row with no line
    /// to belong to: invisible to the checklist, counted by nothing, and impossible to reach from
    /// the application it was uploaded to. A document whose service is gone has nothing left to
    /// prove, so the row goes with it.
    /// </remarks>
    private void DiscardEvidenceFor(
        VerificationApplication application,
        IEnumerable<ApplicationService> lines)
    {
        var removedLineIds = lines.Select(line => line.Id).ToHashSet();

        if (removedLineIds.Count == 0)
        {
            return;
        }

        var orphaned = application.Files
            .Where(file => file.ApplicationServiceId is { } lineId && removedLineIds.Contains(lineId))
            .ToList();

        foreach (var file in orphaned)
        {
            application.Files.Remove(file);
            _db.ApplicationFiles.Remove(file);
        }
    }

    /// <summary>
    /// Loads an application that belongs to the caller's order. A different order's application is
    /// reported as not found rather than forbidden, so ids cannot be probed for existence.
    /// </summary>
    public async Task<VerificationApplication> RequireOwnedAsync(
        Guid applicationId,
        Guid orderId,
        CancellationToken cancellationToken,
        bool includeChildren = false)
    {
        var query = _db.Applications.AsQueryable();

        if (includeChildren)
        {
            query = query
                .Include(a => a.Names)
                .Include(a => a.Services)
                .Include(a => a.Files);
        }

        var application = await query
            .FirstOrDefaultAsync(a => a.Id == applicationId && a.OrderId == orderId, cancellationToken);

        return application ?? throw new NotFoundException("Application", applicationId);
    }

    /// <summary>
    /// Determines which required-file definitions the application's services still lack. Used to
    /// gate submission and to drive the upload step's checklist.
    /// </summary>
    public async Task<IReadOnlyList<RequiredFileStatusDto>> GetRequiredFileStatusAsync(
        VerificationApplication application,
        string languageCode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        var serviceTypeIds = application.Services.Select(s => s.ServiceTypeId).Distinct().ToList();

        var definitions = await _db.ServiceTypeRequiredFiles
            .AsNoTracking()
            .Include(f => f.Fields).ThenInclude(f => f.Options)
            .Include(f => f.AllowedFileTypes)
            .Include(f => f.Samples)
            .Where(f => f.ServiceTypeId != null
                        && serviceTypeIds.Contains(f.ServiceTypeId.Value)
                        && f.IsActive)
            .ToListAsync(cancellationToken);

        var enteredValues = await _db.ApplicationDocumentValues
            .AsNoTracking()
            .Where(v => v.ApplicationId == application.Id)
            .Select(v => new { v.ApplicationServiceId, v.RequiredFileFieldId, v.Value })
            .ToListAsync(cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var uploaded = await _db.ApplicationFiles
            .AsNoTracking()
            .Where(f => f.ApplicationId == application.Id
                        && f.Kind == ApplicationFileKind.UserUpload
                        && f.RequiredFileId != null)
            .Select(f => new { f.RequiredFileId, f.ApplicationServiceId })
            .ToListAsync(cancellationToken);

        var statuses = new List<RequiredFileStatusDto>();

        // Requirements are per service line, not per service type: buying the same service twice
        // means uploading its documents twice.
        foreach (var line in application.Services)
        {
            foreach (var definition in definitions.Where(d => d.ServiceTypeId == line.ServiceTypeId))
            {
                var uploadedCount = uploaded.Count(u =>
                    u.RequiredFileId == definition.Id && u.ApplicationServiceId == line.Id);

                var fields = definition.Fields
                    .Where(field => field.IsActive)
                    .OrderBy(field => field.SortOrder)
                    .ThenBy(field => field.NameEn)
                    .ToList();

                var values = enteredValues
                    .Where(v => v.ApplicationServiceId == line.Id
                                && fields.Any(f => f.Id == v.RequiredFileFieldId))
                    .ToDictionary(v => v.RequiredFileFieldId, v => v.Value);

                // A document counts as complete only when its own rules pass, so the submit gate
                // and the wizard agree on what "ready" means.
                var fieldsComplete = fields.All(field =>
                    field.Validate(values.GetValueOrDefault(field.Id), today) is null);

                statuses.Add(new RequiredFileStatusDto(
                    definition.Id,
                    line.Id,
                    definition.ResolveName(languageCode),
                    definition.IsMandatory,
                    uploadedCount > 0,
                    definition.ResolveMaxSizeBytes(ApplicationFile.MaxFileSizeBytes),
                    definition.MaxFiles,
                    uploadedCount,
                    fields.Select(f => RequiredFileFieldDto.From(f, languageCode)).ToList(),
                    values,
                    fieldsComplete,
                    DocumentFileTypes.ExtensionsForAll(definition.ResolveAllowedFileTypes()),
                    RequiredFileSampleDto.ListFor(definition, languageCode)));
            }
        }

        return statuses;
    }
}
