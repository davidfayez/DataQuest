using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Common;
using DataVerification.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Payments.Admin;

/// <summary>
/// Attaches the scannable image an applicant points their banking app at. Uploaded in a second
/// step because the account row has to exist before a file can hang off it.
/// </summary>
public sealed record UploadAccountBarcodeCommand(
    Guid AccountId,
    string FileName,
    long SizeBytes,
    Stream Content) : IRequest<PaymentMethodAccountDto>;

public sealed record DeleteAccountBarcodeCommand(Guid AccountId) : IRequest<PaymentMethodAccountDto>;

/// <summary>
/// The barcode bytes. Served to administrators configuring the method and to the applicant paying
/// through it, so the handler deliberately carries no realm check of its own — the two controllers
/// that expose it apply their own.
/// </summary>
public sealed record GetAccountBarcodeQuery(Guid AccountId) : IRequest<FileDownload>;

public sealed class PaymentMethodBarcodeHandlers :
    IRequestHandler<UploadAccountBarcodeCommand, PaymentMethodAccountDto>,
    IRequestHandler<DeleteAccountBarcodeCommand, PaymentMethodAccountDto>,
    IRequestHandler<GetAccountBarcodeQuery, FileDownload>
{
    /// <summary>A QR code never needs more than this, and the cap keeps the admin form honest.</summary>
    public const long MaxBarcodeBytes = 2 * 1024 * 1024;

    private const string StorageDirectory = "payment-barcodes";

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorage _storage;
    private readonly IFileTypeValidator _typeValidator;
    private readonly IAuditLogger _auditLogger;

    public PaymentMethodBarcodeHandlers(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IFileStorage storage,
        IFileTypeValidator typeValidator,
        IAuditLogger auditLogger)
    {
        _db = db;
        _currentUser = currentUser;
        _storage = storage;
        _typeValidator = typeValidator;
        _auditLogger = auditLogger;
    }

    public async Task<PaymentMethodAccountDto> Handle(
        UploadAccountBarcodeCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var account = await _db.PaymentMethodAccounts
            .Include(a => a.PaymentMethod)
            .ThenInclude(m => m!.Type)
            .FirstOrDefaultAsync(a => a.Id == request.AccountId, cancellationToken)
            ?? throw new NotFoundException(nameof(PaymentMethodAccount), request.AccountId);

        if (account.PaymentMethod?.Type?.RequiresBarcode != true)
        {
            throw new DomainException(
                "payment_account.barcode_not_supported",
                "This payment type does not use barcodes.");
        }

        if (request.SizeBytes <= 0 || request.SizeBytes > MaxBarcodeBytes)
        {
            throw new DomainException(
                "payment_account.barcode_too_large",
                $"The barcode must be between 1 byte and {MaxBarcodeBytes / (1024 * 1024)} MB.");
        }

        // Signature check, not an extension check: the declared name proves nothing.
        var detected = await _typeValidator.DetectAllowedContentTypeAsync(
            request.Content, request.FileName, cancellationToken);

        if (detected is not ("image/jpeg" or "image/png"))
        {
            throw new DomainException(
                "payment_account.barcode_type_not_allowed",
                "The barcode must be a JPEG or a PNG.");
        }

        var previousPath = account.BarcodeStoragePath;

        var storedPath = await _storage.SaveAsync(
            request.Content,
            StorageDirectory,
            request.FileName,
            account.Id.ToString(),
            cancellationToken);

        account.BarcodeStoragePath = storedPath;
        account.BarcodeContentType = detected;
        account.BarcodeFileName = request.FileName;
        account.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        // Only once the new file is safely recorded is the old one discarded.
        if (!string.IsNullOrWhiteSpace(previousPath) && previousPath != storedPath)
        {
            await _storage.DeleteAsync(previousPath, cancellationToken);
        }

        await _auditLogger.LogAsync(
            "PaymentMethodAccount.BarcodeUploaded",
            nameof(PaymentMethodAccount),
            account.Id,
            new { request.FileName, request.SizeBytes },
            cancellationToken);

        return PaymentMethodAccountDto.From(account, _currentUser.LanguageCode);
    }

    public async Task<PaymentMethodAccountDto> Handle(
        DeleteAccountBarcodeCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var account = await _db.PaymentMethodAccounts
            .FirstOrDefaultAsync(a => a.Id == request.AccountId, cancellationToken)
            ?? throw new NotFoundException(nameof(PaymentMethodAccount), request.AccountId);

        var path = account.BarcodeStoragePath;

        account.BarcodeStoragePath = null;
        account.BarcodeContentType = null;
        account.BarcodeFileName = null;
        account.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(path))
        {
            await _storage.DeleteAsync(path, cancellationToken);
        }

        await _auditLogger.LogAsync(
            "PaymentMethodAccount.BarcodeRemoved",
            nameof(PaymentMethodAccount),
            account.Id,
            null,
            cancellationToken);

        return PaymentMethodAccountDto.From(account, _currentUser.LanguageCode);
    }

    public async Task<FileDownload> Handle(
        GetAccountBarcodeQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var account = await _db.PaymentMethodAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == request.AccountId, cancellationToken)
            ?? throw new NotFoundException(nameof(PaymentMethodAccount), request.AccountId);

        if (!account.HasBarcode)
        {
            throw new NotFoundException("Barcode for payment account", request.AccountId);
        }

        var content = await _storage.OpenReadAsync(account.BarcodeStoragePath!, cancellationToken);

        return new FileDownload(
            content,
            account.BarcodeContentType ?? "application/octet-stream",
            account.BarcodeFileName ?? "barcode");
    }
}
