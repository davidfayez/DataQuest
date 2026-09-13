using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Common.Models;
using DataVerification.Domain.Common;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Lookups.Admin;

// --------------------------------------------------- Verification authorities

public sealed record ListAuthoritiesQuery : PagedQuery, IRequest<PagedResult<VerificationAuthorityDto>>
{
    public Guid? CountryId { get; init; }
}

/// <summary>
/// Creates or updates an authority together with the sub-transaction types it handles. The
/// mapping is replaced wholesale, which is what the admin screen's multi-select expresses.
/// </summary>
public sealed record UpsertAuthorityCommand(
    Guid? Id,
    Guid CountryId,
    string NameAr,
    string NameEn,
    bool IsActive,
    IReadOnlyList<Guid> SubTransactionTypeIds,
    string? DescriptionAr = null,
    string? DescriptionEn = null,
    string? Code = null) : IRequest<VerificationAuthorityDto>;

public sealed record DeleteAuthorityCommand(Guid Id) : IRequest<LookupDeleteOutcome>;

public sealed class UpsertAuthorityCommandValidator : AbstractValidator<UpsertAuthorityCommand>
{
    public UpsertAuthorityCommandValidator()
    {
        RuleFor(c => c.CountryId).NotEmpty();
        RuleFor(c => c.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(c => c.NameEn).NotEmpty().MaximumLength(200);
        RuleFor(c => c.SubTransactionTypeIds).NotNull();
        // Both languages, every save: an authority, a transaction type or a sub-type is chosen
        // by an applicant who reads only one of them, so neither may be left blank.
        RuleFor(c => c.DescriptionAr).NotEmpty()
            .WithMessage("Enter the Arabic description.").MaximumLength(2000);
        RuleFor(c => c.DescriptionEn).NotEmpty()
            .WithMessage("Enter the English description.").MaximumLength(2000);
        RuleFor(c => c.Code)
            .Must(code => !string.IsNullOrWhiteSpace(code)).WithMessage("Enter a code.")
            .Must(code => string.IsNullOrWhiteSpace(code) || LookupCode.IsValid(code))
            .WithMessage("A code is 2 to 30 letters or digits, with - or _ between.");
    }
}

public sealed class AuthorityAdminHandlers :
    IRequestHandler<ListAuthoritiesQuery, PagedResult<VerificationAuthorityDto>>,
    IRequestHandler<UpsertAuthorityCommand, VerificationAuthorityDto>,
    IRequestHandler<DeleteAuthorityCommand, LookupDeleteOutcome>
{
    private readonly IApplicationDbContext _db;
    private readonly AdminLookupService _lookups;

    public AuthorityAdminHandlers(IApplicationDbContext db, AdminLookupService lookups)
    {
        _db = db;
        _lookups = lookups;
    }

    public Task<PagedResult<VerificationAuthorityDto>> Handle(
        ListAuthoritiesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var source = _db.VerificationAuthorities.Include(a => a.SubTransactionTypeLinks).AsQueryable();

        if (request.CountryId is { } countryId)
        {
            source = source.Where(a => a.CountryId == countryId);
        }

        return _lookups.ListAsync(
            source,
            request,
            a => VerificationAuthorityDto.From(a, _lookups.Language),
            cancellationToken);
    }

    public async Task<VerificationAuthorityDto> Handle(
        UpsertAuthorityCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        _ = await _lookups.RequireAsync(_db.Countries, request.CountryId, cancellationToken);

        var requestedSubTypes = request.SubTransactionTypeIds.Distinct().ToList();

        // Mapping an authority to a sub-type from another country would make it reachable from a
        // cascade it does not belong to, so the whole selection is validated against the country.
        var validSubTypes = await _db.SubTransactionTypes
            .Where(s => requestedSubTypes.Contains(s.Id)
                        && s.TransactionType!.CountryLinks.Any(
                            link => link.CountryId == request.CountryId))
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        if (validSubTypes.Count != requestedSubTypes.Count)
        {
            throw new ConflictException(
                "authority.sub_type_country_mismatch",
                "Every sub-transaction type must belong to the same country as the authority.");
        }

        VerificationAuthority authority;
        if (request.Id is { } id)
        {
            authority = await _db.VerificationAuthorities
                .Include(a => a.SubTransactionTypeLinks)
                .FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(VerificationAuthority), id);
        }
        else
        {
            authority = new VerificationAuthority
            {
                CountryId = request.CountryId,
                NameAr = request.NameAr,
                NameEn = request.NameEn,
            };
            _db.VerificationAuthorities.Add(authority);
        }

        authority.CountryId = request.CountryId;
        var code = LookupCode.Normalize(request.Code);

        // Checked ahead of the unique index so the admin is told which code is taken rather than
        // shown a database error. Excluding this row lets an edit keep the code it already has.
        if (await _db.VerificationAuthorities.AnyAsync(
                other => other.Code == code && other.Id != authority.Id,
                cancellationToken))
        {
            throw new ConflictException(
                "authority.duplicate_code",
                $"Another verification authority already uses the code '{code}'.");
        }

        authority.Code = code;
        authority.NameAr = request.NameAr.Trim();
        authority.NameEn = request.NameEn.Trim();
        authority.DescriptionAr = request.DescriptionAr!.Trim();
        authority.DescriptionEn = request.DescriptionEn!.Trim();
        authority.IsActive = request.IsActive;

        var existingLinks = authority.SubTransactionTypeLinks.ToList();

        foreach (var link in existingLinks.Where(l => !requestedSubTypes.Contains(l.SubTransactionTypeId)))
        {
            _db.AuthoritySubTransactionTypes.Remove(link);
            authority.SubTransactionTypeLinks.Remove(link);
        }

        var linkedIds = authority.SubTransactionTypeLinks.Select(l => l.SubTransactionTypeId).ToHashSet();

        foreach (var subTypeId in requestedSubTypes.Where(subTypeId => !linkedIds.Contains(subTypeId)))
        {
            authority.SubTransactionTypeLinks.Add(new AuthoritySubTransactionType
            {
                VerificationAuthorityId = authority.Id,
                SubTransactionTypeId = subTypeId,
            });
        }

        await _lookups.SaveAsync(cancellationToken);
        await _lookups.AuditAsync(
            request.Id is null ? "VerificationAuthority.Created" : "VerificationAuthority.Updated",
            nameof(VerificationAuthority),
            authority.Id,
            new { authority.NameEn, authority.CountryId, SubTypes = requestedSubTypes },
            cancellationToken);

        return VerificationAuthorityDto.From(authority, _lookups.Language);
    }

