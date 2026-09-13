namespace DataVerification.Domain.Common;

/// <summary>
/// Base for the admin-managed lookup tables. Every lookup carries an Arabic and an English name;
/// the API returns both plus a <see cref="ResolveName"/> result negotiated from Accept-Language.
/// </summary>
public abstract class LocalizedLookup : Entity
{
    public required string NameAr { get; set; }

    public required string NameEn { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Picks the name for a language tag. Arabic is returned for <c>ar</c>; every other locale
    /// falls back to English, which is the platform's lingua franca for lookup data.
    /// </summary>
    public string ResolveName(string? languageCode) =>
        LocalizedText.Resolve(NameAr, NameEn, languageCode);
}
