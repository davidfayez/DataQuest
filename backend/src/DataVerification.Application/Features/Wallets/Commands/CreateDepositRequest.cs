using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Features.Payments;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DataVerification.Application.Features.Wallets.Commands;

/// <summary>One receipt as it arrives from the deposit form, before it is written to storage.</summary>
public sealed record DepositProofUpload(string FileName, long SizeBytes, Stream Content);

/// <summary>One file attached to one of the method's required documents.</summary>
public sealed record DepositDocumentUpload(Guid RequiredFileId, string FileName, long SizeBytes, Stream Content);

/// <summary>One detail typed beside a required document.</summary>
public sealed record DepositDocumentValue(Guid RequiredFileFieldId, string? Value);

/// <summary>
/// Raises a top-up request through a configured payment method. What the applicant must supply —
/// which account they paid, proof of it, the method's own required documents and their details —
/// is decided by the method and its type rather than by this command, so adding a provider never
/// reaches here.
/// </summary>
/// <remarks>
/// Deliberately separate from <see cref="CreateWalletRequestCommand"/>, which still serves
/// withdrawals: a payout carries no method, no reference and no receipt, and folding both into one
/// multipart endpoint would make each direction's rules conditional on the other's.
/// </remarks>
public sealed record CreateDepositRequestCommand(
    Guid PaymentMethodId,
    Guid? PaymentMethodAccountId,
    decimal Amount,
    string? ReferenceNumber,
    string? Note,
    IReadOnlyList<DepositProofUpload> Files,
    IReadOnlyList<DepositDocumentUpload>? DocumentFiles = null,
    IReadOnlyList<DepositDocumentValue>? DocumentValues = null,
    /// <summary>Which balance the money is for; the order's main currency when omitted.</summary>
    Guid? CurrencyId = null) : IRequest<WalletRequestDto>;

public sealed class CreateDepositRequestCommandValidator : AbstractValidator<CreateDepositRequestCommand>
{
    public CreateDepositRequestCommandValidator(IOptions<WalletOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var limits = options.Value;

        RuleFor(c => c.PaymentMethodId).NotEmpty().WithMessage("Choose a payment method.");

        RuleFor(c => c.Amount)
            .GreaterThanOrEqualTo(limits.MinimumRequestAmount)
            .WithMessage($"The amount must be at least {limits.MinimumRequestAmount:0.00}.")
            .LessThanOrEqualTo(limits.MaximumRequestAmount)
            .WithMessage($"The amount may not exceed {limits.MaximumRequestAmount:0.00}.");

        RuleFor(c => c.ReferenceNumber).MaximumLength(100);
        RuleFor(c => c.Note).MaximumLength(1000);

        RuleForEach(c => c.DocumentValues).ChildRules(value =>
        {
            value.RuleFor(v => v.RequiredFileFieldId).NotEmpty();
            value.RuleFor(v => v.Value).MaximumLength(1000);
        });
    }
}

