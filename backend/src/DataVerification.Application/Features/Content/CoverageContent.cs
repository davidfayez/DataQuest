using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Content;

// -------------------------------------------------------------------- Public DTOs

/// <summary>
/// One country on the coverage map.
/// </summary>
/// <param name="Code">
/// ISO 3166-1 alpha-2. This is what places the spot: the site holds the world as one SVG path per
/// code, so the code is the join between a row here and a shape on the map.
/// </param>
public sealed record CoverageCountryDto(Guid Id, string Code, string Name, string Marker);

/// <summary>The coverage section, resolved to one language.</summary>
public sealed record CoverageContentDto(
    string Title,
    string Subtitle,
    /// <summary>Empty hides the whole section, heading included — a map with no spots claims nothing.</summary>
    IReadOnlyList<CoverageCountryDto> Countries);

// --------------------------------------------------------------------- Admin DTOs

public sealed record AdminCoverageEntryDto(
    Guid Id,
    Guid CountryId,
    string Code,
    /// <summary>The country's own name, in whichever language the panel asked for.</summary>
    string Name,
    /// <summary><c>Spot</c> or <c>Flag</c>, matching what the command takes.</summary>
    string Marker,
    int SortOrder,
    bool IsPublished);

public sealed record AdminCoverageContentDto(
    IReadOnlyDictionary<string, string> Title,
    IReadOnlyDictionary<string, string> Subtitle,
    IReadOnlyList<AdminCoverageEntryDto> Countries);

/// <summary>Setting keys and the built-in English copy used until an admin overrides it.</summary>
public static class CoverageContentDefaults
{
    public const string TitleKeyPrefix = "landing.coverage.title";
    public const string SubtitleKeyPrefix = "landing.coverage.subtitle";

    public const string Title = "Where we verify";
    public const string Subtitle =
        "We work directly with issuing authorities in these countries, and we are adding more.";

    public static string TitleKey(string languageCode) => $"{TitleKeyPrefix}.{languageCode}";
    public static string SubtitleKey(string languageCode) => $"{SubtitleKeyPrefix}.{languageCode}";
}

// ------------------------------------------------------------------------ Queries

public sealed record GetCoverageContentQuery : IRequest<CoverageContentDto>;

public sealed record GetAdminCoverageContentQuery : IRequest<AdminCoverageContentDto>;

// ----------------------------------------------------------------------- Commands

/// <summary>
/// The marker travels as a name ("Spot", "Flag"), not a number, because that is how the read side
/// reports it. Taking the enum directly would make the API accept only the numeric form on the way
/// in while handing a name back on the way out.
/// </summary>
public sealed record UpsertCoverageEntryCommand(
    Guid? Id,
    Guid CountryId,
    string Marker,
    int SortOrder,
    bool IsPublished) : IRequest<AdminCoverageEntryDto>
{
    public CoverageMarker? ParsedMarker =>
        Enum.TryParse<CoverageMarker>(Marker, ignoreCase: true, out var marker) ? marker : null;
}

public sealed record DeleteCoverageEntryCommand(Guid Id) : IRequest<Unit>;

/// <summary>Both fields optional; null means "leave alone", so a page saves only what it owns.</summary>
public sealed record UpdateCoverageHeadingCommand(
    Dictionary<string, string>? Title = null,
    Dictionary<string, string>? Subtitle = null) : IRequest<Unit>;

// --------------------------------------------------------------------- Validation

