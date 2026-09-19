using System.Linq.Expressions;
using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Common;
using DataVerification.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Lookups.Admin;

/// <summary>
/// Attaches one labelled reference file to a required document.
/// </summary>
/// <remarks>
/// Uploaded on its own rather than inside the owner's save: the file needs a saved document to
/// belong to, and a multi-megabyte body has no business riding along with every edit to a price.
/// </remarks>
/// <param name="Owner">
/// Which editor is asking. A document of the other kind is reported as not found, so the endpoint
/// behind one permission cannot reach documents guarded by another.
/// </param>
public sealed record UploadRequiredFileSampleCommand(
    Guid RequiredFileId,
    string? LabelAr,
    string? LabelEn,
    Stream Content,
    string FileName,
    long SizeBytes,
    RequiredDocumentOwner Owner = RequiredDocumentOwner.ServiceType) : IRequest<RequiredFileSampleDto>;

/// <summary>Removes a reference file, and its bytes.</summary>
public sealed record DeleteRequiredFileSampleCommand(
    Guid SampleId,
    RequiredDocumentOwner Owner = RequiredDocumentOwner.ServiceType) : IRequest<Unit>;

/// <summary>
/// The bytes of one reference file.
/// </summary>
/// <param name="ForApplicant">
/// True for the applicant's endpoint, which only serves files on an active document of an active
/// service or payment method — the same documents the applicant can be shown.
/// </param>
/// <param name="Owner">For the admin endpoints: which kind of document they may read.</param>
public sealed record GetRequiredFileSampleQuery(
    Guid SampleId,
    bool ForApplicant,
    RequiredDocumentOwner? Owner = null) : IRequest<RequiredFileSampleDownload>;

public sealed record RequiredFileSampleDownload(Stream Content, string ContentType, string FileName);

public sealed class UploadRequiredFileSampleCommandValidator
    : AbstractValidator<UploadRequiredFileSampleCommand>
{
    public UploadRequiredFileSampleCommandValidator()
    {
        RuleFor(c => c.RequiredFileId).NotEmpty();

        // Both languages, as everywhere an applicant reads a label: they see only one of them.
        RuleFor(c => c.LabelAr).NotEmpty().WithMessage("Enter the Arabic label.")
            .MaximumLength(RequiredFileSampleLimits.MaxLabelLength);
        RuleFor(c => c.LabelEn).NotEmpty().WithMessage("Enter the English label.")
            .MaximumLength(RequiredFileSampleLimits.MaxLabelLength);

        RuleFor(c => c.FileName).NotEmpty();
    }
}

