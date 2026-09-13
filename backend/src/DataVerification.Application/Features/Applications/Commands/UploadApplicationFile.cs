using DataVerification.Application.Common.Exceptions;
using DataVerification.Domain.Common;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Applications.Commands;

/// <summary>
/// Attaches an applicant upload to an application. The stream must be seekable so the real file
/// type can be sniffed from its leading bytes before anything is written to storage.
/// </summary>
public sealed record UploadApplicationFileCommand(
    Guid ApplicationId,
    Guid? ApplicationServiceId,
    Guid? RequiredFileId,
    string FileName,
    long SizeBytes,
    Stream Content) : IRequest<ApplicationFileDto>;

public sealed class UploadApplicationFileCommandHandler
    : IRequestHandler<UploadApplicationFileCommand, ApplicationFileDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ApplicationWriteService _writeService;
    private readonly IFileStorage _storage;
    private readonly IFileTypeValidator _typeValidator;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _auditLogger;

    public UploadApplicationFileCommandHandler(
        IApplicationDbContext db,
        ApplicationWriteService writeService,
        IFileStorage storage,
        IFileTypeValidator typeValidator,
        ICurrentUser currentUser,
        IAuditLogger auditLogger)
    {
        _db = db;
        _writeService = writeService;
        _storage = storage;
        _typeValidator = typeValidator;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
    }

    public async Task<ApplicationFileDto> Handle(
        UploadApplicationFileCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var orderId = _currentUser.OrderId
            ?? throw new ForbiddenAccessException("This endpoint is only available to applicants.");

        var application = await _writeService.RequireOwnedAsync(
            request.ApplicationId,
            orderId,
            cancellationToken,
            includeChildren: true);

        EnsureUploadsAccepted(application.Status);

        if (request.SizeBytes <= 0)
        {
            throw new ConflictException("file.empty", "The uploaded file is empty.");
        }

        if (request.SizeBytes > ApplicationFile.MaxFileSizeBytes)
        {
            throw new ConflictException(
                "file.too_large",
                $"Files must be {ApplicationFile.MaxFileSizeBytes / (1024 * 1024)} MB or smaller.");
        }

        var limits = await EnsureDocumentLimitsAsync(application, request, cancellationToken);
        var maxFilesForDocument = limits.MaxFiles;

        // Extension alone is not evidence of anything — the signature has to agree with it, and
        // with whatever formats this particular document was configured to accept.
        var contentType = await _typeValidator.DetectAllowedContentTypeAsync(
            request.Content,
            request.FileName,
            limits.AllowedFileTypes,
            cancellationToken)
            ?? throw new ConflictException(
                "file.unsupported_type",
                limits.AllowedFileTypes is { Count: > 0 }
                    ? "This document accepts only "
                      + string.Join(", ", DocumentFileTypes.ExtensionsForAll(limits.AllowedFileTypes))
                      + " files."
                    : "Only PDF, JPG, JPEG and PNG files are accepted.");

        var (serviceId, requiredFileId) = await ResolveTargetAsync(application, request, cancellationToken);

        var storagePath = await _storage.SaveAsync(
            request.Content,
            $"orders/{orderId}/applications/{application.Id}",
            request.FileName,
            serviceId?.ToString("N"),
            cancellationToken);

        var actor = _currentUser.ToActor();

        var file = new ApplicationFile
        {
            ApplicationId = application.Id,
            ApplicationServiceId = serviceId,
            RequiredFileId = requiredFileId,
            FileName = Path.GetFileName(request.FileName),
            StoragePath = storagePath,
            ContentType = contentType,
            SizeBytes = request.SizeBytes,
            Kind = ApplicationFileKind.UserUpload,
            UploadedByType = actor.Type,
            UploadedById = actor.Id,
            UploadedByName = actor.DisplayName,
        };

        // Re-uploading a single-file document replaces the previous attempt rather than piling
        // up, which keeps the checklist meaningful when a reviewer asks for a clearer scan. A
        // document configured for several files instead collects them until it reaches its cap.
        if (requiredFileId.HasValue && serviceId.HasValue && maxFilesForDocument <= 1)
        {
            var superseded = await _db.ApplicationFiles
                .Where(f => f.ApplicationId == application.Id
                            && f.RequiredFileId == requiredFileId
                            && f.ApplicationServiceId == serviceId
                            && f.Kind == ApplicationFileKind.UserUpload)
                .ToListAsync(cancellationToken);

            foreach (var previous in superseded)
            {
                await _storage.DeleteAsync(previous.StoragePath, cancellationToken);
                _db.ApplicationFiles.Remove(previous);
            }
        }

        _db.ApplicationFiles.Add(file);
        await _db.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            "Application.FileUploaded",
            "Application",
            application.Id,
            new { file.FileName, file.ContentType, file.SizeBytes },
            cancellationToken);

        var orderNumber = await _db.Orders
            .AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(o => o.OrderNumber)
            .FirstOrDefaultAsync(cancellationToken);

        return new ApplicationFileDto(
            file.Id,
            file.ApplicationServiceId,
            file.RequiredFileId,
            file.FileName,
            file.ContentType,
            file.SizeBytes,
            file.Kind,
            file.CreatedAtUtc,
            DownloadFileName.Compose(
                application.ApplicationNumber,
                orderNumber,
                limits.Label,
                file.FileName));
    }

    /// <summary>
    /// Uploads are accepted while the application is still being prepared, and again when a
    /// reviewer has asked for more information. At every other status the evidence is frozen.
    /// </summary>
    private static void EnsureUploadsAccepted(ApplicationStatus status)
    {
        var accepted = status is ApplicationStatus.Draft
            or ApplicationStatus.PendingPayment
            or ApplicationStatus.MissedInfo;

        if (!accepted)
        {
            throw new ConflictException(
                "application.uploads_closed",
                $"Files cannot be uploaded while the application is '{status}'.");
        }
    }

    /// <summary>
    /// Validates that the service line and required-file definition belong to this application,
    /// so an upload cannot be filed against another application's requirement.
    /// </summary>
    private async Task<(Guid? ServiceId, Guid? RequiredFileId)> ResolveTargetAsync(
        VerificationApplication application,
        UploadApplicationFileCommand request,
        CancellationToken cancellationToken)
    {
        if (request.ApplicationServiceId is null && request.RequiredFileId is null)
        {
            return (null, null);
        }

        var serviceId = request.ApplicationServiceId
            ?? throw new ConflictException(
                "file.service_line_required",
                "Specify which service line this required file belongs to.");

        var line = application.Services.FirstOrDefault(s => s.Id == serviceId)
            ?? throw new NotFoundException("ApplicationService", serviceId);

        if (request.RequiredFileId is not { } requiredFileId)
        {
            return (serviceId, null);
        }

        var definitionMatches = await _db.ServiceTypeRequiredFiles
            .AsNoTracking()
            .AnyAsync(
                f => f.Id == requiredFileId && f.ServiceTypeId == line.ServiceTypeId,
                cancellationToken);

        if (!definitionMatches)
        {
            throw new ConflictException(
                "file.requirement_mismatch",
                "That required file is not part of the selected service.");
        }

        return (serviceId, requiredFileId);
    }

    /// <summary>
    /// Applies the per-document limits an administrator configured: its own size cap, which may
    /// only narrow the platform maximum, and how many files the document accepts.
    /// </summary>
    /// <summary>
    /// What the targeted document allows: how many files, in which formats, and what it is called
    /// — the label the download is named after.
    /// </summary>
    private sealed record DocumentLimits(
        int MaxFiles,
        IReadOnlyList<string>? AllowedFileTypes,
        string? Label = null);

    private async Task<DocumentLimits> EnsureDocumentLimitsAsync(
        VerificationApplication application,
        UploadApplicationFileCommand request,
        CancellationToken cancellationToken)
    {
        // No document targeted — a loose attachment, which only the platform-wide rules cover.
        if (request.RequiredFileId is not { } requiredFileId)
        {
            return new DocumentLimits(1, null);
        }

        var definition = await _db.ServiceTypeRequiredFiles
            .AsNoTracking()
            .Include(f => f.AllowedFileTypes)
            .FirstOrDefaultAsync(f => f.Id == requiredFileId, cancellationToken);

        if (definition is null)
        {
            return new DocumentLimits(1, null);
        }

        var allowedFileTypes = definition.ResolveAllowedFileTypes();

        var maxSize = definition.ResolveMaxSizeBytes(ApplicationFile.MaxFileSizeBytes);
        if (request.SizeBytes > maxSize)
        {
            throw new ConflictException(
                "file.too_large_for_document",
                $"'{definition.NameEn}' accepts files of {maxSize / 1024} KB or smaller.");
        }

        // A single-file document is always replaceable — that is how a reviewer asking for a
        // clearer scan is answered — so only a multi-file document can actually be "full".
        if (definition.MaxFiles > 1)
        {
            var alreadyUploaded = await _db.ApplicationFiles
                .AsNoTracking()
                .CountAsync(
                    f => f.ApplicationId == application.Id
                         && f.RequiredFileId == requiredFileId
                         && f.ApplicationServiceId == request.ApplicationServiceId
                         && f.Kind == ApplicationFileKind.UserUpload,
                    cancellationToken);

            if (alreadyUploaded >= definition.MaxFiles)
            {
                throw new ConflictException(
                    "file.too_many_for_document",
                    $"'{definition.NameEn}' accepts at most {definition.MaxFiles} file(s).");
            }
        }

        return new DocumentLimits(
            definition.MaxFiles,
            allowedFileTypes,
            definition.ResolveName(_currentUser.LanguageCode));
    }
}
