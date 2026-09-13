using DataVerification.Domain.Common;
using DataVerification.Domain.Enums;

namespace DataVerification.Domain.Entities;

/// <summary>
/// A named set of files an administrator attached to an application while reviewing it, together
/// with the details they recorded beside it.
///
/// This is the review-side counterpart of <see cref="ServiceTypeRequiredFile"/>: there an
/// administrator defines what the applicant must provide, here they attach what the authority
/// returned. The field builder is the same one, so a reviewer describes a document with the
/// vocabulary they already know.
/// </summary>
public class ApplicationDocument : LocalizedLookup
{
    public Guid ApplicationId { get; set; }

    public VerificationApplication? Application { get; set; }

    /// <summary>
    /// The status the application was moved to in the same request, or null when the document was
    /// attached without a transition. Kept so the trail shows which decision a document supports.
    /// </summary>
    public ApplicationStatus? AttachedAtStatus { get; set; }

    /// <summary>
    /// Whether the applicant may see this document and its details. False keeps it to the review
    /// team, exactly as an internal comment does — and, like internal comments, it is enforced in
    /// the query layer rather than by hiding it in the UI.
    /// </summary>
    public bool IsVisibleToApplicant { get; set; } = true;

    public ActorType UploadedByType { get; set; }

    public Guid? UploadedById { get; set; }

    public string? UploadedByName { get; set; }

    /// <summary>Display order within the application, lowest first.</summary>
    public int SortOrder { get; set; }

    /// <summary>The details recorded beside the upload, in display order.</summary>
    public ICollection<ApplicationDocumentField> Fields { get; set; } = [];

    /// <summary>The uploaded files themselves. A document may carry several.</summary>
    public ICollection<ApplicationFile> Files { get; set; } = [];
}

/// <summary>
/// One detail an administrator recorded beside an attached document — "issue date", "reference
/// number" and so on. Unlike <see cref="RequiredFileField"/>, the definition and the value are
/// captured in the same breath, so both live on the row.
/// </summary>
public class ApplicationDocumentField : LocalizedLookup
{
    public Guid ApplicationDocumentId { get; set; }

    public ApplicationDocument? Document { get; set; }

    public RequiredFieldType FieldType { get; set; }

    public bool IsRequired { get; set; }

    /// <summary>Display order within the document, lowest first.</summary>
    public int SortOrder { get; set; }

    /// <summary>What the administrator entered. Held as text and read through the field's type.</summary>
    public string? Value { get; set; }

    // --- Text -----------------------------------------------------------
    public int? MinLength { get; set; }

    public int? MaxLength { get; set; }

    /// <summary>Optional .NET regular expression the whole value must match.</summary>
    public string? Pattern { get; set; }

    // --- Number ---------------------------------------------------------
    public decimal? MinValue { get; set; }

    public decimal? MaxValue { get; set; }

    // --- Date -----------------------------------------------------------
    public RequiredFieldDateRule DateRule { get; set; }

    public DateOnly? MinDate { get; set; }

    public DateOnly? MaxDate { get; set; }

    // --- Dropdown -------------------------------------------------------
    public ICollection<ApplicationDocumentFieldOption> Options { get; set; } = [];

    /// <summary>The configured rules, in the shared shape every custom field is validated against.</summary>
    public FieldRuleSet ToRuleSet() => new()
    {
        FieldType = FieldType,
        IsRequired = IsRequired,
        MinLength = MinLength,
        MaxLength = MaxLength,
        Pattern = Pattern,
        MinValue = MinValue,
        MaxValue = MaxValue,
        DateRule = DateRule,
        MinDate = MinDate,
        MaxDate = MaxDate,
    };

    /// <summary>
    /// Checks the recorded value against this field's own rules, returning a machine-readable
    /// error code or null. An administrator who sets a rule is held to it too.
    /// </summary>
    public string? Validate(DateOnly today) =>
        CustomFieldRules.Validate(
            ToRuleSet(),
            Value,
            today,
            Options.Select(option => option.Value).ToList());
}

/// <summary>One selectable choice on an administrator's dropdown field.</summary>
public class ApplicationDocumentFieldOption : Entity
{
    public Guid ApplicationDocumentFieldId { get; set; }

    public ApplicationDocumentField? Field { get; set; }

    /// <summary>The value stored on the field — stable across label translations.</summary>
    public required string Value { get; set; }

    public required string LabelAr { get; set; }

    public required string LabelEn { get; set; }

    public int SortOrder { get; set; }

    public string ResolveLabel(string? languageCode)
    {
        var isArabic = languageCode?.StartsWith("ar", StringComparison.OrdinalIgnoreCase) == true;
        var preferred = isArabic ? LabelAr : LabelEn;
        return string.IsNullOrWhiteSpace(preferred) ? (isArabic ? LabelEn : LabelAr) : preferred;
    }
}