    public Task<LookupDeleteOutcome> Handle(
        DeleteAuthorityCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _lookups.DeleteOrDeactivateAsync(
            _db.VerificationAuthorities,
            request.Id,
            async ct => await _db.Applications.AnyAsync(a => a.VerificationAuthorityId == request.Id, ct)
                        || await _db.ServiceTypes.AnyAsync(s => s.VerificationAuthorityId == request.Id, ct),
            "VerificationAuthority",
            cancellationToken);
    }
}

// ------------------------------------------------------------- Service types

public sealed record ListServiceTypesQuery : PagedQuery, IRequest<PagedResult<ServiceTypeDto>>
{
    public Guid? VerificationAuthorityId { get; init; }
}

/// <summary>
/// Creates or updates a service type, its per-currency costs and required-file definitions in one
/// call, since they are edited together on the same admin screen.
/// </summary>
public sealed record UpsertServiceTypeCommand(
    Guid? Id,
    Guid VerificationAuthorityId,
    Guid SubTransactionTypeId,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    int ExecutionTimeDays,
    bool EnableExpress,
    string? ExpressNoteAr,
    string? ExpressNoteEn,
    bool IsActive,
    bool ShowOnLanding,
    IReadOnlyList<ServiceTypeCostInput> Costs,
    IReadOnlyList<RequiredFileInput> RequiredFiles,
    IReadOnlyList<string> OutputLanguages,
    string? Code = null) : IRequest<ServiceTypeDto>;

