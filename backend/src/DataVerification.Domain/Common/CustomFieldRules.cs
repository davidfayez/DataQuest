using System.Globalization;
using System.Text.RegularExpressions;
using DataVerification.Domain.Enums;

namespace DataVerification.Domain.Common;

/// <summary>
/// The rules an administrator configured for one custom field, lifted out of whichever entity
/// stores them. Two entities carry the same rule set — the field an applicant fills in beside a
/// required document, and the field an administrator records beside a document they attach during
/// review — and both must accept and reject exactly the same values.
/// </summary>
public readonly record struct FieldRuleSet
{
    public RequiredFieldType FieldType { get; init; }

    public bool IsRequired { get; init; }

    // --- Text -----------------------------------------------------------
    public int? MinLength { get; init; }

    public int? MaxLength { get; init; }

    /// <summary>Optional .NET regular expression the whole value must match.</summary>
    public string? Pattern { get; init; }

    // --- Number ---------------------------------------------------------
    public decimal? MinValue { get; init; }

    public decimal? MaxValue { get; init; }

    // --- Date -----------------------------------------------------------
    public RequiredFieldDateRule DateRule { get; init; }

    public DateOnly? MinDate { get; init; }

    public DateOnly? MaxDate { get; init; }
}

/// <summary>
/// Checks a submitted value against a <see cref="FieldRuleSet"/> and returns a machine-readable
/// error code, or null when the value is acceptable. Validation lives here so every caller
/// enforces exactly the rules the administrator configured, wherever the field is stored.
/// </summary>
public static class CustomFieldRules
{
    public static string? Validate(
        in FieldRuleSet rules,
        string? value,
        DateOnly today,
        IReadOnlyCollection<string> optionValues)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return rules.IsRequired ? "field.required" : null;
        }

        return rules.FieldType switch
        {
            RequiredFieldType.Text => ValidateText(rules, trimmed),
            RequiredFieldType.Number => ValidateNumber(rules, trimmed),
            RequiredFieldType.Date => ValidateDate(rules, trimmed, today),
            RequiredFieldType.Dropdown => ValidateDropdown(trimmed, optionValues),
            _ => null,
        };
    }

    private static string? ValidateText(in FieldRuleSet rules, string value)
    {
        if (rules.MinLength is { } min && value.Length < min) return "field.too_short";
        if (rules.MaxLength is { } max && value.Length > max) return "field.too_long";

        if (!string.IsNullOrWhiteSpace(rules.Pattern))
        {
            // A malformed pattern is an administrator's mistake, not the applicant's — never let it
            // surface as a rejected value, and never let a pathological one hang the request.
            try
            {
                if (!Regex.IsMatch(value, rules.Pattern, RegexOptions.None, TimeSpan.FromMilliseconds(200)))
                {
                    return "field.pattern_mismatch";
                }
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (RegexMatchTimeoutException)
            {
                return null;
            }
        }

        return null;
    }

    private static string? ValidateNumber(in FieldRuleSet rules, string value)
    {
        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
        {
            return "field.not_a_number";
        }

        if (rules.MinValue is { } min && number < min) return "field.below_minimum";
        if (rules.MaxValue is { } max && number > max) return "field.above_maximum";
        return null;
    }

    private static string? ValidateDate(in FieldRuleSet rules, string value, DateOnly today)
    {
        if (!DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return "field.not_a_date";
        }

        if (rules.DateRule == RequiredFieldDateRule.PastOnly && date >= today) return "field.must_be_past";
        if (rules.DateRule == RequiredFieldDateRule.FutureOnly && date <= today) return "field.must_be_future";
        if (rules.MinDate is { } min && date < min) return "field.before_earliest";
        if (rules.MaxDate is { } max && date > max) return "field.after_latest";
        return null;
    }

    private static string? ValidateDropdown(string value, IReadOnlyCollection<string> optionValues) =>
        optionValues.Any(option => string.Equals(option, value, StringComparison.OrdinalIgnoreCase))
            ? null
            : "field.not_an_option";
}
