using System.Text.RegularExpressions;

namespace DataVerification.Domain.Common;

/// <summary>A lookup that carries a unique code alongside its names.</summary>
public interface ICodedLookup
{
    string Code { get; set; }
}

/// <summary>
/// The rule every lookup code follows: letters and digits, with hyphens or underscores between,
/// 2 to 30 characters, stored upper-case so one code has exactly one spelling.
/// </summary>
public static partial class LookupCode
{
    public const int MaxLength = 30;

    public static string Normalize(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    public static bool IsValid(string? code) => Shape().IsMatch(Normalize(code));

    [GeneratedRegex("^[A-Z0-9][A-Z0-9_-]{1,29}$")]
    private static partial Regex Shape();
}