public sealed class UpsertCoverageEntryCommandValidator : AbstractValidator<UpsertCoverageEntryCommand>
{
    public UpsertCoverageEntryCommandValidator()
    {
        RuleFor(c => c.CountryId).NotEmpty().WithMessage("Choose a country.");

        RuleFor(c => c.Marker)
            .Must(m => Enum.TryParse<CoverageMarker>(m, ignoreCase: true, out _))
            .WithMessage("Choose Spot or Flag.");

        RuleFor(c => c.SortOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateCoverageHeadingCommandValidator : AbstractValidator<UpdateCoverageHeadingCommand>
{
    public UpdateCoverageHeadingCommandValidator()
    {
        RuleFor(c => c.Title)
            .Must(TranslationRules.AllSupported).WithMessage("The title uses an unsupported language.")
            .Must(t => TranslationRules.WithinLength(t, 200)).WithMessage("The title is too long (max 200).");

        RuleFor(c => c.Subtitle)
            .Must(TranslationRules.AllSupported).WithMessage("The subtitle uses an unsupported language.")
            .Must(s => TranslationRules.WithinLength(s, 400))
            .WithMessage("The subtitle is too long (max 400).");
    }
}

// ----------------------------------------------------------------------- Handlers

public sealed class CoverageContentHandlers :
    IRequestHandler<GetCoverageContentQuery, CoverageContentDto>,
    IRequestHandler<GetAdminCoverageContentQuery, AdminCoverageContentDto>,
    IRequestHandler<UpsertCoverageEntryCommand, AdminCoverageEntryDto>,
    IRequestHandler<DeleteCoverageEntryCommand, Unit>,
    IRequestHandler<UpdateCoverageHeadingCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _auditLogger;

    public CoverageContentHandlers(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IAuditLogger auditLogger)
    {
        _db = db;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
    }

    public async Task<CoverageContentDto> Handle(
        GetCoverageContentQuery request,
        CancellationToken cancellationToken)
    {
        var language = _currentUser.LanguageCode;

        var entries = await PublishedQuery()
            .OrderBy(e => e.SortOrder)
            .ThenBy(e => e.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var copy = await LoadCopyAsync(cancellationToken);

        return new CoverageContentDto(
            Resolve(copy.Title, language, CoverageContentDefaults.Title),
            Resolve(copy.Subtitle, language, CoverageContentDefaults.Subtitle),
            entries
                .Select(e => new CoverageCountryDto(
                    e.Id,
                    e.Country!.Code,
                    e.Country.ResolveName(language),
                    e.Marker.ToString()))
                .ToList());
    }

    public async Task<AdminCoverageContentDto> Handle(
        GetAdminCoverageContentQuery request,
        CancellationToken cancellationToken)
    {
        var language = _currentUser.LanguageCode;

        var entries = await _db.CoverageEntries
            .AsNoTracking()
            .Include(e => e.Country)
            .OrderBy(e => e.SortOrder)
            .ThenBy(e => e.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var copy = await LoadCopyAsync(cancellationToken);

        return new AdminCoverageContentDto(
            copy.Title,
            copy.Subtitle,
            entries.Select(e => ToDto(e, language)).ToList());
    }

    public async Task<AdminCoverageEntryDto> Handle(
        UpsertCoverageEntryCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var country = await _db.Countries
            .FirstOrDefaultAsync(c => c.Id == request.CountryId, cancellationToken)
            ?? throw new NotFoundException(nameof(Country), request.CountryId);

        CoverageEntry entry;
        if (request.Id is { } id && id != Guid.Empty)
        {
            entry = await _db.CoverageEntries
                .FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(CoverageEntry), id);
        }
        else
        {
            entry = new CoverageEntry();
            _db.CoverageEntries.Add(entry);
        }

        // The unique index would catch this, but as a 500 from the database rather than something
        // the panel can put next to the field.
        var duplicate = await _db.CoverageEntries.AnyAsync(
            e => e.CountryId == request.CountryId && e.Id != entry.Id,
            cancellationToken);

        if (duplicate)
        {
            throw new ConflictException($"{country.ResolveName("en")} is already on the map.");
        }

        entry.CountryId = request.CountryId;
        // Validation has already rejected anything unparseable.
        entry.Marker = request.ParsedMarker!.Value;
        entry.SortOrder = request.SortOrder;
        entry.IsPublished = request.IsPublished;

        await _db.SaveChangesAsync(cancellationToken);
        await _auditLogger.LogAsync(
            request.Id is null ? "CoverageEntry.Created" : "CoverageEntry.Updated",
            nameof(CoverageEntry),
            entry.Id,
            new { country.Code },
            cancellationToken);

        entry.Country = country;
        return ToDto(entry, _currentUser.LanguageCode);
    }

    public async Task<Unit> Handle(DeleteCoverageEntryCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entry = await _db.CoverageEntries
            .Include(e => e.Country)
            .FirstOrDefaultAsync(e => e.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(CoverageEntry), request.Id);

        _db.CoverageEntries.Remove(entry);
        await _db.SaveChangesAsync(cancellationToken);
        await _auditLogger.LogAsync(
            "CoverageEntry.Deleted",
            nameof(CoverageEntry),
            request.Id,
            new { entry.Country?.Code },
            cancellationToken);

        return Unit.Value;
    }

    public async Task<Unit> Handle(
        UpdateCoverageHeadingCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        foreach (var lang in LandingLanguages.All)
        {
            await ApplyAsync(request.Title, CoverageContentDefaults.TitleKey(lang), lang);
            await ApplyAsync(request.Subtitle, CoverageContentDefaults.SubtitleKey(lang), lang);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _auditLogger.LogAsync(
            "CoverageHeading.Updated", nameof(SiteSetting), null, null, cancellationToken);

        return Unit.Value;

        async Task ApplyAsync(Dictionary<string, string>? map, string key, string lang)
        {
            // A field the caller did not send is skipped rather than written as blank.
            if (map is null) return;

            var value = Lookup(map, lang);
            var row = await _db.SiteSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);

            if (string.IsNullOrWhiteSpace(value))
            {
                if (row is not null) _db.SiteSettings.Remove(row);
                return;
            }

            if (row is null) _db.SiteSettings.Add(new SiteSetting { Key = key, Value = value.Trim() });
            else row.Value = value.Trim();
        }
    }

    // --------------------------------------------------------------------- Helpers

    /// <summary>
    /// Published entries whose country is itself still active. A country switched off in the lookup
    /// is one the platform no longer serves, and the map would otherwise keep claiming it.
    /// </summary>
    private IQueryable<CoverageEntry> PublishedQuery() => _db.CoverageEntries
        .AsNoTracking()
        .Include(e => e.Country)
        .Where(e => e.IsPublished && e.Country!.IsActive);

    private static AdminCoverageEntryDto ToDto(CoverageEntry entry, string? language) => new(
        entry.Id,
        entry.CountryId,
        entry.Country?.Code ?? string.Empty,
        entry.Country?.ResolveName(language) ?? string.Empty,
        entry.Marker.ToString(),
        entry.SortOrder,
        entry.IsPublished);

    private async Task<CoverageCopy> LoadCopyAsync(CancellationToken cancellationToken)
    {
        var prefixes = new[]
        {
            CoverageContentDefaults.TitleKeyPrefix,
            CoverageContentDefaults.SubtitleKeyPrefix,
        };

        var settings = await _db.SiteSettings
            .AsNoTracking()
            .Where(s => prefixes.Any(prefix => s.Key.StartsWith(prefix)))
            .ToListAsync(cancellationToken);

        var copy = new CoverageCopy(New(), New());

        foreach (var setting in settings)
        {
            if (TrySuffix(setting.Key, CoverageContentDefaults.TitleKeyPrefix, out var title))
                copy.Title[title] = setting.Value;
            else if (TrySuffix(setting.Key, CoverageContentDefaults.SubtitleKeyPrefix, out var sub))
                copy.Subtitle[sub] = setting.Value;
        }

        return copy;

        static Dictionary<string, string> New() => new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record CoverageCopy(
        Dictionary<string, string> Title,
        Dictionary<string, string> Subtitle);

    /// <summary>Splits "landing.coverage.title.ar" into its trailing language code.</summary>
    private static bool TrySuffix(string key, string prefix, out string language)
    {
        language = string.Empty;

        if (!key.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase)) return false;

        language = key[(prefix.Length + 1)..];
        return language.Length > 0 && !language.Contains('.', StringComparison.Ordinal);
    }

    /// <summary>Requested language, then English, then the built-in copy.</summary>
    private static string Resolve(IReadOnlyDictionary<string, string> map, string? language, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(language) && map.TryGetValue(language, out var exact)
            && !string.IsNullOrWhiteSpace(exact))
        {
            return exact;
        }

        return map.TryGetValue(LandingLanguages.Default, out var english)
            && !string.IsNullOrWhiteSpace(english)
            ? english
            : fallback;
    }

    /// <summary>Case-insensitive lookup, since JSON keys arrive with whatever casing the client sent.</summary>
    private static string? Lookup(IReadOnlyDictionary<string, string>? map, string lang)
    {
        if (map is null) return null;

        foreach (var (key, value) in map)
        {
            if (string.Equals(key, lang, StringComparison.OrdinalIgnoreCase)) return value;
        }

        return null;
    }
}
