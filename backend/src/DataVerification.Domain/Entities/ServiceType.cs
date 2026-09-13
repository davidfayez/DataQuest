using DataVerification.Domain.Common;

namespace DataVerification.Domain.Entities;

/// <summary>
/// نوع الخدمة — depends on a verification authority and a sub-transaction type, and owns the
/// commercial terms: per-currency costs, whether express delivery may be requested, the
/// turnaround time and the list of files the applicant must upload.
/// </summary>
public class ServiceType : LocalizedLookup, ICodedLookup
{
    /// <summary>Unique within its kind and stored upper-case — see <see cref="LookupCode"/>.</summary>
    public string Code { get; set; } = string.Empty;

    public Guid VerificationAuthorityId { get; set; }

    public VerificationAuthority? VerificationAuthority { get; set; }

    public Guid SubTransactionTypeId { get; set; }

    public SubTransactionType? SubTransactionType { get; set; }

    public string? DescriptionAr { get; set; }

    public string? DescriptionEn { get; set; }

    public int ExecutionTimeDays { get; set; }

    /// <summary>
    /// Legacy/default cost kept in sync with the first configured currency price for landing and
    /// admin list display. Authoritative pricing always uses <see cref="Costs"/>.
    /// </summary>
    public decimal Cost { get; set; }

    public bool EnableExpress { get; set; }

    /// <summary>
    /// The note shown beside the wizard's express toggle, authored per service by an administrator.
    /// A <c>{{cost}}</c> placeholder is substituted with the express surcharge in the order's
    /// currency. Left blank, the wizard falls back to its own generic sentence.
    /// </summary>
    public string? ExpressNoteAr { get; set; }

    public string? ExpressNoteEn { get; set; }

    /// <summary>Legacy/default express surcharge; see <see cref="Cost"/>.</summary>
    public decimal ExpressCost { get; set; }

    /// <summary>Whether this service is advertised in the public landing page "tracks" section.</summary>
    public bool ShowOnLanding { get; set; } = true;

    public ICollection<ServiceTypeCost> Costs { get; set; } = [];

    public ICollection<ServiceTypeRequiredFile> RequiredFiles { get; set; } = [];

    /// <summary>
    /// The languages this service's result can be issued in, chosen by an administrator. The
    /// wizard offers exactly these in its "output language" list.
    /// </summary>
    public ICollection<ServiceTypeLanguage> OutputLanguages { get; set; } = [];

    /// <summary>
    /// The configured output languages in platform order. Falls back to the full set when an
    /// administrator has not narrowed them, so the wizard never shows an empty list.
    /// </summary>
    public IReadOnlyList<string> ResolveOutputLanguages()
    {
        var configured = PlatformLanguages.Normalize(OutputLanguages.Select(l => l.LanguageCode));
        return configured.Count > 0 ? configured : PlatformLanguages.All;
    }

    /// <summary>True when the service may be issued in the given language.</summary>
    public bool OffersLanguage(string? languageCode) =>
        languageCode is not null
        && ResolveOutputLanguages().Contains(languageCode, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Picks the description for a language tag, mirroring <see cref="LocalizedLookup.ResolveName"/>:
    /// Arabic for <c>ar</c>, English otherwise, each falling back to the other when blank.
    /// </summary>
    public string? ResolveDescription(string? languageCode)
    {
        var isArabic = languageCode?.StartsWith("ar", StringComparison.OrdinalIgnoreCase) == true;
        var preferred = isArabic ? DescriptionAr : DescriptionEn;
        return string.IsNullOrWhiteSpace(preferred) ? (isArabic ? DescriptionEn : DescriptionAr) : preferred;
    }

    /// <summary>
    /// Picks the express note for a language tag, mirroring <see cref="ResolveDescription"/>.
    /// Returns <c>null</c> when neither translation is set, which is the wizard's signal to use
    /// its own default wording.
    /// </summary>
    public string? ResolveExpressNote(string? languageCode)
    {
        var isArabic = languageCode?.StartsWith("ar", StringComparison.OrdinalIgnoreCase) == true;
        var preferred = isArabic ? ExpressNoteAr : ExpressNoteEn;
        var resolved = string.IsNullOrWhiteSpace(preferred)
            ? (isArabic ? ExpressNoteEn : ExpressNoteAr)
            : preferred;

        return string.IsNullOrWhiteSpace(resolved) ? null : resolved;
    }

    /// <summary>Finds the price row for a currency, or <c>null</c> when none is configured.</summary>
    public ServiceTypeCost? FindCost(Guid currencyId) =>
        Costs.FirstOrDefault(cost => cost.CurrencyId == currencyId);

    /// <summary>
    /// Authoritative price for a line. Costs are always recomputed here from the stored
    /// configuration — client-submitted totals are never trusted.
    /// </summary>
    public decimal CalculateLineTotal(int quantity, bool isExpress, decimal unitCost, decimal expressCost)
    {
        if (quantity <= 0)
        {
            throw new DomainException("service.invalid_quantity", "Quantity must be greater than zero.");
        }

        if (isExpress && !EnableExpress)
        {
            throw new DomainException(
                "service.express_not_available",
                $"Service type '{NameEn}' does not offer express delivery.");
        }

        var unit = unitCost + (isExpress ? expressCost : 0m);
        return unit * quantity;
    }
}

/// <summary>One language a service type's result may be issued in.</summary>
public class ServiceTypeLanguage : Entity
{
    public Guid ServiceTypeId { get; set; }

