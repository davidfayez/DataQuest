using DataVerification.Domain.Common;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;

namespace DataVerification.Application.Features.Lookups;

/// <summary>
/// The shape every lookup is returned in. Both names are always present so the admin panel can
/// edit them, while <see cref="Name"/> carries the one resolved from Accept-Language for display.
/// </summary>
public record LookupDto(Guid Id, string Name, string NameAr, string NameEn, bool IsActive)
{
    public static LookupDto From(LocalizedLookup lookup, string? languageCode) => new(
        lookup.Id,
        lookup.ResolveName(languageCode),
        lookup.NameAr,
        lookup.NameEn,
        lookup.IsActive);
}

public sealed record CountryDto(
    Guid Id,
    string Code,
    string PhoneCode,
    string Name,
    string NameAr,
    string NameEn,
    bool IsActive,
    IReadOnlyList<string> CurrencyCodes)
{
    /// <param name="currencyCodes">
    /// The country's mapped currency codes, when the caller has loaded them. Empty otherwise, so
    /// callers that do not need the list (the applicant cascade) pay nothing for it.
    /// </param>
    public static CountryDto From(
        Country country,
        string? languageCode,
        IEnumerable<string>? currencyCodes = null) => new(
        country.Id,
        country.Code,
        country.PhoneCode,
        country.ResolveName(languageCode),
        country.NameAr,
        country.NameEn,
        country.IsActive,
        currencyCodes?.OrderBy(code => code).ToList() ?? []);
}

public sealed record CurrencyDto(
    Guid Id,
    string Code,
    string Symbol,
    string Name,
    string NameAr,
    string NameEn,
    bool IsActive,
    IReadOnlyList<Guid> CountryIds)
{
    public static CurrencyDto From(
        Currency currency,
        string? languageCode,
        IEnumerable<Guid>? countryIds = null) => new(
        currency.Id,
        currency.Code,
        currency.Symbol,
        currency.ResolveName(languageCode),
        currency.NameAr,
        currency.NameEn,
        currency.IsActive,
        countryIds?.Distinct().ToList() ?? []);
}

/// <summary>A transaction type together with every country in which it is available.</summary>
public sealed record TransactionTypeDto(
    Guid Id,
    IReadOnlyList<Guid> CountryIds,
    string Name,
    string NameAr,
    string NameEn,
    bool IsActive,
    string? Description = null,
    string? DescriptionAr = null,
    string? DescriptionEn = null,
    string Code = "")
{
    public static TransactionTypeDto From(TransactionType type, string? languageCode) => new(
        type.Id,
        type.CountryLinks.Select(link => link.CountryId).Distinct().ToList(),
        type.ResolveName(languageCode),
        type.NameAr,
        type.NameEn,
        type.IsActive,
        type.ResolveDescription(languageCode),
        type.DescriptionAr,
        type.DescriptionEn,
        type.Code);
}

/// <param name="CountryIds">
/// The countries this sub-type is offered in — always a subset of its parent transaction type's.
/// Empty when the links have not been loaded, so callers that only need the name pay nothing.
/// </param>
public sealed record SubTransactionTypeDto(
    Guid Id,
    Guid TransactionTypeId,
    string Name,
    string NameAr,
    string NameEn,
    bool IsActive,
    IReadOnlyList<Guid> CountryIds,
    string? Description = null,
    string? DescriptionAr = null,
    string? DescriptionEn = null,
    string Code = "")
{
    public static SubTransactionTypeDto From(SubTransactionType type, string? languageCode) => new(
        type.Id,
        type.TransactionTypeId,
        type.ResolveName(languageCode),
        type.NameAr,
        type.NameEn,
        type.IsActive,
        type.CountryLinks.Select(link => link.CountryId).ToList(),
        type.ResolveDescription(languageCode),
        type.DescriptionAr,
        type.DescriptionEn,
        type.Code);
}

