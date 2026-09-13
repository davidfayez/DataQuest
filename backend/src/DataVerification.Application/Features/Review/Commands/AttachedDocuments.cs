using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Review.Commands;

// ------------------------------------------------------- Attached documents
//
// What an administrator sends when they attach documents to an application. The shape deliberately
// mirrors the service type's required-file editor — a named document carrying files plus custom
// fields — so the reviewer's panel and the lookups screen speak the same language. The difference
// is that here the definition and the answer arrive together: the reviewer names the field and
// fills it in the same breath.

/// <summary>Limits on one attach operation. Enforced here and by the endpoint's body-size caps.</summary>
public static class AttachedDocumentLimits
{
    /// <summary>Documents one status change may carry.</summary>
    public const int MaxDocuments = 10;

    /// <summary>Files across the whole request, each still capped at the platform maximum.</summary>
    public const int MaxFiles = 10;

    /// <summary>Custom fields on a single document.</summary>
    public const int MaxFields = 20;

    /// <summary>
    /// The largest body the attach endpoint accepts: every file at the platform maximum, plus a
    /// megabyte of multipart headers and form fields. Both Kestrel and the form reader are capped
    /// globally at one file's worth, so the endpoint has to raise its own ceiling explicitly.
    /// </summary>
    public const long MaxRequestBodyBytes = (MaxFiles * ApplicationFile.MaxFileSizeBytes) + (1024 * 1024);
}

/// <summary>A document an administrator is attaching, with its files and recorded details.</summary>
public sealed record AttachedDocumentInput(
    string NameAr,
    string NameEn,
    bool IsVisibleToApplicant,
    IReadOnlyList<AttachedFileInput> Files,
    IReadOnlyList<AttachedDocumentFieldInput> Fields);

/// <summary>One uploaded file. The stream is read once, during persistence.</summary>
public sealed record AttachedFileInput(string FileName, long SizeBytes, Stream Content);

/// <summary>
/// A custom field the administrator defined and answered at the same time. The rule properties are
/// the same set <see cref="RequiredFileFieldInput"/> carries, because the value is judged by
/// exactly the same validator.
/// </summary>
public sealed record AttachedDocumentFieldInput(
    string NameAr,
    string NameEn,
    RequiredFieldType FieldType,
    bool IsRequired,
    int SortOrder,
    string? Value,
    int? MinLength,
    int? MaxLength,
    string? Pattern,
    decimal? MinValue,
    decimal? MaxValue,
    RequiredFieldDateRule DateRule,
    DateOnly? MinDate,
    DateOnly? MaxDate,
    IReadOnlyList<AttachedDocumentFieldOptionInput> Options);

public sealed record AttachedDocumentFieldOptionInput(string Value, string LabelAr, string LabelEn);