public sealed class CreateDepositRequestCommandHandler
    : IRequestHandler<CreateDepositRequestCommand, WalletRequestDto>
{
    /// <summary>A receipt or screenshot; the same cap the applicant's other uploads carry.</summary>
    public const long MaxProofBytes = 5 * 1024 * 1024;

    /// <summary>Enough for a receipt and a screenshot of the confirmation, and no more.</summary>
    public const int MaxProofFiles = 5;

    /// <summary>
    /// Every file across the method's required documents together. Each document also has its own
    /// cap; this one bounds the request as a whole.
    /// </summary>
    public const int MaxDocumentFiles = 10;

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorage _storage;
    private readonly IFileTypeValidator _typeValidator;
    private readonly IAuditLogger _auditLogger;
    private readonly PaymentNotifier _notifier;
    private readonly WalletOptions _options;
    private readonly WalletBook _wallets;

    public CreateDepositRequestCommandHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IFileStorage storage,
        IFileTypeValidator typeValidator,
        IAuditLogger auditLogger,
        PaymentNotifier notifier,
        IOptions<WalletOptions> options,
        WalletBook wallets)
    {
        ArgumentNullException.ThrowIfNull(options);

        _db = db;
        _currentUser = currentUser;
        _storage = storage;
        _typeValidator = typeValidator;
        _auditLogger = auditLogger;
        _notifier = notifier;
        _options = options.Value;
        _wallets = wallets;
    }

    public async Task<WalletRequestDto> Handle(
        CreateDepositRequestCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var orderId = _currentUser.OrderId
            ?? throw new ForbiddenAccessException("This endpoint is only available to applicants.");

        if (!_options.AllowDepositRequests)
        {
            throw new ConflictException(
                "wallet_request.disabled",
                "Deposit requests are not available at the moment.");
        }

        // The balance the money is for, opened the first time money arrives in its currency. The
        // order comes with it, already tracked.
        var wallet = await _wallets.OpenAsync(orderId, request.CurrencyId, cancellationToken);
        var order = wallet.Order!;

        var method = await LoadPayableMethodAsync(order, request.PaymentMethodId, wallet.CurrencyId, cancellationToken);
        var type = method.Type!;

        var account = ResolveAccount(method, type, request.PaymentMethodAccountId);
        var reference = await ResolveReferenceAsync(type, request.ReferenceNumber, cancellationToken);
        var files = ValidateProof(type, request.Files);

        var documents = method.RequiredFiles.Where(document => document.IsActive).ToList();
        var documentFiles = ValidateDocuments(documents, request.DocumentFiles ?? []);
        var values = ResolveValues(documents, request.DocumentValues ?? []);

        // One pending top-up at a time per balance. A second one almost always means the first was
        // forgotten, and two pending claims against one wallet are impossible to reconcile.
        var hasPending = await _db.WalletRequests.AnyAsync(
            r => r.OrderId == orderId
                && r.WalletId == wallet.Id
                && r.Type == WalletRequestType.Deposit
                && r.Status == WalletRequestStatus.Pending,
            cancellationToken);

        if (hasPending)
        {
            throw new ConflictException(
                "wallet_request.already_pending",
                "A request of this kind is already awaiting a decision.");
        }

        var walletRequest = new WalletRequest
        {
            OrderId = orderId,
            WalletId = wallet.Id,
            Type = WalletRequestType.Deposit,
            Amount = request.Amount,
            ApplicantNote = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            RequestedByName = _currentUser.DisplayName ?? order.OrderNumber,
            PaymentMethodId = method.Id,
            PaymentMethodAccountId = account?.Id,
            ReferenceNumber = reference,
        };

        // Written before the row so a storage failure leaves no request claiming files it lacks —
        // and anything already written is swept if a later file is refused.
        try
        {
            foreach (var upload in files)
            {
                walletRequest.Files.Add(await StoreProofAsync(orderId, walletRequest.Id, upload, cancellationToken));
            }

            foreach (var (document, upload) in documentFiles)
            {
                walletRequest.Files.Add(
                    await StoreDocumentAsync(orderId, walletRequest.Id, document, upload, cancellationToken));
            }
        }
        catch
        {
            await DiscardStoredFilesAsync(walletRequest, CancellationToken.None);
            throw;
        }

        foreach (var value in values)
        {
            value.WalletRequestId = walletRequest.Id;
            walletRequest.DocumentValues.Add(value);
        }

        _db.WalletRequests.Add(walletRequest);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Bytes were written before the row, so a rejected save must not leave them orphaned.
            await DiscardStoredFilesAsync(walletRequest, cancellationToken);

            // Two applicants racing on the same receipt: the unique index is the real arbiter, and
            // the pre-check above only exists to phrase the common case nicely.
            if (reference is not null)
            {
                throw new ConflictException(
                    "wallet_request.duplicate_reference",
                    "This transfer reference has already been submitted.");
            }

            throw;
        }

        await _auditLogger.LogAsync(
            "WalletRequest.Created",
            "WalletRequest",
            walletRequest.Id,
            new
            {
                orderId,
                Type = WalletRequestType.Deposit,
                request.Amount,
                Method = method.NameEn,
                Reference = reference,
                Files = walletRequest.Files.Count,
                DocumentFiles = documentFiles.Count,
                Details = walletRequest.DocumentValues.Count,
            },
            cancellationToken);

        walletRequest.PaymentMethod = method;
        walletRequest.PaymentMethodAccount = account;

        // Whoever reconciles this method hears about the claim straight away. Delivery failures are
        // swallowed by the sender, so a lost email can never undo a committed request.
        await _notifier.NotifyAsync(
            method,
            WalletRequestStatus.Pending,
            order.OrderNumber,
            request.Amount,
            wallet.Currency?.Code ?? string.Empty,
            reference,
            decidedBy: null,
            reviewerNote: null,
            cancellationToken);

        return WalletRequestDto.From(
            walletRequest,
            order.OrderNumber,
            wallet.Currency?.Code ?? string.Empty,
            _currentUser.LanguageCode);
    }

    /// <summary>
    /// Loads the method only if this order may pay through it in this currency — same country, the
    /// currency accepted, still usable. Re-checked here rather than trusted from the picker, which is
    /// not the boundary.
    /// </summary>
    private async Task<PaymentMethod> LoadPayableMethodAsync(
        Order order,
        Guid methodId,
        Guid currencyId,
        CancellationToken cancellationToken)
    {
        if (order.VerificationCountryId is not { } countryId)
        {
            throw new ConflictException(
                "order.setup_incomplete",
                "Finish setting up the order before adding funds.");
        }

        var method = await _db.PaymentMethods
            .Include(m => m.Type)
            .Include(m => m.Accounts)
            .Include(m => m.NotificationEmails)
            .Include(m => m.RequiredFiles).ThenInclude(d => d.Fields).ThenInclude(f => f.Options)
            .Include(m => m.RequiredFiles).ThenInclude(d => d.AllowedFileTypes)
            .AsSplitQuery()
            .FirstOrDefaultAsync(m => m.Id == methodId, cancellationToken)
            ?? throw new NotFoundException(nameof(PaymentMethod), methodId);

        var offeredHere = await _db.PaymentMethodCountries
            .AnyAsync(link => link.PaymentMethodId == methodId && link.CountryId == countryId, cancellationToken);

        var acceptsCurrency = await _db.PaymentMethodCurrencies
            .AnyAsync(link => link.PaymentMethodId == methodId && link.CurrencyId == currencyId, cancellationToken);

        if (!offeredHere || !acceptsCurrency || method.Type is null || !method.IsUsable(method.Type))
        {
            throw new ConflictException(
                "payment_method.unavailable",
                "This payment method is not available for your order.");
        }

        return method;
    }

    private static PaymentMethodAccount? ResolveAccount(
        PaymentMethod method,
        PaymentMethodType type,
        Guid? accountId)
    {
        if (!type.RequiresAccountNumber)
        {
            return null;
        }

        if (accountId is not { } id)
        {
            throw new ConflictException(
                "wallet_request.account_required",
                "Choose the account you transferred to.");
        }

        var account = method.Accounts.FirstOrDefault(a => a.Id == id && a.IsActive)
            ?? throw new ConflictException(
                "wallet_request.account_invalid",
                "That account is not available on this payment method.");

        return account;
    }

    private async Task<string?> ResolveReferenceAsync(
        PaymentMethodType type,
        string? submitted,
        CancellationToken cancellationToken)
    {
        var reference = string.IsNullOrWhiteSpace(submitted) ? null : submitted.Trim();

        // No longer demanded of the applicant, whatever the type is configured to want: the form
        // stopped asking for it. Nothing is weakened by that — a reference typed off a slip was
        // never the guard on double-crediting. The reviewer records the reference they matched on
        // the statement when they approve, and that one is checked against every credited request.
        if (reference is not null && await IsDuplicateReferenceAsync(reference, cancellationToken))
        {
            throw new ConflictException(
                "wallet_request.duplicate_reference",
                "This transfer reference has already been submitted.");
        }

        return reference;
    }

    private Task<bool> IsDuplicateReferenceAsync(string? reference, CancellationToken cancellationToken) =>
        reference is null
            ? Task.FromResult(false)
            : _db.WalletRequests.AnyAsync(r => r.ReferenceNumber == reference, cancellationToken);

    private static List<DepositProofUpload> ValidateProof(
        PaymentMethodType type,
        IReadOnlyList<DepositProofUpload> files)
    {
        var supplied = files.Where(file => file.SizeBytes > 0).ToList();

        if (type.RequiresProofDocument && supplied.Count == 0)
        {
            throw new ConflictException(
                "wallet_request.proof_required",
                "Attach proof of the transfer — a receipt or a screenshot.");
        }

        if (supplied.Count > MaxProofFiles)
        {
            throw new ConflictException(
                "wallet_request.too_many_files",
                $"Attach at most {MaxProofFiles} files.");
        }

        if (supplied.Any(file => file.SizeBytes > MaxProofBytes))
        {
            throw new ConflictException(
                "file.too_large",
                $"Files must be {MaxProofBytes / (1024 * 1024)} MB or smaller.");
        }

        return supplied;
    }

    /// <summary>
    /// Matches each file to the document it was sent for and applies that document's limits — the
    /// same limits the wizard applies to a service's documents.
    /// </summary>
    private static List<(ServiceTypeRequiredFile Document, DepositDocumentUpload Upload)> ValidateDocuments(
        IReadOnlyList<ServiceTypeRequiredFile> documents,
        IReadOnlyList<DepositDocumentUpload> uploads)
    {
        var supplied = uploads.Where(upload => upload.SizeBytes > 0).ToList();

        if (supplied.Count > MaxDocumentFiles)
        {
            throw new ConflictException(
                "wallet_request.too_many_files",
                $"Attach at most {MaxDocumentFiles} files across the required documents.");
        }

        var byId = documents.ToDictionary(document => document.Id);
        var matched = new List<(ServiceTypeRequiredFile Document, DepositDocumentUpload Upload)>();

        foreach (var upload in supplied)
        {
            if (!byId.TryGetValue(upload.RequiredFileId, out var document))
            {
                throw new ConflictException(
                    "wallet_request.document_unknown",
                    "A file was attached to a document this payment method does not ask for.");
            }

            var limit = document.ResolveMaxSizeBytes(ApplicationFile.MaxFileSizeBytes);
            if (upload.SizeBytes > limit)
            {
                throw new ConflictException(
                    "file.too_large_for_document",
                    $"Files for '{document.NameEn}' must be {Math.Max(1, limit / 1024)} KB or smaller.");
            }

            matched.Add((document, upload));
        }

        foreach (var document in documents)
        {
            var count = matched.Count(entry => entry.Document.Id == document.Id);

            if (count > document.MaxFiles)
            {
                throw new ConflictException(
                    "file.too_many_for_document",
                    $"'{document.NameEn}' accepts at most {document.MaxFiles} file(s).");
            }

            if (document.IsMandatory && count == 0)
            {
                throw new ConflictException(
                    "wallet_request.document_required",
                    $"Attach '{document.NameEn}'.");
            }
        }

        return matched;
    }

    /// <summary>
    /// Checks every detail the method's documents ask for against its own rules, and copies what
    /// was entered — with the names it was entered under — onto the request.
    /// </summary>
    private static List<WalletRequestDocumentValue> ResolveValues(
        IReadOnlyList<ServiceTypeRequiredFile> documents,
        IReadOnlyList<DepositDocumentValue> submitted)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var fields = documents
            .OrderBy(document => document.IsMandatory ? 0 : 1)
            .ThenBy(document => document.NameEn)
            .SelectMany(document => document.Fields
                .Where(field => field.IsActive)
                .OrderBy(field => field.SortOrder)
                .ThenBy(field => field.NameEn)
                .Select(field => (Document: document, Field: field)))
            .ToList();

        var known = fields.Select(entry => entry.Field.Id).ToHashSet();
        var entered = new Dictionary<Guid, string?>();

        foreach (var input in submitted)
        {
            if (!known.Contains(input.RequiredFileFieldId))
            {
                throw new ConflictException(
                    "wallet_request.field_unknown",
                    "A detail was sent for a field this payment method does not ask for.");
            }

            entered[input.RequiredFileFieldId] = string.IsNullOrWhiteSpace(input.Value)
                ? null
                : input.Value.Trim();
        }

        var values = new List<WalletRequestDocumentValue>();
        var order = 0;

        foreach (var (document, field) in fields)
        {
            var value = entered.GetValueOrDefault(field.Id);

            if (field.Validate(value, today) is { } error)
            {
                throw new ConflictException(
                    error,
                    error == "field.required"
                        ? $"Fill in '{field.NameEn}' for '{document.NameEn}'."
                        : $"'{field.NameEn}' for '{document.NameEn}' is not valid.");
            }

            if (value is null)
            {
                continue;
            }

            var option = field.FieldType == RequiredFieldType.Dropdown
                ? field.Options.FirstOrDefault(candidate => candidate.Value == value)
                : null;

            values.Add(new WalletRequestDocumentValue
            {
                RequiredFileId = document.Id,
                RequiredFileFieldId = field.Id,
                DocumentNameAr = document.NameAr,
                DocumentNameEn = document.NameEn,
                FieldNameAr = field.NameAr,
                FieldNameEn = field.NameEn,
                Value = value,
                ValueLabelAr = option?.LabelAr,
                ValueLabelEn = option?.LabelEn,
                SortOrder = order++,
            });
        }

        return values;
    }

    private async Task<WalletRequestFile> StoreProofAsync(
        Guid orderId,
        Guid requestId,
        DepositProofUpload upload,
        CancellationToken cancellationToken)
    {
        // Extension alone is not evidence of anything — the signature has to agree with it.
        var contentType = await _typeValidator.DetectAllowedContentTypeAsync(
            upload.Content,
            upload.FileName,
            cancellationToken)
            ?? throw new ConflictException(
                "file.unsupported_type",
                "Only PDF, JPG, JPEG and PNG files are accepted.");

        var storagePath = await _storage.SaveAsync(
            upload.Content,
            $"orders/{orderId}/wallet-requests",
            upload.FileName,
            requestId.ToString("N"),
            cancellationToken);

        return new WalletRequestFile
        {
            WalletRequestId = requestId,
            FileName = Path.GetFileName(upload.FileName),
            ContentType = contentType,
            StoragePath = storagePath,
            SizeBytes = upload.SizeBytes,
            UploadedByName = _currentUser.DisplayName,
        };
    }

    private async Task<WalletRequestFile> StoreDocumentAsync(
        Guid orderId,
        Guid requestId,
        ServiceTypeRequiredFile document,
        DepositDocumentUpload upload,
        CancellationToken cancellationToken)
    {
        // Narrowed to the formats this document accepts, and checked by signature, not by name.
        var contentType = await _typeValidator.DetectAllowedContentTypeAsync(
            upload.Content,
            upload.FileName,
            document.ResolveAllowedFileTypes(),
            cancellationToken)
            ?? throw new ConflictException(
                "file.unsupported_type",
                $"'{document.NameEn}' does not accept this kind of file.");

        var storagePath = await _storage.SaveAsync(
            upload.Content,
            $"orders/{orderId}/wallet-requests",
            upload.FileName,
            requestId.ToString("N"),
            cancellationToken);

        return new WalletRequestFile
        {
            WalletRequestId = requestId,
            FileName = Path.GetFileName(upload.FileName),
            ContentType = contentType,
            StoragePath = storagePath,
            SizeBytes = upload.SizeBytes,
            UploadedByName = _currentUser.DisplayName,
            RequiredFileId = document.Id,
            DocumentNameAr = document.NameAr,
            DocumentNameEn = document.NameEn,
        };
    }

    /// <summary>Sweeps bytes written for a request that never landed, so storage cannot leak.</summary>
    private async Task DiscardStoredFilesAsync(WalletRequest request, CancellationToken cancellationToken)
    {
        foreach (var file in request.Files)
        {
            await _storage.DeleteAsync(file.StoragePath, cancellationToken);
        }
    }
}