public sealed record ServiceTypeCostInput(Guid CurrencyId, decimal Cost, decimal ExpressCost);

/// <param name="AllowedFileTypes">
/// Codes from <c>DocumentFileTypes</c> — the upload formats this document accepts. An empty set
/// means the platform default, so a document saved before this existed keeps behaving as it did.
/// </param>
public sealed record RequiredFileInput(
    Guid? Id,
    string NameAr,
    string NameEn,
    bool IsMandatory,
    long? MaxSizeBytes,
    int MaxFiles,
    IReadOnlyList<RequiredFileFieldInput> Fields,
    IReadOnlyList<string>? AllowedFileTypes = null);

/// <summary>A custom field an applicant fills in beside the document, with its validation rules.</summary>
public sealed record RequiredFileFieldInput(
    Guid? Id,
    string NameAr,
    string NameEn,
    RequiredFieldType FieldType,
    bool IsRequired,
    int SortOrder,
    int? MinLength,
    int? MaxLength,
    string? Pattern,
    decimal? MinValue,
    decimal? MaxValue,
    RequiredFieldDateRule DateRule,
    DateOnly? MinDate,
    DateOnly? MaxDate,
    IReadOnlyList<RequiredFileFieldOptionInput> Options);

public sealed record RequiredFileFieldOptionInput(string Value, string LabelAr, string LabelEn);

public sealed record DeleteServiceTypeCommand(Guid Id) : IRequest<LookupDeleteOutcome>;

