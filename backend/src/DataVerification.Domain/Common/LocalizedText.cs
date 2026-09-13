namespace DataVerification.Domain.Common;

/// <summary>
/// The platform's one rule for choosing between a stored Arabic and English string.
/// </summary>
/// <remarks>
/// Lives apart from <see cref="LocalizedLookup"/> so a query that projects the two columns —
/// rather than loading the whole entity — can still negotiate the language the same way. Reading a
/// lookup row just to pick one of its names is what makes a page slow.
/// </remarks>
public static class LocalizedText
{
    /// <summary>
    /// Returns the name for a language tag. Arabic is returned for <c>ar</c>; every other locale
    /// gets English, which is the platform's lingua franca for lookup data. Either side falls back
    /// to the other when it is missing, so a half-translated row still reads.
    /// </summary>
    public static string Resolve(string? arabic, string? english, string? languageCode)
    {
        var isArabic = languageCode?.StartsWith("ar", StringComparison.OrdinalIgnoreCase) == true;

        var preferred = isArabic ? arabic : english;
        var fallback = isArabic ? english : arabic;

        return (string.IsNullOrWhiteSpace(preferred) ? fallback : preferred) ?? string.Empty;
    }
}
