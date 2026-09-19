using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Common.Models;
using DataVerification.Application.Features.Lookups.Admin;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using DataVerification.Domain.Payments;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Payments.Admin;

public sealed record ListPaymentMethodsQuery : PagedQuery, IRequest<PagedResult<AdminPaymentMethodDto>>
{
    /// <summary>Only methods offered in this country.</summary>
    public Guid? CountryId { get; init; }

    /// <summary>Only methods of this payment type.</summary>
    public Guid? PaymentMethodTypeId { get; init; }

    /// <summary>Only methods whose type is of this kind — link versus transfer.</summary>
    public PaymentMethodKind? Kind { get; init; }
}

public sealed record GetPaymentMethodQuery(Guid Id) : IRequest<AdminPaymentMethodDto>;

/// <summary>One of the method's stored gateway secrets, in full, for the eye button.</summary>
public sealed record GetPaymentMethodSecretQuery(Guid Id, string Key) : IRequest<StoredGatewaySecretDto>;

public sealed record StoredGatewaySecretDto(string Key, string? Value);

/// <summary>
/// One receiving account as submitted by the editor. An <see cref="Id"/> that matches an existing
/// row edits it in place, which is what keeps an already-uploaded barcode attached when the rest
/// of the method is saved.
/// </summary>
public sealed record PaymentMethodAccountInput(
    Guid? Id,
    string LabelAr,
    string LabelEn,
    string AccountNumber,
    string? AccountHolder,
    /// <summary>Required by a bank-transfer type; ignored by every other kind.</summary>
    Guid? BankId,
    bool IsActive,
    int SortOrder);

/// <summary>One notification mailbox as submitted by the editor.</summary>
public sealed record PaymentNotificationEmailInput(
    Guid? Id,
    string Email,
    string? DisplayName,
    bool NotifyOnSubmitted,
    bool NotifyOnApproved,
    bool NotifyOnRejected);

public sealed record UpsertPaymentMethodCommand(
    Guid? Id,
    Guid PaymentMethodTypeId,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    string? PublicNoteAr,
    string? PublicNoteEn,
    string? PrivateNoteAr,
    string? PrivateNoteEn,
    string? ExternalUrl,
    int SortOrder,
    bool IsActive,
    IReadOnlyList<Guid> CountryIds,
    IReadOnlyList<Guid> CurrencyIds,
    IReadOnlyList<PaymentMethodAccountInput> Accounts,
    IReadOnlyList<PaymentNotificationEmailInput> NotificationEmails,
    /// <summary>Null leaves any stored integration untouched; see PaymentIntegrationInput.</summary>
    PaymentIntegrationInput? Integration = null,
    /// <summary>
    /// The documents an applicant uploads with every deposit, edited in place by id. Null leaves the
    /// stored ones untouched, so a client that predates them cannot clear them by omission.
    /// </summary>
    IReadOnlyList<RequiredFileInput>? RequiredFiles = null,
    /// <summary>
    /// The gateway integration this method pays through, from the Payment type integrations page.
    /// Null means none, and clears whatever was configured for it.
    /// </summary>
    Guid? GatewayIntegrationId = null,
    /// <summary>
    /// The chosen gateway's non-secret settings, replaced outright. Ignored without an integration.
    /// </summary>
    IReadOnlyDictionary<string, string?>? GatewaySettings = null,
    /// <summary>
    /// The chosen gateway's secrets. A key left out keeps what is stored, an empty value removes it,
    /// anything else replaces it.
    /// </summary>
    IReadOnlyDictionary<string, string?>? GatewaySecrets = null)
    : IRequest<AdminPaymentMethodDto>;

public sealed record DeletePaymentMethodCommand(Guid Id) : IRequest<LookupDeleteOutcome>;