public sealed class UpsertServiceTypeCommandValidator : AbstractValidator<UpsertServiceTypeCommand>
{
    public UpsertServiceTypeCommandValidator()
    {
        RuleFor(c => c.VerificationAuthorityId).NotEmpty();
        RuleFor(c => c.SubTransactionTypeId).NotEmpty()
            .WithMessage("Select a sub-transaction type.");
        RuleFor(c => c.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(c => c.NameEn).NotEmpty().MaximumLength(200);
        RuleFor(c => c.DescriptionAr).MaximumLength(2000);
        RuleFor(c => c.DescriptionEn).MaximumLength(2000);
        RuleFor(c => c.ExpressNoteAr).MaximumLength(500);
        RuleFor(c => c.ExpressNoteEn).MaximumLength(500);
        RuleFor(c => c.ExecutionTimeDays).GreaterThan(0);
        RuleFor(c => c.Code)
            .Must(code => !string.IsNullOrWhiteSpace(code)).WithMessage("Enter a code.")
            .Must(code => string.IsNullOrWhiteSpace(code) || LookupCode.IsValid(code))
            .WithMessage("A code is 2 to 30 letters or digits, with - or _ between.");
        RuleFor(c => c.Costs).NotEmpty()
            .WithMessage("Add at least one currency cost.");

        // A service with no documents asks the applicant to upload nothing, and then the review
        // queue receives an application with nothing to verify.
        RuleFor(c => c.RequiredFiles).NotEmpty()
            .WithMessage("Add at least one required document.");

        // The wizard offers exactly these, so an empty set would leave the applicant unable to
        // choose an output language at all.
        RuleFor(c => c.OutputLanguages).NotEmpty()
            .WithMessage("Select at least one output language.");

        RuleFor(c => c.OutputLanguages)
            .Must(codes => codes is null || codes.All(PlatformLanguages.IsSupported))
            .WithMessage($"Output languages must be drawn from: {string.Join(", ", PlatformLanguages.All)}.");

        RuleForEach(c => c.Costs).ChildRules(cost =>
        {
            cost.RuleFor(x => x.CurrencyId).NotEmpty();
            cost.RuleFor(x => x.Cost).GreaterThanOrEqualTo(0);
            cost.RuleFor(x => x.ExpressCost).GreaterThanOrEqualTo(0);
        });

        // An express price is only meaningful when express is actually offered.
        RuleFor(c => c)
            .Must(c => !c.EnableExpress || c.Costs.All(x => x.ExpressCost > 0))
            .WithMessage("Set an express cost greater than zero for every currency when express delivery is enabled.")
            .OverridePropertyName(nameof(UpsertServiceTypeCommand.Costs));

        RuleForEach(c => c.RequiredFiles).ChildRules(file =>
        {
            file.RuleFor(f => f.MaxFiles).InclusiveBetween(1, 20);

            // A document that accepts nothing could never be satisfied, so an empty set means the
            // default rather than "refuse everything" — but a set of codes we do not recognise is
            // a mistake worth reporting rather than quietly ignoring.
            file.RuleFor(f => f.AllowedFileTypes)
                .Must(codes => codes is null || codes.All(DocumentFileTypes.IsSupported))
                .WithMessage(
                    $"Document formats must be drawn from: {string.Join(", ", DocumentFileTypes.All)}.");
            file.RuleFor(f => f.MaxSizeBytes)
                .GreaterThan(0).When(f => f.MaxSizeBytes.HasValue)
                .WithMessage("A document maximum size must be greater than zero.");

            file.RuleForEach(f => f.Fields).ChildRules(field =>
            {
                field.RuleFor(x => x.NameAr).NotEmpty().MaximumLength(200);
                field.RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
                field.RuleFor(x => x.Pattern).MaximumLength(400);

                field.RuleFor(x => x.MaxLength)
                    .GreaterThanOrEqualTo(x => x.MinLength ?? 0)
                    .When(x => x.MaxLength.HasValue)
                    .WithMessage("A maximum length cannot be below the minimum length.");

                field.RuleFor(x => x.MaxValue)
                    .GreaterThanOrEqualTo(x => x.MinValue!.Value)
                    .When(x => x.MinValue.HasValue && x.MaxValue.HasValue)
                    .WithMessage("A maximum value cannot be below the minimum value.");

                field.RuleFor(x => x.MaxDate)
                    .GreaterThanOrEqualTo(x => x.MinDate!.Value)
                    .When(x => x.MinDate.HasValue && x.MaxDate.HasValue)
                    .WithMessage("A latest date cannot be before the earliest date.");

                // A dropdown with nothing to pick leaves a required field unanswerable.
                field.RuleFor(x => x.Options)
                    .NotEmpty()
                    .When(x => x.FieldType == RequiredFieldType.Dropdown)
                    .WithMessage("Add at least one option to a dropdown field.");

                field.RuleForEach(x => x.Options).ChildRules(option =>
                {
                    option.RuleFor(o => o.Value).NotEmpty().MaximumLength(200);
                    option.RuleFor(o => o.LabelAr).NotEmpty().MaximumLength(200);
                    option.RuleFor(o => o.LabelEn).NotEmpty().MaximumLength(200);
                });
            });

            file.RuleFor(f => f.NameAr).NotEmpty().MaximumLength(200);
            file.RuleFor(f => f.NameEn).NotEmpty().MaximumLength(200);
        });
    }
}

public sealed class ServiceTypeAdminHandlers :
    IRequestHandler<ListServiceTypesQuery, PagedResult<ServiceTypeDto>>,
    IRequestHandler<UpsertServiceTypeCommand, ServiceTypeDto>,
    IRequestHandler<DeleteServiceTypeCommand, LookupDeleteOutcome>
{
    private readonly IApplicationDbContext _db;
    private readonly AdminLookupService _lookups;

    public ServiceTypeAdminHandlers(IApplicationDbContext db, AdminLookupService lookups)
    {
        _db = db;
        _lookups = lookups;
    }

    public Task<PagedResult<ServiceTypeDto>> Handle(
        ListServiceTypesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Split, not joined. Documents, their fields and options, allowed formats, languages and
        // prices in one result set made SQL Server ask for ~113 MB of sort memory to return three
        // rows; on a busy server that grant is queued, and the list took 25 seconds every time.
        var source = _db.ServiceTypes
            .AsSplitQuery()
            .Include(s => s.RequiredFiles).ThenInclude(f => f.Fields).ThenInclude(f => f.Options)
            .Include(s => s.RequiredFiles).ThenInclude(f => f.AllowedFileTypes)
            .Include(s => s.OutputLanguages)
            .Include(s => s.Costs)
            .ThenInclude(c => c.Currency)
            .AsQueryable();

        if (request.VerificationAuthorityId is { } authorityId)
        {
            source = source.Where(s => s.VerificationAuthorityId == authorityId);
        }

        return _lookups.ListAsync(
            source,
            request,
            s => ServiceTypeDto.From(s, _lookups.Language),
            cancellationToken);
    }

    public async Task<ServiceTypeDto> Handle(
        UpsertServiceTypeCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        _ = await _lookups.RequireAsync(
            _db.VerificationAuthorities,
            request.VerificationAuthorityId,
            cancellationToken);

        _ = await _lookups.RequireAsync(
            _db.SubTransactionTypes,
            request.SubTransactionTypeId,
            cancellationToken);

        var authorityHandlesSubType = await _db.AuthoritySubTransactionTypes.AnyAsync(
            link => link.VerificationAuthorityId == request.VerificationAuthorityId
                    && link.SubTransactionTypeId == request.SubTransactionTypeId,
            cancellationToken);
        if (!authorityHandlesSubType)
        {
            throw new ConflictException(
                "service_type.sub_type_not_handled",
                "The selected authority does not handle that sub-transaction type.");
        }

        var requestedCosts = request.Costs
            .GroupBy(c => c.CurrencyId)
            .Select(group => group.Last())
            .ToList();
        var currencyIds = requestedCosts.Select(c => c.CurrencyId).ToList();
        var knownCurrencyCount = await _db.Currencies
            .CountAsync(c => currencyIds.Contains(c.Id), cancellationToken);
        if (knownCurrencyCount != currencyIds.Count)
        {
            throw new NotFoundException("One or more of the currencies supplied do not exist.");
        }

        await EnsureEveryCurrencyIsPricedAsync(request, currencyIds, cancellationToken);

        ServiceType serviceType;
        if (request.Id is { } id)
        {
            serviceType = await _db.ServiceTypes
                .Include(s => s.RequiredFiles).ThenInclude(f => f.Fields).ThenInclude(f => f.Options)
            .Include(s => s.RequiredFiles).ThenInclude(f => f.AllowedFileTypes)
                .Include(s => s.OutputLanguages)
                .Include(s => s.Costs)
                .ThenInclude(c => c.Currency)
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(ServiceType), id);
        }
        else
        {
            serviceType = new ServiceType
            {
                VerificationAuthorityId = request.VerificationAuthorityId,
                SubTransactionTypeId = request.SubTransactionTypeId,
                NameAr = request.NameAr,
                NameEn = request.NameEn,
            };
            _db.ServiceTypes.Add(serviceType);
        }

        serviceType.VerificationAuthorityId = request.VerificationAuthorityId;
        serviceType.SubTransactionTypeId = request.SubTransactionTypeId;
        var code = LookupCode.Normalize(request.Code);

        // Checked ahead of the unique index so the admin is told which code is taken rather than
        // shown a database error. Excluding this row lets an edit keep the code it already has.
        if (await _db.ServiceTypes.AnyAsync(
                other => other.Code == code && other.Id != serviceType.Id,
                cancellationToken))
        {
            throw new ConflictException(
                "service_type.duplicate_code",
                $"Another service type already uses the code '{code}'.");
        }

        serviceType.Code = code;
        serviceType.NameAr = request.NameAr.Trim();
        serviceType.NameEn = request.NameEn.Trim();
        serviceType.DescriptionAr = request.DescriptionAr?.Trim();
        serviceType.DescriptionEn = request.DescriptionEn?.Trim();
        serviceType.ExecutionTimeDays = request.ExecutionTimeDays;
        serviceType.EnableExpress = request.EnableExpress;
        // Blank is stored as null, so "no note configured" is one state rather than two.
        serviceType.ExpressNoteAr = NullIfBlank(request.ExpressNoteAr);
        serviceType.ExpressNoteEn = NullIfBlank(request.ExpressNoteEn);
        serviceType.IsActive = request.IsActive;
        serviceType.ShowOnLanding = request.ShowOnLanding;

        SyncCosts(serviceType, requestedCosts, request.EnableExpress);

        SyncOutputLanguages(serviceType, PlatformLanguages.Normalize(request.OutputLanguages));

        await SyncRequiredFilesAsync(serviceType, request.RequiredFiles, cancellationToken);

        await _lookups.SaveAsync(cancellationToken);
        await _lookups.AuditAsync(
            request.Id is null ? "ServiceType.Created" : "ServiceType.Updated",
            nameof(ServiceType),
            serviceType.Id,
            new
            {
                serviceType.NameEn,
                serviceType.SubTransactionTypeId,
                serviceType.Cost,
                serviceType.EnableExpress,
                Costs = requestedCosts.Select(c => new { c.CurrencyId, c.Cost, c.ExpressCost }),
                OutputLanguages = serviceType.OutputLanguages.Select(l => l.LanguageCode).Order(),
            },
            cancellationToken);

        return ServiceTypeDto.From(serviceType, _lookups.Language);
    }