    public ServiceType? ServiceType { get; set; }

    /// <summary>A platform locale code, stored lower-cased — see <see cref="PlatformLanguages"/>.</summary>
    public required string LanguageCode { get; set; }
}

/// <summary>Per-currency price for a service type.</summary>
public class ServiceTypeCost : Entity
{
    public Guid ServiceTypeId { get; set; }

    public ServiceType? ServiceType { get; set; }

    public Guid CurrencyId { get; set; }

    public Currency? Currency { get; set; }

    public decimal Cost { get; set; }

    public decimal ExpressCost { get; set; }
}

/// <summary>One file the applicant must (or may) attach for a given service type.</summary>
public class ServiceTypeRequiredFile : LocalizedLookup
{
    public Guid ServiceTypeId { get; set; }

    public ServiceType? ServiceType { get; set; }

    public bool IsMandatory { get; set; } = true;

    /// <summary>
    /// Per-document upload cap in bytes. Null falls back to the platform maximum, and a value
    /// above it is ignored — this narrows the limit, it cannot widen it.
    /// </summary>
    public long? MaxSizeBytes { get; set; }

    /// <summary>How many files the applicant may attach to this document. At least one.</summary>
    public int MaxFiles { get; set; } = 1;

    /// <summary>Extra information the applicant fills in beside the upload, in display order.</summary>
    public ICollection<RequiredFileField> Fields { get; set; } = [];

    /// <summary>
    /// The upload formats this document accepts, chosen by an administrator. Empty means the
    /// platform default — see <see cref="ResolveAllowedFileTypes"/>.
    /// </summary>
    public ICollection<RequiredFileAllowedType> AllowedFileTypes { get; set; } = [];

    /// <summary>The effective size cap, never exceeding the platform maximum.</summary>
    public long ResolveMaxSizeBytes(long platformMaximum) =>
        MaxSizeBytes is { } configured && configured > 0 && configured < platformMaximum
            ? configured
            : platformMaximum;

    /// <summary>
    /// The formats to enforce, in platform order. A document configured with none falls back to
    /// the platform default rather than accepting nothing, so a document created before this was
    /// configurable keeps behaving as it did.
    /// </summary>
    public IReadOnlyList<string> ResolveAllowedFileTypes() =>
        DocumentFileTypes.Resolve(AllowedFileTypes.Select(t => t.FileTypeCode));

    /// <summary>True when this document accepts the given format code.</summary>
    public bool Accepts(string? fileTypeCode) =>
        fileTypeCode is not null
        && ResolveAllowedFileTypes().Contains(fileTypeCode, StringComparer.OrdinalIgnoreCase);
}

/// <summary>One upload format a required document accepts.</summary>
public class RequiredFileAllowedType : Entity
{
    public Guid RequiredFileId { get; set; }

    public ServiceTypeRequiredFile? RequiredFile { get; set; }

    /// <summary>A code from <see cref="DocumentFileTypes"/>, stored lower-cased.</summary>
    public required string FileTypeCode { get; set; }
}