public sealed class UpsertPaymentMethodCommandValidator : AbstractValidator<UpsertPaymentMethodCommand>
{
    public UpsertPaymentMethodCommandValidator()
    {
        RuleFor(c => c.PaymentMethodTypeId).NotEmpty().WithMessage("Choose a payment type.");
        RuleFor(c => c.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(c => c.NameEn).NotEmpty().MaximumLength(200);
        RuleFor(c => c.DescriptionAr).MaximumLength(2000);
        RuleFor(c => c.DescriptionEn).MaximumLength(2000);
        RuleFor(c => c.PublicNoteAr).MaximumLength(2000);
        RuleFor(c => c.PublicNoteEn).MaximumLength(2000);
        RuleFor(c => c.PrivateNoteAr).MaximumLength(2000);
        RuleFor(c => c.PrivateNoteEn).MaximumLength(2000);
        RuleFor(c => c.SortOrder).GreaterThanOrEqualTo(0);

        RuleFor(c => c.CountryIds).NotEmpty().WithMessage("Select at least one country.");
        RuleFor(c => c.CurrencyIds).NotEmpty().WithMessage("Select at least one currency.");

        // The same rules a service type's documents follow.
        When(c => c.RequiredFiles is not null, () =>
        {
            RuleForEach(c => c.RequiredFiles).SetValidator(new RequiredFileInputValidator());
            RuleFor(c => c.RequiredFiles!)
                .Must(list => list.Count <= 20)
                .WithMessage("A method may ask for at most 20 documents.");
        });

        RuleForEach(c => c.Accounts).ChildRules(account =>
        {
            account.RuleFor(a => a.AccountNumber)
                .NotEmpty().WithMessage("An account needs a number.")
                .MaximumLength(120);
            account.RuleFor(a => a.LabelAr).MaximumLength(200);
            account.RuleFor(a => a.LabelEn).MaximumLength(200);
            account.RuleFor(a => a.AccountHolder).MaximumLength(200);
            account.RuleFor(a => a.SortOrder).GreaterThanOrEqualTo(0);
        });

        RuleForEach(c => c.NotificationEmails).ChildRules(recipient =>
        {
            recipient.RuleFor(r => r.Email)
                .NotEmpty().WithMessage("A notification recipient needs an email address.")
                .MaximumLength(256)
                .EmailAddress().WithMessage("That is not a valid email address.");
            recipient.RuleFor(r => r.DisplayName).MaximumLength(200);
        });

        // A generous cap, but a cap: a runaway list would fan every deposit out to hundreds of
        // mailboxes and look like the platform is spamming.
        RuleFor(c => c.NotificationEmails).Must(list => list.Count <= 25)
            .WithMessage("A method may notify at most 25 addresses.");

        // The same mailbox twice would send every notification twice.
        RuleFor(c => c.NotificationEmails)
            .Must(list => list
                .Select(r => r.Email?.Trim())
                .Where(email => !string.IsNullOrEmpty(email))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == list.Count(r => !string.IsNullOrWhiteSpace(r.Email)))
            .WithMessage("The same notification address is listed more than once.");

        // The same number listed twice would make "which account did you pay" unanswerable.
        RuleFor(c => c.Accounts)
            .Must(accounts => accounts
                .Select(a => a.AccountNumber?.Trim())
                .Where(number => !string.IsNullOrEmpty(number))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == accounts.Count(a => !string.IsNullOrWhiteSpace(a.AccountNumber)))
            .WithMessage("The same account number is listed more than once.");

        When(c => c.Integration is not null, () =>
        {
            RuleFor(c => c.Integration!.Provider).MaximumLength(120);
            RuleFor(c => c.Integration!.MerchantId).MaximumLength(200);
            RuleFor(c => c.Integration!.IntegrationId).MaximumLength(200);
            RuleFor(c => c.Integration!.Mode).IsInEnum();

            RuleFor(c => c.Integration!.ApiKey).MaximumLength(500);
            RuleFor(c => c.Integration!.Password).MaximumLength(500);
            RuleFor(c => c.Integration!.WebhookSecret).MaximumLength(500);

            RuleFor(c => c.Integration!.BaseUrl)
                .Must(BeAnAbsoluteUrl).WithMessage("The API base URL must be a full http(s) address.");
            RuleFor(c => c.Integration!.RedirectUrl)
                .Must(BeAnAbsoluteUrl).WithMessage("The redirect URL must be a full http(s) address.");
            RuleFor(c => c.Integration!.CancelUrl)
                .Must(BeAnAbsoluteUrl).WithMessage("The cancel URL must be a full http(s) address.");
            RuleFor(c => c.Integration!.CallbackUrl)
                .Must(BeAnAbsoluteUrl).WithMessage("The callback URL must be a full http(s) address.");

            RuleFor(c => c.Integration!.SessionTimeoutMinutes)
                .InclusiveBetween(1, 1440)
                .When(c => c.Integration!.SessionTimeoutMinutes is not null)
                .WithMessage("A payment session lasts between 1 minute and 24 hours.");

            // Live means real money over these URLs. Plain http would carry the applicant's return
            // trip, and the provider's result callback, in the clear.
            RuleFor(c => c.Integration!)
                .Must(integration => integration.Mode != PaymentIntegrationMode.Live
                    || new[]
                    {
                        integration.BaseUrl,
                        integration.RedirectUrl,
                        integration.CancelUrl,
                        integration.CallbackUrl,
                    }.All(IsHttpsOrEmpty))
                .WithMessage("A live integration must use https for every URL.");
        });
    }

    /// <summary>Empty is allowed — these are optional; anything present has to be a real URL.</summary>
    private static bool BeAnAbsoluteUrl(string? value) =>
        string.IsNullOrWhiteSpace(value)
        || (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp));