    public Task<LookupDeleteOutcome> Handle(
        DeleteServiceTypeCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _lookups.DeleteOrDeactivateAsync(
            _db.ServiceTypes,
            request.Id,
            ct => _db.ApplicationServices.AnyAsync(s => s.ServiceTypeId == request.Id, ct),
            "ServiceType",
            cancellationToken);
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Replaces the configured output languages with exactly the requested set.</summary>
    private void SyncOutputLanguages(ServiceType serviceType, IReadOnlyList<string> requested)
    {
        var wanted = requested.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var existing in serviceType.OutputLanguages
                     .Where(l => !wanted.Contains(l.LanguageCode))
                     .ToList())
        {
            _db.ServiceTypeLanguages.Remove(existing);
            serviceType.OutputLanguages.Remove(existing);
        }

        var already = serviceType.OutputLanguages
            .Select(l => l.LanguageCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var code in requested.Where(code => !already.Contains(code)))
        {
            serviceType.OutputLanguages.Add(new ServiceTypeLanguage
            {
                ServiceTypeId = serviceType.Id,
                LanguageCode = code,
            });
        }
    }

    /// <summary>
    /// Refuses a service that is not priced in every currency it could be sold in.
    /// </summary>
    /// <remarks>
    /// This is not tidiness. The applicant's service list is filtered by
    /// <c>Costs.Any(c => c.CurrencyId == orderCurrency)</c>, so a service missing one currency does
    /// not merely lack a price for those applicants — it does not appear for them at all, with
    /// nothing anywhere to say why. The gap is invisible from the admin panel and invisible in the
    /// wizard, which is exactly the kind of thing that stays broken for months.
    ///
    /// "Every currency it could be sold in" is the currency list of the countries its transaction
    /// type runs in, which is the same set the admin form offers. Currencies outside that set can
    /// never be an order's currency here, so demanding a price in them would be busywork.
    /// </remarks>
    private async Task EnsureEveryCurrencyIsPricedAsync(
        UpsertServiceTypeCommand request,
        IReadOnlyList<Guid> pricedCurrencyIds,
        CancellationToken cancellationToken)
    {
        var required = await _db.SubTransactionTypes
            .AsNoTracking()
            .Where(s => s.Id == request.SubTransactionTypeId)
            .SelectMany(s => _db.TransactionTypeCountries
                .Where(link => link.TransactionTypeId == s.TransactionTypeId)
                .SelectMany(link => _db.CountryCurrencies
                    .Where(cc => cc.CountryId == link.CountryId && cc.Currency!.IsActive)
                    .Select(cc => cc.Currency!)))
            .Distinct()
            .Select(c => new { c.Id, c.Code })
            .ToListAsync(cancellationToken);

        // Nothing in scope means the countries or their currencies are not configured yet. That is
        // a gap in the country setup rather than in this service, and the existing "at least one
        // cost" rule still applies, so there is nothing to demand here.
        if (required.Count == 0)
        {
            return;
        }

        var priced = pricedCurrencyIds.ToHashSet();
        var missing = required.Where(c => !priced.Contains(c.Id)).Select(c => c.Code).Order().ToList();

        if (missing.Count > 0)
        {
            throw new ConflictException(
                "service_type.missing_currency_costs",
                "Every currency this service can be sold in needs a price. Missing: "
                + string.Join(", ", missing) + ".");
        }
    }

