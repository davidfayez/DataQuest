using DataVerification.Domain.Common;
using DataVerification.Domain.Enums;

namespace DataVerification.Domain.Entities;

/// <summary>
/// One piece of information an administrator asks for alongside a required document — "issue date",
/// "issue number" and so on. The applicant fills it in once per document slot, whatever number of
/// files that slot accepts.
/// </summary>
public class RequiredFileField : LocalizedLookup
{
    public Guid RequiredFileId { get; set; }

    public ServiceTypeRequiredFile? RequiredFile { get; set; }

    public RequiredFieldType FieldType { get; set; }

    public bool IsRequired { get; set; }

    /// <summary>Display order within the document, lowest first.</summary>
    public int SortOrder { get; set; }

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
    public ICollection<RequiredFileFieldOption> Options { get; set; } = [];

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
    /// Checks a submitted value against this field's rules and returns a machine-readable error
    /// code, or null when the value is acceptable. The rules themselves live in
    /// <see cref="CustomFieldRules"/> so an administrator's document fields are judged identically.
    /// </summary>
    public string? Validate(string? value, DateOnly today) =>
        CustomFieldRules.Validate(
            ToRuleSet(),
            value,
            today,
            Options.Select(option => option.Value).ToList());
}

/// <summary>One selectable choice on a dropdown field.</summary>
public class RequiredFileFieldOption : Entity
{
    public Guid RequiredFileFieldId { get; set; }

    public RequiredFileField? Field { get; set; }

    /// <summary>The value stored on the application — stable across label translations.</summary>
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

/// <summary>
/// What an applicant entered for one custom field, on one document slot of one purchased service
/// line. Values are held as text and interpreted by the field's own type.
/// </summary>
public class ApplicationDocumentValue : Entity
{
    public Guid ApplicationId { get; set; }

    public VerificationApplication? Application { get; set; }

    /// <summary>The purchased service line the document belongs to.</summary>
    public Guid ApplicationServiceId { get; set; }

    public ApplicationService? ApplicationService { get; set; }

    public Guid RequiredFileFieldId { get; set; }

    public RequiredFileField? Field { get; set; }

    public required string Value { get; set; }
}
