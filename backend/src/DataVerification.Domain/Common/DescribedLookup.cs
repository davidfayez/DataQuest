namespace DataVerification.Domain.Common;

/// <summary>
/// A lookup that also carries a description in both languages — the text that explains what an
/// authority, a transaction type or a sub-type actually covers, beyond its name.
/// </summary>
/// <remarks>
/// Nullable in storage so rows created before descriptions existed stay valid; the admin commands
/// are what require both on every save, so an old row gains its descriptions the next time
/// someone edits it.
/// </remarks>
public abstract class DescribedLookup : LocalizedLookup
{
    public string? DescriptionAr { get; set; }

    public string? DescriptionEn { get; set; }

    /// <summary>
    /// Picks the description for a language tag, mirroring <see cref="LocalizedLookup.ResolveName"/>:
    /// Arabic for <c>ar</c>, English otherwise, each falling back to the other when blank.
    /// </summary>
    public string? ResolveDescription(string? languageCode)
    {
        var isArabic = languageCode?.StartsWith("ar", StringComparison.OrdinalIgnoreCase) == true;
        var preferred = isArabic ? DescriptionAr : DescriptionEn;
        var resolved = string.IsNullOrWhiteSpace(preferred)
            ? (isArabic ? DescriptionEn : DescriptionAr)
            : preferred;

        return string.IsNullOrWhiteSpace(resolved) ? null : resolved;
    }
}