    private void SyncCosts(
        ServiceType serviceType,
        IReadOnlyList<ServiceTypeCostInput> requested,
        bool enableExpress)
    {
        var requestedIds = requested.Select(c => c.CurrencyId).ToHashSet();

        foreach (var existing in serviceType.Costs.Where(c => !requestedIds.Contains(c.CurrencyId)).ToList())
        {
            _db.ServiceTypeCosts.Remove(existing);
            serviceType.Costs.Remove(existing);
        }

        foreach (var input in requested)
        {
            var target = serviceType.Costs.FirstOrDefault(c => c.CurrencyId == input.CurrencyId);
            if (target is null)
            {
                target = new ServiceTypeCost
                {
                    ServiceTypeId = serviceType.Id,
                    CurrencyId = input.CurrencyId,
                };
                serviceType.Costs.Add(target);
            }

            target.Cost = input.Cost;
            target.ExpressCost = enableExpress ? input.ExpressCost : 0m;
        }

        // Keep the scalar columns as a convenient default for landing/admin list display.
        var primary = serviceType.Costs.OrderBy(c => c.Cost).First();
        serviceType.Cost = primary.Cost;
        serviceType.ExpressCost = enableExpress ? primary.ExpressCost : 0m;
    }

    /// <summary>
    /// Reconciles the required-file list. A definition already referenced by an uploaded file is
    /// kept even if the admin dropped it, so historical uploads keep their meaning.
    /// </summary>
    private async Task SyncRequiredFilesAsync(
        ServiceType serviceType,
        IReadOnlyList<RequiredFileInput> requested,
        CancellationToken cancellationToken)
    {
        var keptIds = requested.Where(f => f.Id.HasValue).Select(f => f.Id!.Value).ToHashSet();

        foreach (var existing in serviceType.RequiredFiles.ToList())
        {
            if (keptIds.Contains(existing.Id))
            {
                continue;
            }

            var referenced = await _db.ApplicationFiles
                .AnyAsync(f => f.RequiredFileId == existing.Id, cancellationToken);

            if (referenced)
            {
                existing.IsMandatory = false;
                existing.IsActive = false;
                continue;
            }

            _db.ServiceTypeRequiredFiles.Remove(existing);
            serviceType.RequiredFiles.Remove(existing);
        }

        foreach (var input in requested)
        {
            var target = input.Id.HasValue
                ? serviceType.RequiredFiles.FirstOrDefault(f => f.Id == input.Id.Value)
                : null;

            if (target is null)
            {
                target = new ServiceTypeRequiredFile
                {
                    ServiceTypeId = serviceType.Id,
                    NameAr = input.NameAr,
                    NameEn = input.NameEn,
                };
                serviceType.RequiredFiles.Add(target);
            }

            target.NameAr = input.NameAr.Trim();
            target.NameEn = input.NameEn.Trim();
            target.IsMandatory = input.IsMandatory;
            target.IsActive = true;
            target.MaxSizeBytes = input.MaxSizeBytes;
            target.MaxFiles = input.MaxFiles;

            SyncAllowedFileTypes(target, DocumentFileTypes.Normalize(input.AllowedFileTypes));
            SyncFields(target, input.Fields);
        }
    }