    private static bool IsHttpsOrEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value)
        || (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps);
}

public sealed class PaymentMethodHandlers :
    IRequestHandler<ListPaymentMethodsQuery, PagedResult<AdminPaymentMethodDto>>,
    IRequestHandler<GetPaymentMethodQuery, AdminPaymentMethodDto>,
    IRequestHandler<GetPaymentMethodSecretQuery, StoredGatewaySecretDto>,
    IRequestHandler<UpsertPaymentMethodCommand, AdminPaymentMethodDto>,
    IRequestHandler<DeletePaymentMethodCommand, LookupDeleteOutcome>
{
    private readonly IApplicationDbContext _db;
    private readonly AdminLookupService _lookups;
    private readonly IFileStorage _storage;
    private readonly ISecretProtector _protector;

    public PaymentMethodHandlers(
        IApplicationDbContext db,
        AdminLookupService lookups,
        IFileStorage storage,
        ISecretProtector protector)
    {
        _db = db;
        _lookups = lookups;
        _storage = storage;
        _protector = protector;
    }

    public async Task<PagedResult<AdminPaymentMethodDto>> Handle(
        ListPaymentMethodsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = WithGraph(_db.PaymentMethods.AsNoTracking());

        if (request.IsActive is { } isActive)
        {
            query = query.Where(method => method.IsActive == isActive);
        }

        if (request.CountryId is { } countryId)
        {
            query = query.Where(method => method.CountryLinks.Any(link => link.CountryId == countryId));
        }

        if (request.PaymentMethodTypeId is { } typeId)
        {
            query = query.Where(method => method.PaymentMethodTypeId == typeId);
        }

        if (request.Kind is { } kind)
        {
            query = query.Where(method => method.Type!.Kind == kind);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(method => EF.Functions.Like(method.NameEn, $"%{term}%")
                                          || EF.Functions.Like(method.NameAr, $"%{term}%"));
        }

        // Sort order is the admin's own arrangement of the applicant's picker, so the list they
        // arrange it on is ordered the same way.
        // The id last: the related rows load in split queries, and each one has to page over the
        // same rows in the same order.
        query = query
            .OrderBy(method => method.SortOrder)
            .ThenBy(method => method.NameEn)
            .ThenBy(method => method.Id);

        return await query.ToPagedResultAsync(
            request,
            method => AdminPaymentMethodDto.From(method, _lookups.Language, _protector.IsEnabled),
            cancellationToken);
    }

    public async Task<AdminPaymentMethodDto> Handle(
        GetPaymentMethodQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var method = await WithGraph(_db.PaymentMethods.AsNoTracking())
            .FirstOrDefaultAsync(m => m.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(PaymentMethod), request.Id);

        return AdminPaymentMethodDto.From(method, _lookups.Language, _protector.IsEnabled);
    }

    public async Task<StoredGatewaySecretDto> Handle(
        GetPaymentMethodSecretQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var method = await _db.PaymentMethods
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(PaymentMethod), request.Id);

        var secrets = GatewaySettingsJson.Read(method.GatewaySecretsJson);
        var value = secrets.TryGetValue(request.Key, out var cipher) ? _protector.Unprotect(cipher) : null;

        // Every look is recorded — which key, never what it was.
        await _lookups.AuditAsync(
            "PaymentMethod.GatewaySecretViewed",
            nameof(PaymentMethod),
            method.Id,
            new { request.Key, Found = value is not null },
            cancellationToken);

        return new StoredGatewaySecretDto(request.Key, value);
    }

    public async Task<AdminPaymentMethodDto> Handle(
        UpsertPaymentMethodCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var type = await _db.PaymentMethodTypes
            .FirstOrDefaultAsync(t => t.Id == request.PaymentMethodTypeId, cancellationToken)
            ?? throw new NotFoundException(nameof(PaymentMethodType), request.PaymentMethodTypeId);

        var countryIds = request.CountryIds.Distinct().ToList();
        var currencyIds = request.CurrencyIds.Distinct().ToList();

        await EnsureCountriesExistAsync(countryIds, cancellationToken);
        await EnsureCurrenciesBelongToCountriesAsync(countryIds, currencyIds, cancellationToken);
        await EnsureBanksBelongToCountriesAsync(type, countryIds, request.Accounts, cancellationToken);
        EnsureTypeRulesSatisfied(type, request);

        var method = request.Id is { } id && id != Guid.Empty
            ? await WithGraph(_db.PaymentMethods).FirstOrDefaultAsync(m => m.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(PaymentMethod), id)
            : NewMethod(request);

        await _lookups.EnsureUniqueAsync(
            _db.PaymentMethods,
            other => other.Id != method.Id
                && (other.NameEn == request.NameEn || other.NameAr == request.NameAr),
            "payment_method.duplicate_name",
            "A payment method with this name already exists.",
            cancellationToken);

        method.PaymentMethodTypeId = type.Id;
        method.NameAr = request.NameAr;
        method.NameEn = request.NameEn;
        method.DescriptionAr = Trim(request.DescriptionAr);
        method.DescriptionEn = Trim(request.DescriptionEn);
        method.PublicNoteAr = Trim(request.PublicNoteAr);
        method.PublicNoteEn = Trim(request.PublicNoteEn);
        method.PrivateNoteAr = Trim(request.PrivateNoteAr);
        method.PrivateNoteEn = Trim(request.PrivateNoteEn);
        // A link on a type that does not ask for one would never be shown, and keeping it would
        // resurface the moment someone switched the type back.
        method.ExternalUrl = type.RequiresExternalUrl ? Trim(request.ExternalUrl) : null;
        method.SortOrder = request.SortOrder;
        method.IsActive = request.IsActive;
        method.UpdatedAtUtc = DateTime.UtcNow;

        SyncCountries(method, countryIds);
        SyncCurrencies(method, currencyIds);
        var orphanedBarcodes = SyncAccounts(method, type, request.Accounts);
        SyncNotificationEmails(method, request.NotificationEmails);
        SyncIntegration(method, type, request.Integration);
        await SyncGatewayIntegrationAsync(method, request, cancellationToken);

        // Null leaves the stored documents alone; see the command.
        IReadOnlyList<string> discardedReferences = [];
        if (request.RequiredFiles is not null)
        {
            discardedReferences = await new RequiredDocumentSync(_db).SyncAsync(
                method.RequiredFiles,
                input => new ServiceTypeRequiredFile
                {
                    PaymentMethodId = method.Id,
                    NameAr = input.NameAr,
                    NameEn = input.NameEn,
                },
                request.RequiredFiles,
                cancellationToken);
        }

        await _lookups.SaveAsync(cancellationToken);

        // Only once the rows are gone are their files discarded, so a failed save never leaves a
        // barcode or a reference file pointing at bytes that no longer exist.
        foreach (var path in orphanedBarcodes.Concat(discardedReferences))
        {
            await _storage.DeleteAsync(path, cancellationToken);
        }

        await _lookups.AuditAsync(
            "PaymentMethod.Saved",
            nameof(PaymentMethod),
            method.Id,
            new
            {
                Method = method.NameEn,
                Type = type.NameEn,
                method.IsActive,
                Countries = countryIds.Count,
                GatewayIntegration = method.GatewayIntegration?.NameEn,
                // Which gateway secrets exist, never what they are.
                GatewaySecrets = GatewaySettingsJson.Read(method.GatewaySecretsJson)
                    .Keys.Order(StringComparer.Ordinal).ToList(),
                // Which credentials exist, never what they are.
                Integration = method.Integration is null
                    ? null
                    : new
                    {
                        method.Integration.Provider,
                        Mode = method.Integration.Mode.ToString(),
                        method.Integration.HasApiKey,
                        method.Integration.HasPassword,
                        method.Integration.HasWebhookSecret,
                    },
            },
            cancellationToken);

        method.Type = type;
        return AdminPaymentMethodDto.From(method, _lookups.Language, _protector.IsEnabled);
    }

    public async Task<LookupDeleteOutcome> Handle(
        DeletePaymentMethodCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Captured before the row can be removed, so the barcodes can be swept afterwards.
        var barcodePaths = await _db.PaymentMethodAccounts
            .AsNoTracking()
            .Where(account => account.PaymentMethodId == request.Id
                && account.BarcodeStoragePath != null)
            .Select(account => account.BarcodeStoragePath!)
            .ToListAsync(cancellationToken);

        // And the reference files on its documents, which go with the method's rows.
        var referencePaths = await _db.RequiredFileSamples
            .AsNoTracking()
            .Where(sample => sample.RequiredFile!.PaymentMethodId == request.Id)
            .Select(sample => sample.StoragePath)
            .ToListAsync(cancellationToken);
        barcodePaths.AddRange(referencePaths);

        var outcome = await _lookups.DeleteOrDeactivateAsync(
            _db.PaymentMethods,
            request.Id,
            ct => _db.WalletRequests.AnyAsync(r => r.PaymentMethodId == request.Id, ct),
            "PaymentMethod",
            cancellationToken);

        if (outcome == LookupDeleteOutcome.Deleted)
        {
            foreach (var path in barcodePaths)
            {
                await _storage.DeleteAsync(path, cancellationToken);
            }
        }

        return outcome;
    }

    private static IQueryable<PaymentMethod> WithGraph(IQueryable<PaymentMethod> source) =>
        source
            .Include(method => method.Type)
            .Include(method => method.CountryLinks)
            .Include(method => method.CurrencyLinks)
            .Include(method => method.Accounts)
            .ThenInclude(account => account.Bank)
            .Include(method => method.NotificationEmails)
            .Include(method => method.Integration)
            .Include(method => method.GatewayIntegration)
            .Include(method => method.RequiredFiles).ThenInclude(document => document.Fields)
                .ThenInclude(field => field.Options)
            .Include(method => method.RequiredFiles).ThenInclude(document => document.AllowedFileTypes)
            .Include(method => method.RequiredFiles).ThenInclude(document => document.Samples)
            // Split: documents, fields, options, formats, files, accounts and links joined into one
            // result set would multiply every row by every other.
            .AsSplitQuery();

    private PaymentMethod NewMethod(UpsertPaymentMethodCommand request)
    {
        var method = new PaymentMethod { NameAr = request.NameAr, NameEn = request.NameEn };
        _db.PaymentMethods.Add(method);
        return method;
    }

    private async Task EnsureCountriesExistAsync(
        List<Guid> countryIds,
        CancellationToken cancellationToken)
    {
        var known = await _db.Countries.CountAsync(c => countryIds.Contains(c.Id), cancellationToken);
        if (known != countryIds.Count)
        {
            throw new NotFoundException("One or more of the countries supplied do not exist.");
        }
    }

    /// <summary>
    /// A method may only accept a currency one of its countries actually uses. This is the same
    /// rule the editor enforces by only offering those currencies; it is repeated here because the
    /// editor is not the boundary.
    /// </summary>
    private async Task EnsureCurrenciesBelongToCountriesAsync(
        List<Guid> countryIds,
        IReadOnlyCollection<Guid> currencyIds,
        CancellationToken cancellationToken)
    {
        var allowed = await _db.CountryCurrencies
            .AsNoTracking()
            .Where(link => countryIds.Contains(link.CountryId))
            .Select(link => link.CurrencyId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var stray = currencyIds.Except(allowed).ToList();
        if (stray.Count > 0)
        {
            throw new ConflictException(
                "payment_method.currency_not_in_country",
                "A selected currency is not available in any of the selected countries.");
        }
    }

    /// <summary>
    /// A bank-transfer method may only name banks from the countries it serves. Same shape as the
    /// currency rule, and repeated here because the editor is not the boundary.
    /// </summary>
    private async Task EnsureBanksBelongToCountriesAsync(
        PaymentMethodType type,
        List<Guid> countryIds,
        IReadOnlyList<PaymentMethodAccountInput> accounts,
        CancellationToken cancellationToken)
    {
        if (!type.RequiresBank)
        {
            return;
        }

        var wanted = accounts
            .Where(account => account.BankId.HasValue)
            .Select(account => account.BankId!.Value)
            .Distinct()
            .ToList();

        if (wanted.Count == 0)
        {
            return;
        }

        var reachable = await _db.Banks
            .AsNoTracking()
            .Where(bank => wanted.Contains(bank.Id) && countryIds.Contains(bank.CountryId))
            .Select(bank => bank.Id)
            .ToListAsync(cancellationToken);

        if (reachable.Count != wanted.Count)
        {
            throw new ConflictException(
                "payment_method.bank_not_in_country",
                "A chosen bank does not operate in any of the selected countries.");
        }
    }

    private static void EnsureTypeRulesSatisfied(
        PaymentMethodType type,
        UpsertPaymentMethodCommand request)
    {
        if (type.RequiresExternalUrl)
        {
            if (string.IsNullOrWhiteSpace(request.ExternalUrl))
            {
                throw new ConflictException(
                    "payment_method.link_required",
                    "A method of this type needs the payment link applicants are sent to.");
            }

            if (!Uri.TryCreate(request.ExternalUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ConflictException(
                    "payment_method.link_invalid",
                    "The payment link must be a http:// or https:// address.");
            }
        }

        // Enforced only on an active method: an admin must be able to save a draft of a transfer
        // method and add its numbers afterwards, and IsUsable already keeps it off the picker.
        if (type.RequiresAccountNumber
            && request.IsActive
            && !request.Accounts.Any(account => account.IsActive
                && !string.IsNullOrWhiteSpace(account.AccountNumber)))
        {
            throw new ConflictException(
                "payment_method.accounts_required",
                "A method of this type needs at least one active receiving number before it can be published.");
        }
    }

    // A missing QR code is deliberately not a save-blocking error, unlike a missing number: a
    // barcode uploads against an account that must already exist, so refusing the save would make
    // a new InstaPay method impossible to create in one pass. IsUsable keeps such a method off the
    // applicant's picker instead, and the editor says which numbers are still waiting for a code.

    /// <summary>
    /// Applies the integration editor's payload.
    /// </summary>
    /// <remarks>
    /// Two rules do the real work here. A null payload leaves the stored row alone, so a save from
    /// a form that never showed the integration cannot erase it. And each secret is only replaced
    /// when the editor actually sent one — an untouched field sends null, so saving a name change
    /// does not wipe live credentials that the page could never display in the first place.
    /// </remarks>
    /// <summary>
    /// Points the method at a gateway integration and stores its settings for that gateway.
    ///
    /// A retired integration may stay on a method that already uses it, but cannot be newly chosen.
    /// Detaching the integration, or moving to one on a different gateway, clears the values: they
    /// only mean anything against the gateway they were entered for.
    /// </summary>
    private async Task SyncGatewayIntegrationAsync(
        PaymentMethod method,
        UpsertPaymentMethodCommand request,
        CancellationToken cancellationToken)
    {
        var integrationId = request.GatewayIntegrationId;

        if (integrationId is null || integrationId == Guid.Empty)
        {
            method.GatewayIntegrationId = null;
            method.GatewayIntegration = null;
            method.GatewaySettingsJson = "{}";
            method.GatewaySecretsJson = "{}";
            return;
        }

        var integration = method.GatewayIntegrationId == integrationId && method.GatewayIntegration is not null
            ? method.GatewayIntegration
            : await _db.PaymentGatewayIntegrations
                .FirstOrDefaultAsync(i => i.Id == integrationId, cancellationToken)
                ?? throw new NotFoundException(nameof(PaymentGatewayIntegration), integrationId);

        if (!integration.IsActive && method.GatewayIntegrationId != integration.Id)
        {
            throw new Common.Exceptions.ValidationException(
            [
                new FluentValidation.Results.ValidationFailure(
                    nameof(UpsertPaymentMethodCommand.GatewayIntegrationId),
                    "That integration is inactive; activate it or choose another."),
            ]);
        }

        var gateway = PaymentGatewayCatalog.Find(integration.GatewayCode)
            ?? throw new Common.Exceptions.ValidationException(
            [
                new FluentValidation.Results.ValidationFailure(
                    nameof(UpsertPaymentMethodCommand.GatewayIntegrationId),
                    "That integration's gateway is no longer available."),
            ]);

        // Secrets carry over only while the method stays on the same gateway.
        var kept = KeepsStoredSecrets(method, integration)
            ? GatewaySettingsJson.Read(method.GatewaySecretsJson)
            : new Dictionary<string, string>(StringComparer.Ordinal);

        var (settings, secrets) = GatewaySettingsBinder.Resolve(
            gateway,
            request.GatewaySettings,
            request.GatewaySecrets,
            kept,
            integration.Mode,
            _protector);

        method.GatewayIntegrationId = integration.Id;
        method.GatewayIntegration = integration;
        method.GatewaySettingsJson = GatewaySettingsJson.Write(settings);
        method.GatewaySecretsJson = GatewaySettingsJson.Write(secrets);
    }

    /// <summary>True while the method's stored secrets still belong to the gateway being saved.</summary>
    private bool KeepsStoredSecrets(PaymentMethod method, PaymentGatewayIntegration integration)
    {
        if (method.GatewayIntegrationId is null) return false;
        if (method.GatewayIntegrationId == integration.Id) return true;

        // A different integration on the same gateway asks for the same fields, but it is a
        // different account: its keys are not this one's.
        return false;
    }

    private void SyncIntegration(
        PaymentMethod method,
        PaymentMethodType type,
        PaymentIntegrationInput? input)
    {
        if (input is null)
        {
            return;
        }

        // Credentials belong to a method that talks to a provider — a payment link or PayPal.
        // Keeping them on a method switched to a manual transfer would leave live secrets attached
        // to a channel whose editor no longer shows them.
        if (!type.UsesProviderCredentials)
        {
            if (method.Integration is not null)
            {
                _db.PaymentMethodIntegrations.Remove(method.Integration);
                method.Integration = null;
            }

            return;
        }

        var integration = method.Integration;

        if (integration is null)
        {
            integration = new PaymentMethodIntegration { PaymentMethodId = method.Id };
            method.Integration = integration;
            _db.PaymentMethodIntegrations.Add(integration);
        }

        integration.Provider = Trim(input.Provider);
        integration.Mode = input.Mode;
        integration.MerchantId = Trim(input.MerchantId);
        integration.IntegrationId = Trim(input.IntegrationId);
        integration.BaseUrl = Trim(input.BaseUrl);
        integration.RedirectUrl = Trim(input.RedirectUrl);
        integration.CancelUrl = Trim(input.CancelUrl);
        integration.CallbackUrl = Trim(input.CallbackUrl);
        integration.SessionTimeoutMinutes = input.SessionTimeoutMinutes;
        integration.UpdatedAtUtc = DateTime.UtcNow;

        integration.ApiKeySecret = ApplySecret(input.ApiKey, integration.ApiKeySecret);
        integration.PasswordSecret = ApplySecret(input.Password, integration.PasswordSecret);
        integration.WebhookSecret = ApplySecret(input.WebhookSecret, integration.WebhookSecret);
    }

    /// <summary>
    /// null keeps what is stored, empty clears it, anything else replaces it — encrypted.
    /// </summary>
    private string? ApplySecret(string? submitted, string? stored)
    {
        if (submitted is null)
        {
            return stored;
        }

        return string.IsNullOrWhiteSpace(submitted)
            ? null
            : _protector.Protect(submitted.Trim());
    }

    private void SyncCountries(PaymentMethod method, IReadOnlyCollection<Guid> countryIds)
    {
        foreach (var removed in method.CountryLinks.Where(link => !countryIds.Contains(link.CountryId)).ToList())
        {
            method.CountryLinks.Remove(removed);
            _db.PaymentMethodCountries.Remove(removed);
        }

        var present = method.CountryLinks.Select(link => link.CountryId).ToHashSet();
        foreach (var countryId in countryIds.Where(id => !present.Contains(id)))
        {
            method.CountryLinks.Add(new PaymentMethodCountry
            {
                PaymentMethodId = method.Id,
                CountryId = countryId,
            });
        }
    }

    private void SyncCurrencies(PaymentMethod method, IReadOnlyCollection<Guid> currencyIds)
    {
        foreach (var removed in method.CurrencyLinks.Where(link => !currencyIds.Contains(link.CurrencyId)).ToList())
        {
            method.CurrencyLinks.Remove(removed);
            _db.PaymentMethodCurrencies.Remove(removed);
        }

        var present = method.CurrencyLinks.Select(link => link.CurrencyId).ToHashSet();
        foreach (var currencyId in currencyIds.Where(id => !present.Contains(id)))
        {
            method.CurrencyLinks.Add(new PaymentMethodCurrency
            {
                PaymentMethodId = method.Id,
                CurrencyId = currencyId,
            });
        }
    }

    /// <summary>
    /// Applies the submitted account list, editing matched rows in place so their uploaded
    /// barcodes survive. Returns the storage paths of barcodes whose rows were dropped, for the
    /// caller to sweep once the save has committed.
    /// </summary>
    private List<string> SyncAccounts(
        PaymentMethod method,
        PaymentMethodType type,
        IReadOnlyList<PaymentMethodAccountInput> submitted)
    {
        // Accounts only mean anything on a type that asks for them; switching a method to an
        // external link retires them rather than leaving invisible rows behind.
        var wanted = type.RequiresAccountNumber ? submitted : [];

        var keptIds = wanted
            .Where(account => account.Id is { } id && id != Guid.Empty)
            .Select(account => account.Id!.Value)
            .ToHashSet();

        var orphanedBarcodes = new List<string>();

        foreach (var removed in method.Accounts.Where(account => !keptIds.Contains(account.Id)).ToList())
        {
            if (removed.BarcodeStoragePath is { } path)
            {
                orphanedBarcodes.Add(path);
            }

            method.Accounts.Remove(removed);
            _db.PaymentMethodAccounts.Remove(removed);
        }

        foreach (var input in wanted)
        {
            var existing = input.Id is { } id && id != Guid.Empty
                ? method.Accounts.FirstOrDefault(account => account.Id == id)
                : null;

            if (existing is null)
            {
                existing = new PaymentMethodAccount
                {
                    PaymentMethodId = method.Id,
                    AccountNumber = input.AccountNumber.Trim(),
                };

                method.Accounts.Add(existing);
            }

            existing.LabelAr = input.LabelAr?.Trim() ?? string.Empty;
            existing.LabelEn = input.LabelEn?.Trim() ?? string.Empty;
            existing.AccountNumber = input.AccountNumber.Trim();
            existing.AccountHolder = Trim(input.AccountHolder);
            // Only a bank-transfer type has banks; switching a method away from one clears them
            // rather than leaving a stale reference nothing reads.
            existing.BankId = type.RequiresBank ? input.BankId : null;
            existing.IsActive = input.IsActive;
            existing.SortOrder = input.SortOrder;
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }

        return orphanedBarcodes;
    }

    /// <summary>
    /// Applies the submitted recipient list, editing matched rows in place. Replace-set semantics,
    /// the same as accounts: a mailbox removed from the editor stops being notified.
    /// </summary>
    private void SyncNotificationEmails(
        PaymentMethod method,
        IReadOnlyList<PaymentNotificationEmailInput> submitted)
    {
        var keptIds = submitted
            .Where(recipient => recipient.Id is { } id && id != Guid.Empty)
            .Select(recipient => recipient.Id!.Value)
            .ToHashSet();

        foreach (var removed in method.NotificationEmails.Where(r => !keptIds.Contains(r.Id)).ToList())
        {
            method.NotificationEmails.Remove(removed);
            _db.PaymentMethodNotificationEmails.Remove(removed);
        }

        foreach (var input in submitted.Where(r => !string.IsNullOrWhiteSpace(r.Email)))
        {
            var existing = input.Id is { } id && id != Guid.Empty
                ? method.NotificationEmails.FirstOrDefault(recipient => recipient.Id == id)
                : null;

            if (existing is null)
            {
                existing = new PaymentMethodNotificationEmail
                {
                    PaymentMethodId = method.Id,
                    Email = input.Email.Trim(),
                };

                method.NotificationEmails.Add(existing);
            }

            existing.Email = input.Email.Trim();
            existing.DisplayName = Trim(input.DisplayName);
            existing.NotifyOnSubmitted = input.NotifyOnSubmitted;
            existing.NotifyOnApproved = input.NotifyOnApproved;
            existing.NotifyOnRejected = input.NotifyOnRejected;
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