public sealed record VerificationAuthorityDto(
    Guid Id,
    Guid CountryId,
    string Name,
    string NameAr,
    string NameEn,
    bool IsActive,
    IReadOnlyList<Guid> SubTransactionTypeIds,
    string? Description = null,
    string? DescriptionAr = null,
    string? DescriptionEn = null,
    string Code = "")
{
    public static VerificationAuthorityDto From(VerificationAuthority authority, string? languageCode) => new(
        authority.Id,
        authority.CountryId,
        authority.ResolveName(languageCode),
        authority.NameAr,
        authority.NameEn,
        authority.IsActive,
        authority.SubTransactionTypeLinks.Select(link => link.SubTransactionTypeId).ToList(),
        authority.ResolveDescription(languageCode),
        authority.DescriptionAr,
        authority.DescriptionEn,
        authority.Code);
}

/// <summary>One currency-specific price on a service type.</summary>
public sealed record ServiceTypeCostDto(Guid CurrencyId, string? CurrencyCode, decimal Cost, decimal ExpressCost);

/// <summary>
/// A service type with everything the wizard's summary table and pricing need: cost, express
/// configuration, turnaround, and the files the applicant must upload.
/// </summary>
public sealed record ServiceTypeDto(
    Guid Id,
    Guid VerificationAuthorityId,
    Guid SubTransactionTypeId,
    string Name,
    string NameAr,
    string NameEn,
    string? Description,
    string? DescriptionAr,
    string? DescriptionEn,
    int ExecutionTimeDays,
    decimal Cost,
    bool EnableExpress,
    decimal ExpressCost,
    /// <summary>Admin-authored note beside the express toggle; null falls back to the app's wording.</summary>
    string? ExpressNote,
    string? ExpressNoteAr,
    string? ExpressNoteEn,
    bool IsActive,
    bool ShowOnLanding,
    IReadOnlyList<ServiceTypeCostDto> Costs,
    IReadOnlyList<RequiredFileDto> RequiredFiles,
    /// <summary>Languages the result may be issued in, as configured by an administrator.</summary>
    IReadOnlyList<string> OutputLanguages,
    string Code = "")
{
    public static ServiceTypeDto From(
        ServiceType serviceType,
        string? languageCode,
        Guid? priceCurrencyId = null) => new(
        serviceType.Id,
        serviceType.VerificationAuthorityId,
        serviceType.SubTransactionTypeId,
        serviceType.ResolveName(languageCode),
        serviceType.NameAr,
        serviceType.NameEn,
        serviceType.ResolveDescription(languageCode),
        serviceType.DescriptionAr,
        serviceType.DescriptionEn,
        serviceType.ExecutionTimeDays,
        ResolveCost(serviceType, priceCurrencyId),
        serviceType.EnableExpress,
        // A service that does not offer express has no express price to advertise.
        serviceType.EnableExpress ? ResolveExpressCost(serviceType, priceCurrencyId) : 0m,
        serviceType.ResolveExpressNote(languageCode),
        serviceType.ExpressNoteAr,
        serviceType.ExpressNoteEn,
        serviceType.IsActive,
        serviceType.ShowOnLanding,
        serviceType.Costs
            .OrderBy(c => c.Currency?.Code ?? string.Empty)
            .Select(c => new ServiceTypeCostDto(
                c.CurrencyId,
                c.Currency?.Code,
                c.Cost,
                serviceType.EnableExpress ? c.ExpressCost : 0m))
            .ToList(),
        serviceType.RequiredFiles
            .OrderByDescending(f => f.IsMandatory)
            .ThenBy(f => f.NameEn)
            .Select(f => RequiredFileDto.From(f, languageCode))
            .ToList(),
        serviceType.ResolveOutputLanguages(),
        serviceType.Code);

    private static decimal ResolveCost(ServiceType serviceType, Guid? priceCurrencyId)
    {
        if (priceCurrencyId is { } currencyId)
        {
            var match = serviceType.FindCost(currencyId);
            if (match is not null) return match.Cost;
        }

        return serviceType.Costs.OrderBy(c => c.Cost).FirstOrDefault()?.Cost ?? serviceType.Cost;
    }

    private static decimal ResolveExpressCost(ServiceType serviceType, Guid? priceCurrencyId)
    {
        if (priceCurrencyId is { } currencyId)
        {
            var match = serviceType.FindCost(currencyId);
            if (match is not null) return match.ExpressCost;
        }

        return serviceType.Costs.OrderBy(c => c.Cost).FirstOrDefault()?.ExpressCost
               ?? serviceType.ExpressCost;
    }
}