    /// <summary>Replaces the formats a document accepts with exactly the requested set.</summary>
    private void SyncAllowedFileTypes(ServiceTypeRequiredFile document, IReadOnlyList<string> requested)
    {
        foreach (var existing in document.AllowedFileTypes
                     .Where(t => !requested.Contains(t.FileTypeCode, StringComparer.OrdinalIgnoreCase))
                     .ToList())
        {
            _db.RequiredFileAllowedTypes.Remove(existing);
            document.AllowedFileTypes.Remove(existing);
        }

        var present = document.AllowedFileTypes
            .Select(t => t.FileTypeCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var code in requested.Where(code => !present.Contains(code)))
        {
            document.AllowedFileTypes.Add(new RequiredFileAllowedType
            {
                RequiredFileId = document.Id,
                FileTypeCode = code,
            });
        }
    }

    /// <summary>Replaces a document custom fields with exactly the requested set.</summary>
    private void SyncFields(ServiceTypeRequiredFile document, IReadOnlyList<RequiredFileFieldInput> requested)
    {
        var keptIds = requested.Where(f => f.Id.HasValue).Select(f => f.Id!.Value).ToHashSet();

        foreach (var existing in document.Fields.Where(f => !keptIds.Contains(f.Id)).ToList())
        {
            _db.RequiredFileFields.Remove(existing);
            document.Fields.Remove(existing);
        }

        foreach (var input in requested)
        {
            var target = input.Id.HasValue
                ? document.Fields.FirstOrDefault(f => f.Id == input.Id.Value)
                : null;

            if (target is null)
            {
                target = new RequiredFileField
                {
                    RequiredFileId = document.Id,
                    NameAr = input.NameAr,
                    NameEn = input.NameEn,
                };
                document.Fields.Add(target);
            }

            target.NameAr = input.NameAr.Trim();
            target.NameEn = input.NameEn.Trim();
            target.FieldType = input.FieldType;
            target.IsRequired = input.IsRequired;
            target.SortOrder = input.SortOrder;
            target.IsActive = true;

            // Only the rules belonging to the chosen type are kept, so switching a type cannot
            // leave a stale bound quietly rejecting valid input.
            var isText = input.FieldType == RequiredFieldType.Text;
            var isNumber = input.FieldType == RequiredFieldType.Number;
            var isDate = input.FieldType == RequiredFieldType.Date;

            target.MinLength = isText ? input.MinLength : null;
            target.MaxLength = isText ? input.MaxLength : null;
            target.Pattern = isText && !string.IsNullOrWhiteSpace(input.Pattern) ? input.Pattern.Trim() : null;
            target.MinValue = isNumber ? input.MinValue : null;
            target.MaxValue = isNumber ? input.MaxValue : null;
            target.DateRule = isDate ? input.DateRule : RequiredFieldDateRule.Any;
            target.MinDate = isDate ? input.MinDate : null;
            target.MaxDate = isDate ? input.MaxDate : null;

            SyncOptions(target, input.FieldType == RequiredFieldType.Dropdown ? input.Options : []);
        }
    }

    private void SyncOptions(RequiredFileField field, IReadOnlyList<RequiredFileFieldOptionInput> requested)
    {
        foreach (var existing in field.Options.ToList())
        {
            _db.RequiredFileFieldOptions.Remove(existing);
            field.Options.Remove(existing);
        }

        var order = 0;
        foreach (var option in requested)
        {
            field.Options.Add(new RequiredFileFieldOption
            {
                RequiredFileFieldId = field.Id,
                Value = option.Value.Trim(),
                LabelAr = option.LabelAr.Trim(),
                LabelEn = option.LabelEn.Trim(),
                SortOrder = order++,
            });
        }
    }
}