public sealed class RequiredFileSampleHandlers :
    IRequestHandler<UploadRequiredFileSampleCommand, RequiredFileSampleDto>,
    IRequestHandler<DeleteRequiredFileSampleCommand, Unit>,
    IRequestHandler<GetRequiredFileSampleQuery, RequiredFileSampleDownload>
{
    private readonly IApplicationDbContext _db;
    private readonly IFileStorage _storage;
    private readonly IFileTypeValidator _typeValidator;
    private readonly IAuditLogger _auditLogger;
    private readonly ICurrentUser _currentUser;

    public RequiredFileSampleHandlers(
        IApplicationDbContext db,
        IFileStorage storage,
        IFileTypeValidator typeValidator,
        IAuditLogger auditLogger,
        ICurrentUser currentUser)
    {
        _db = db;
        _storage = storage;
        _typeValidator = typeValidator;
        _auditLogger = auditLogger;
        _currentUser = currentUser;
    }

    /// <summary>Documents of one kind only.</summary>
    private static Expression<Func<ServiceTypeRequiredFile, bool>> OwnedBy(RequiredDocumentOwner owner) =>
        owner == RequiredDocumentOwner.PaymentMethod
            ? document => document.PaymentMethodId != null
            : document => document.ServiceTypeId != null;

    /// <summary>The audit entry names the owner, so the two editors' trails stay apart.</summary>
    private static string AuditPrefix(RequiredDocumentOwner owner) =>
        owner == RequiredDocumentOwner.PaymentMethod ? "PaymentMethod" : "ServiceType";

    public async Task<RequiredFileSampleDto> Handle(
        UploadRequiredFileSampleCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var document = await _db.ServiceTypeRequiredFiles
            .Include(f => f.Samples)
            .Where(OwnedBy(request.Owner))
            .FirstOrDefaultAsync(f => f.Id == request.RequiredFileId, cancellationToken)
            ?? throw new NotFoundException(nameof(ServiceTypeRequiredFile), request.RequiredFileId);

        if (document.Samples.Count >= RequiredFileSampleLimits.MaxPerDocument)
        {
            throw new DomainException(
                "required_file_sample.too_many",
                $"A document can carry at most {RequiredFileSampleLimits.MaxPerDocument} reference files.");
        }

        if (request.SizeBytes <= 0 || request.SizeBytes > RequiredFileSampleLimits.MaxFileSizeBytes)
        {
            throw new DomainException(
                "required_file_sample.too_large",
                $"A reference file must be between 1 byte and "
                + $"{RequiredFileSampleLimits.MaxFileSizeBytes / (1024 * 1024)} MB.");
        }

        // Signature check, not an extension check: the declared name proves nothing.
        var detected = await _typeValidator.DetectAllowedContentTypeAsync(
                request.Content, request.FileName, cancellationToken)
            ?? throw new DomainException(
                "required_file_sample.type_not_allowed",
                "A reference file must be a PDF, JPG, PNG, Word or Excel file.");

        var storedPath = await _storage.SaveAsync(
            request.Content,
            RequiredFileSampleLimits.StorageDirectory,
            request.FileName,
            document.Id.ToString(),
            cancellationToken);

        var sample = new RequiredFileSample
        {
            RequiredFileId = document.Id,
            LabelAr = request.LabelAr!.Trim(),
            LabelEn = request.LabelEn!.Trim(),
            FileName = Path.GetFileName(request.FileName),
            StoragePath = storedPath,
            ContentType = detected,
            SizeBytes = request.SizeBytes,
            SortOrder = document.Samples.Count == 0 ? 0 : document.Samples.Max(s => s.SortOrder) + 1,
        };

        _db.RequiredFileSamples.Add(sample);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // A file with no row pointing at it would sit in storage for ever.
            await _storage.DeleteAsync(storedPath, CancellationToken.None);
            throw;
        }

        await _auditLogger.LogAsync(
            $"{AuditPrefix(request.Owner)}.ReferenceFileAdded",
            nameof(ServiceTypeRequiredFile),
            document.Id,
            new { SampleId = sample.Id, sample.FileName, sample.SizeBytes },
            cancellationToken);

        return RequiredFileSampleDto.From(sample, _currentUser.LanguageCode);
    }

    public async Task<Unit> Handle(
        DeleteRequiredFileSampleCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var owned = OwnedBy(request.Owner);

        var sample = await _db.RequiredFileSamples
            .Where(s => _db.ServiceTypeRequiredFiles.Where(owned).Any(f => f.Id == s.RequiredFileId))
            .FirstOrDefaultAsync(s => s.Id == request.SampleId, cancellationToken)
            ?? throw new NotFoundException(nameof(RequiredFileSample), request.SampleId);

        _db.RequiredFileSamples.Remove(sample);
        await _db.SaveChangesAsync(cancellationToken);

        // Only once the row is gone, so a failed save never leaves a row pointing at nothing.
        await _storage.DeleteAsync(sample.StoragePath, cancellationToken);

        await _auditLogger.LogAsync(
            $"{AuditPrefix(request.Owner)}.ReferenceFileRemoved",
            nameof(ServiceTypeRequiredFile),
            sample.RequiredFileId,
            new { SampleId = sample.Id, sample.FileName },
            cancellationToken);

        return Unit.Value;
    }

    public async Task<RequiredFileSampleDownload> Handle(
        GetRequiredFileSampleQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = _db.RequiredFileSamples
            .AsNoTracking()
            .Where(s => s.Id == request.SampleId);

        if (request.ForApplicant)
        {
            query = query.Where(s => s.RequiredFile!.IsActive
                && ((s.RequiredFile.ServiceTypeId != null && s.RequiredFile.ServiceType!.IsActive)
                    || (s.RequiredFile.PaymentMethodId != null && s.RequiredFile.PaymentMethod!.IsActive)));
        }

        if (request.Owner is { } owner)
        {
            var owned = OwnedBy(owner);
            query = query.Where(s => _db.ServiceTypeRequiredFiles.Where(owned).Any(f => f.Id == s.RequiredFileId));
        }

        var sample = await query
            .Select(s => new { s.StoragePath, s.ContentType, s.FileName })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(RequiredFileSample), request.SampleId);

        // A row with no file behind it is a broken deployment, not a 500: say it is missing.
        if (!await _storage.ExistsAsync(sample.StoragePath, cancellationToken))
        {
            throw new NotFoundException(nameof(RequiredFileSample), request.SampleId);
        }

        return new RequiredFileSampleDownload(
            await _storage.OpenReadAsync(sample.StoragePath, cancellationToken),
            sample.ContentType,
            sample.FileName);
    }
}