public sealed record RequiredFileDto(
    Guid Id,
    Guid ServiceTypeId,
    string Name,
    string NameAr,
    string NameEn,
    bool IsMandatory,
    /// <summary>Effective per-document size cap in bytes, already clamped to the platform maximum.</summary>
    long MaxSizeBytes,
    int MaxFiles,
    IReadOnlyList<RequiredFileFieldDto> Fields,
    /// <summary>
    /// The formats this document accepts, as <c>DocumentFileTypes</c> codes — already resolved, so
    /// a document configured with none reports the platform default rather than an empty list.
    /// </summary>
    IReadOnlyList<string> AllowedFileTypes,
    /// <summary>The same set as file extensions, for the upload control's accept list.</summary>
    IReadOnlyList<string> AllowedExtensions)
{
    public static RequiredFileDto From(ServiceTypeRequiredFile file, string? languageCode) => new(
        file.Id,
        file.ServiceTypeId,
        file.ResolveName(languageCode),
        file.NameAr,
        file.NameEn,
        file.IsMandatory,
        file.ResolveMaxSizeBytes(ApplicationFile.MaxFileSizeBytes),
        file.MaxFiles,
        file.Fields
            .Where(field => field.IsActive)
            .OrderBy(field => field.SortOrder)
            .ThenBy(field => field.NameEn)
            .Select(field => RequiredFileFieldDto.From(field, languageCode))
            .ToList(),
        file.ResolveAllowedFileTypes(),
        DocumentFileTypes.ExtensionsForAll(file.ResolveAllowedFileTypes()));
}

/// <summary>A custom field the applicant fills in beside a required document.</summary>
public sealed record RequiredFileFieldDto(
    Guid Id,
    string Name,
    string NameAr,
    string NameEn,
    RequiredFieldType FieldType,
    bool IsRequired,
    int SortOrder,
    int? MinLength,
    int? MaxLength,
    string? Pattern,
    decimal? MinValue,
    decimal? MaxValue,
    RequiredFieldDateRule DateRule,
    DateOnly? MinDate,
    DateOnly? MaxDate,
    IReadOnlyList<RequiredFileFieldOptionDto> Options)
{
    public static RequiredFileFieldDto From(RequiredFileField field, string? languageCode) => new(
        field.Id,
        field.ResolveName(languageCode),
        field.NameAr,
        field.NameEn,
        field.FieldType,
        field.IsRequired,
        field.SortOrder,
        field.MinLength,
        field.MaxLength,
        field.Pattern,
        field.MinValue,
        field.MaxValue,
        field.DateRule,
        field.MinDate,
        field.MaxDate,
        field.Options
            .OrderBy(option => option.SortOrder)
            .Select(option => new RequiredFileFieldOptionDto(
                option.Value,
                option.ResolveLabel(languageCode),
                option.LabelAr,
                option.LabelEn))
            .ToList());
}

public sealed record RequiredFileFieldOptionDto(string Value, string Label, string LabelAr, string LabelEn);

/// <summary>
/// A body an application can be addressed to. Offered as a list on a new application; the
/// applicant may still type something that is not on it.
/// </summary>
public sealed record AddresseeDto(
    Guid Id,
    string Name,
    string NameAr,
    string NameEn,
    int SortOrder,
    bool IsActive)
{
    public static AddresseeDto From(Addressee addressee, string? languageCode)
    {
        ArgumentNullException.ThrowIfNull(addressee);

        return new(
            addressee.Id,
            addressee.ResolveName(languageCode),
            addressee.NameAr,
            addressee.NameEn,
            addressee.SortOrder,
            addressee.IsActive);
    }
}