/// <summary>
/// The structural rules for attached documents, shared by every command that accepts them. Applied
/// with <c>RuleForEach(...).SetValidator(...)</c> rather than inline child rules so the set stays
/// in one place.
/// </summary>
public sealed class AttachedDocumentInputValidator : AbstractValidator<AttachedDocumentInput>
{
    public AttachedDocumentInputValidator()
    {
        RuleFor(d => d.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(d => d.NameEn).NotEmpty().MaximumLength(200);

        // A document with no file is a detail sheet with nothing to detail. Rejected here so the
        // reviewer is told plainly instead of finding an empty row on the application later.
        RuleFor(d => d.Files)
            .NotEmpty()
            .WithMessage("Attach at least one file to every document.");

        RuleFor(d => d.Fields)
            .Must(fields => fields is null || fields.Count <= AttachedDocumentLimits.MaxFields)
            .WithMessage($"A document may carry at most {AttachedDocumentLimits.MaxFields} fields.");

        RuleForEach(d => d.Fields).ChildRules(field =>
        {
            field.RuleFor(x => x.NameAr).NotEmpty().MaximumLength(200);
            field.RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
            field.RuleFor(x => x.Pattern).MaximumLength(400);
            field.RuleFor(x => x.Value).MaximumLength(1000);

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
    }
}

/// <summary>
/// Attaches documents without moving the application. The review panel uses this when the current
/// status offers no transition — a completed application still accumulates paperwork, and there is
/// no status to change in order to file it.
/// </summary>
public sealed record AttachApplicationDocumentsCommand(
    Guid ApplicationId,
    IReadOnlyList<AttachedDocumentInput> Documents) : IRequest<AttachDocumentsResultDto>;

public sealed record AttachDocumentsResultDto(Guid ApplicationId, int DocumentCount, int FileCount);

public sealed class AttachApplicationDocumentsCommandValidator
    : AbstractValidator<AttachApplicationDocumentsCommand>
{
    public AttachApplicationDocumentsCommandValidator()
    {
        RuleFor(c => c.ApplicationId).NotEmpty();
        RuleFor(c => c.Documents).NotEmpty().WithMessage("Attach at least one document.");
        RuleForEach(c => c.Documents).SetValidator(new AttachedDocumentInputValidator());
    }
}

public sealed class AttachApplicationDocumentsCommandHandler
    : IRequestHandler<AttachApplicationDocumentsCommand, AttachDocumentsResultDto>
{
    private readonly IApplicationDbContext _db;
    private readonly AttachedDocumentWriter _writer;
    private readonly IAuditLogger _auditLogger;

    public AttachApplicationDocumentsCommandHandler(
        IApplicationDbContext db,
        AttachedDocumentWriter writer,
        IAuditLogger auditLogger)
    {
        _db = db;
        _writer = writer;
        _auditLogger = auditLogger;
    }

    public async Task<AttachDocumentsResultDto> Handle(
        AttachApplicationDocumentsCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var application = await _db.Applications
            .Include(a => a.Documents)
            .FirstOrDefaultAsync(a => a.Id == request.ApplicationId, cancellationToken)
            ?? throw new NotFoundException("Application", request.ApplicationId);

        // The current status is recorded rather than a transition: nothing moved, but the trail
        // should still say where the application stood when the paperwork arrived.
        var documents = await _writer.StageAsync(
            application,
            request.Documents,
            application.Status,
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            "Application.DocumentsAttached",
            "Application",
            application.Id,
            new
            {
                application.ApplicationNumber,
                Status = application.Status,
                Documents = documents.Select(d => new
                {
                    d.NameEn,
                    Files = d.Files.Count,
                    Fields = d.Fields.Count,
                    d.IsVisibleToApplicant,
                }).ToList(),
            },
            cancellationToken);

        return new AttachDocumentsResultDto(
            application.Id,
            documents.Count,
            documents.Sum(d => d.Files.Count));
    }
}

/// <summary>
/// Turns <see cref="AttachedDocumentInput"/> into persisted documents: every file sniffed and
/// stored, every recorded detail checked against the rules the administrator set for it.
///
/// Nothing is written to the database here — entities are added to the change tracker and the
/// caller saves, so a document can never be committed without the status change that carried it.
/// Stored bytes are the exception: they are written before the save, so a failure afterwards can
/// leave an orphaned blob. That is the same trade the applicant's upload path makes, and the
/// alternative — holding every file in memory until commit — is worse for a 50 MB request.
/// </summary>
public sealed class AttachedDocumentWriter
{
    private readonly IApplicationDbContext _db;
    private readonly IFileStorage _storage;
    private readonly IFileTypeValidator _typeValidator;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public AttachedDocumentWriter(
        IApplicationDbContext db,
        IFileStorage storage,
        IFileTypeValidator typeValidator,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _db = db;
        _storage = storage;
        _typeValidator = typeValidator;
        _currentUser = currentUser;
        _clock = clock;
    }

    /// <summary>
    /// Stores each document's files and stages the rows. <paramref name="attachedAtStatus"/> is the
    /// status the application is moving to, recorded so the trail shows which decision a document
    /// supports; null when the documents arrive without a transition.
    /// </summary>
    public async Task<IReadOnlyList<ApplicationDocument>> StageAsync(
        VerificationApplication application,
        IReadOnlyList<AttachedDocumentInput> documents,
        ApplicationStatus? attachedAtStatus,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(documents);

        if (documents.Count > AttachedDocumentLimits.MaxDocuments)
        {
            throw new ConflictException(
                "document.too_many",
                $"Attach at most {AttachedDocumentLimits.MaxDocuments} documents at a time.");
        }

        var totalFiles = documents.Sum(d => d.Files.Count);
        if (totalFiles > AttachedDocumentLimits.MaxFiles)
        {
            throw new ConflictException(
                "document.too_many_files",
                $"Attach at most {AttachedDocumentLimits.MaxFiles} files at a time.");
        }

        var actor = _currentUser.ToActor();
        var today = DateOnly.FromDateTime(_clock.UtcNow);
        var staged = new List<ApplicationDocument>(documents.Count);

        // Continues the existing numbering so a second attach does not reorder the first.
        var nextSortOrder = application.Documents.Count == 0
            ? 0
            : application.Documents.Max(d => d.SortOrder) + 1;

        for (var index = 0; index < documents.Count; index++)
        {
            var input = documents[index];

            var document = new ApplicationDocument
            {
                ApplicationId = application.Id,
                NameAr = input.NameAr.Trim(),
                NameEn = input.NameEn.Trim(),
                IsVisibleToApplicant = input.IsVisibleToApplicant,
                AttachedAtStatus = attachedAtStatus,
                UploadedByType = actor.Type,
                UploadedById = actor.Id,
                UploadedByName = actor.DisplayName,
                SortOrder = nextSortOrder + index,
            };

            AddFields(document, input);
            await AddFilesAsync(application, document, input, cancellationToken);

            _db.ApplicationDocuments.Add(document);
            staged.Add(document);
        }

        // Validated after the whole set is built so the reviewer is told about every bad field at
        // once, rather than fixing them one round-trip at a time.
        ThrowIfAnyFieldInvalid(staged, today);

        return staged;
    }

    private static void AddFields(ApplicationDocument document, AttachedDocumentInput input)
    {
        var sortOrder = 0;

        foreach (var field in input.Fields.OrderBy(f => f.SortOrder))
        {
            var entity = new ApplicationDocumentField
            {
                NameAr = field.NameAr.Trim(),
                NameEn = field.NameEn.Trim(),
                FieldType = field.FieldType,
                IsRequired = field.IsRequired,
                SortOrder = sortOrder++,
                Value = string.IsNullOrWhiteSpace(field.Value) ? null : field.Value.Trim(),
                MinLength = field.MinLength,
                MaxLength = field.MaxLength,
                Pattern = string.IsNullOrWhiteSpace(field.Pattern) ? null : field.Pattern,
                MinValue = field.MinValue,
                MaxValue = field.MaxValue,
                DateRule = field.DateRule,
                MinDate = field.MinDate,
                MaxDate = field.MaxDate,
            };

            // Only a dropdown has anything to choose from; options sent for another type are the
            // remains of a reviewer switching the type mid-edit and are dropped rather than stored.
            if (field.FieldType == RequiredFieldType.Dropdown)
            {
                var optionOrder = 0;

                foreach (var option in field.Options)
                {
                    entity.Options.Add(new ApplicationDocumentFieldOption
                    {
                        Value = option.Value.Trim(),
                        LabelAr = option.LabelAr.Trim(),
                        LabelEn = option.LabelEn.Trim(),
                        SortOrder = optionOrder++,
                    });
                }
            }

            document.Fields.Add(entity);
        }
    }

    private async Task AddFilesAsync(
        VerificationApplication application,
        ApplicationDocument document,
        AttachedDocumentInput input,
        CancellationToken cancellationToken)
    {
        var actor = _currentUser.ToActor();

        foreach (var file in input.Files)
        {
            if (file.SizeBytes <= 0 || file.SizeBytes > ApplicationFile.MaxFileSizeBytes)
            {
                throw new ConflictException(
                    "file.too_large",
                    $"Files must be between 1 byte and {ApplicationFile.MaxFileSizeBytes / (1024 * 1024)} MB.");
            }

            // Extension and magic bytes must agree — an admin upload is sniffed exactly as an
            // applicant's is, because a permission is not a reason to trust a file.
            var contentType = await _typeValidator.DetectAllowedContentTypeAsync(
                file.Content,
                file.FileName,
                cancellationToken)
                ?? throw new ConflictException(
                    "file.unsupported_type",
                    "Only PDF, JPG, JPEG and PNG files are accepted.");

            var storagePath = await _storage.SaveAsync(
                file.Content,
                $"orders/{application.OrderId}/applications/{application.Id}/documents",
                file.FileName,
                application.Id.ToString("N"),
                cancellationToken);

            document.Files.Add(new ApplicationFile
            {
                ApplicationId = application.Id,
                FileName = Path.GetFileName(file.FileName),
                StoragePath = storagePath,
                ContentType = contentType,
                SizeBytes = file.SizeBytes,
                Kind = ApplicationFileKind.AdminDocument,
                UploadedByType = actor.Type,
                UploadedById = actor.Id,
                UploadedByName = actor.DisplayName,
            });
        }
    }

    /// <summary>
    /// Holds the administrator to the rules they just wrote. A required field left blank, a date
    /// outside its own window and a value off a dropdown are all refused here.
    /// </summary>
    private static void ThrowIfAnyFieldInvalid(IReadOnlyList<ApplicationDocument> documents, DateOnly today)
    {
        var failures = documents
            .SelectMany(document => document.Fields.Select(field => (document, field)))
            .Select(pair => (pair.document, pair.field, Code: pair.field.Validate(today)))
            .Where(result => result.Code is not null)
            .ToList();

        if (failures.Count == 0) return;

        var detail = string.Join(
            "; ",
            failures.Select(f => $"{f.document.NameEn} → {f.field.NameEn}: {f.Code}"));

        throw new ConflictException("document.field_invalid", $"Check the document details: {detail}.");
    }
}
