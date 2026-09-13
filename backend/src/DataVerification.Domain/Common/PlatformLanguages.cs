namespace DataVerification.Domain.Common;

/// <summary>
/// The locales the platform ships with, mirroring the web app's bundles. Anything that validates a
/// language code — order registration, landing content, a service type's output languages — should
/// measure against this list rather than repeating the codes.
/// </summary>
public static class PlatformLanguages
{
    public const string Default = "en";

    public static readonly IReadOnlyList<string> All =
        ["ar", "en", "ru", "tr", "uz", "de", "hi", "zh", "ja", "pl"];

    public static bool IsSupported(string? code) =>
        code is not null && All.Contains(code, StringComparer.OrdinalIgnoreCase);

    /// <summary>Lower-cases and de-duplicates a submitted set, preserving the platform's order.</summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string>? codes)
    {
        if (codes is null) return [];

        var wanted = codes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return All.Where(wanted.Contains).ToList();
    }
}
