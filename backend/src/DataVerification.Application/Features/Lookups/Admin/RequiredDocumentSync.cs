using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Common;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Lookups.Admin;

/// <summary>What a required document belongs to.</summary>
public enum RequiredDocumentOwner
{
    /// <summary>Uploaded in the application wizard, per service line.</summary>
    ServiceType = 0,

    /// <summary>Uploaded with a deposit request through the method.</summary>
    PaymentMethod = 1,
}

/// <summary>
/// One required document as an admin editor submits it. Shared by service types and payment
/// methods, so the two editors can never disagree about what a valid document is.
/// </summary>
public sealed class RequiredFileInputValidator : AbstractValidator<RequiredFileInput>
{
    public RequiredFileInputValidator()
    {
        RuleFor(f => f.MaxFiles).InclusiveBetween(1, 20);

        // A document that accepts nothing could never be satisfied, so an empty set means the
        // default rather than "refuse everything" — but a set of codes we do not recognise is a
        // mistake worth reporting rather than quietly ignoring.
        RuleFor(f => f.AllowedFileTypes)
            .Must(codes => codes is null || codes.All(DocumentFileTypes.IsSupported))
            .WithMessage(
                $"Document formats must be drawn from: {string.Join(", ", DocumentFileTypes.All)}.");
        RuleFor(f => f.MaxSizeBytes)
            .GreaterThan(0).When(f => f.MaxSizeBytes.HasValue)
            .WithMessage("A document maximum size must be greater than zero.");

        RuleForEach(f => f.Fields).ChildRules(field =>
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

        RuleFor(f => f.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(f => f.NameEn).NotEmpty().MaximumLength(200);
    }
}

/// <summary>
/// Reconciles an owner's required documents with what its editor submitted — the same rules for a
/// service type and for a payment method.
/// </summary>
internal sealed class RequiredDocumentSync
{
    private readonly IApplicationDbContext _db;

    public RequiredDocumentSync(IApplicationDbContext db) => _db = db;

    /// <summary>
    /// Edits the documents in place by id, adds the new ones and removes the dropped ones. A dropped
    /// document that something was already uploaded against is kept, switched off, so what was
    /// submitted keeps its meaning.
    /// </summary>
    /// <param name="create">Makes a new document already pointing at its owner.</param>
    /// <returns>
    /// The storage paths of the reference files that belonged to removed documents. Their rows go
    /// with the document; the caller deletes the bytes once the save has succeeded.
    /// </returns>
    public async Task<IReadOnlyList<string>> SyncAsync(
        ICollection<ServiceTypeRequiredFile> owned,
        Func<RequiredFileInput, ServiceTypeRequiredFile> create,
        IReadOnlyList<RequiredFileInput> requested,
        CancellationToken cancellationToken)
    {
        var keptIds = requested.Where(f => f.Id.HasValue).Select(f => f.Id!.Value).ToHashSet();
        var discardedFiles = new List<string>();

        foreach (var existing in owned.ToList())
        {
            if (keptIds.Contains(existing.Id))
            {
                continue;
            }

            if (await IsReferencedAsync(existing.Id, cancellationToken))
            {
                // Kept for the uploads that point at it, and its reference files with it.
                existing.IsMandatory = false;
                existing.IsActive = false;
                continue;
            }

            discardedFiles.AddRange(existing.Samples.Select(sample => sample.StoragePath));
            _db.ServiceTypeRequiredFiles.Remove(existing);
            owned.Remove(existing);
        }

        foreach (var input in requested)
        {
            var target = input.Id.HasValue
                ? owned.FirstOrDefault(f => f.Id == input.Id.Value)
                : null;

            if (target is null)
            {
                target = create(input);
                owned.Add(target);
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

        return discardedFiles;
    }

    /// <summary>Anything already submitted against the document, in either realm.</summary>
    private async Task<bool> IsReferencedAsync(Guid documentId, CancellationToken cancellationToken) =>
        await _db.ApplicationFiles.AnyAsync(f => f.RequiredFileId == documentId, cancellationToken)
        || await _db.WalletRequestFiles.AnyAsync(f => f.RequiredFileId == documentId, cancellationToken)
        || await _db.WalletRequestDocumentValues.AnyAsync(v => v.RequiredFileId == documentId, cancellationToken);

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

    /// <summary>Replaces a document's custom fields with exactly the requested set.</summary>
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
