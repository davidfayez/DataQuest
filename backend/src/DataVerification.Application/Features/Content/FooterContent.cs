using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Common;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Content;

// -------------------------------------------------------------------- Public DTOs

/// <summary>One row in a footer column, resolved to one language.</summary>
/// <param name="Url">As typed. The site decides whether it is external, an anchor, or an app path.</param>
/// <param name="Visibility">
/// <c>Everyone</c>, <c>SignedIn</c> or <c>SignedOut</c>. Sent to every visitor and filtered by the
/// site, because the footer is cached anonymously — a per-session response would be uncacheable
/// and would leak whether the reader is signed in into the cache key.
/// </param>
public sealed record FooterLinkDto(Guid Id, string Label, string Url, string Visibility, int SortOrder);

/// <summary>One mark beside the brand block, resolved to one language.</summary>
/// <param name="ImageUrl">API-relative path the site turns into an <c>img</c> source.</param>
public sealed record FooterLogoDto(Guid Id, string Alt, string ImageUrl, string? Url, int SortOrder);

/// <summary>Everything the footer draws, beyond the channels it already fetches.</summary>
public sealed record FooterContentDto(
    /// <summary>The line under the brand name.</summary>
    string Subtitle,
    /// <summary>The three column headings, so an operator can rename them.</summary>
    string ExploreHeading,
    string AccountHeading,
    string OrganisationHeading,
    IReadOnlyList<FooterLinkDto> Explore,
    IReadOnlyList<FooterLinkDto> Account,
    IReadOnlyList<FooterLinkDto> Organisation,
    /// <summary>Empty draws no row of marks at all.</summary>
    IReadOnlyList<FooterLogoDto> Logos);

// --------------------------------------------------------------------- Admin DTOs

/// <summary>Column and visibility are names, matching what <see cref="UpsertFooterLinkCommand"/> takes.</summary>
public sealed record AdminFooterLinkDto(
    Guid Id,
    string Column,
    string Visibility,
    IReadOnlyDictionary<string, string> Labels,
    string Url,
    int SortOrder,
    bool IsActive);

public sealed record AdminFooterLogoDto(
    Guid Id,
    IReadOnlyDictionary<string, string> Alts,
    string? Url,
    string? FileName,
    bool HasImage,
    /// <summary>False when it is active but has no file yet, so the editor can say why it is hidden.</summary>
    bool IsShowable,
    int SortOrder,
    bool IsActive);

public sealed record AdminFooterContentDto(
    IReadOnlyDictionary<string, string> Subtitle,
    IReadOnlyDictionary<string, string> ExploreHeading,
    IReadOnlyDictionary<string, string> AccountHeading,
    IReadOnlyDictionary<string, string> OrganisationHeading,
    IReadOnlyList<AdminFooterLinkDto> Links,
    IReadOnlyList<AdminFooterLogoDto> Logos);

/// <summary>Setting keys and the built-in English copy used until an admin overrides it.</summary>
public static class FooterContentDefaults
{
    public const string SubtitleKeyPrefix = "footer.subtitle";
    public const string ExploreHeadingKeyPrefix = "footer.heading.explore";
    public const string AccountHeadingKeyPrefix = "footer.heading.account";
    public const string OrganisationHeadingKeyPrefix = "footer.heading.organisation";

    public const string Subtitle = "Official document verification through trusted government authorities.";
    public const string ExploreHeading = "Explore";
    public const string AccountHeading = "Account";
    public const string OrganisationHeading = "Organisation";

    /// <summary>Where footer marks are written, alongside the other upload directories.</summary>
    public const string StorageDirectory = "footer";

    /// <summary>A footer mark is small; anything larger is a photograph pasted in by mistake.</summary>
    public const long MaxLogoBytes = 2 * 1024 * 1024;

    public static string SubtitleKey(string lang) => $"{SubtitleKeyPrefix}.{lang}";
    public static string ExploreHeadingKey(string lang) => $"{ExploreHeadingKeyPrefix}.{lang}";
    public static string AccountHeadingKey(string lang) => $"{AccountHeadingKeyPrefix}.{lang}";
    public static string OrganisationHeadingKey(string lang) => $"{OrganisationHeadingKeyPrefix}.{lang}";
}

// ------------------------------------------------------------------------ Queries

public sealed record GetFooterContentQuery : IRequest<FooterContentDto>;

public sealed record GetAdminFooterContentQuery : IRequest<AdminFooterContentDto>;

/// <summary>The bytes of one footer mark, for the public endpoint that serves them.</summary>
public sealed record FooterLogoDownload(Stream Content, string ContentType, string FileName);

public sealed record GetFooterLogoImageQuery(Guid Id) : IRequest<FooterLogoDownload>;

// ----------------------------------------------------------------------- Commands

/// <summary>
/// Column and visibility travel as names ("Explore", "SignedIn"), not numbers, because that is how
/// the read side reports them. Taking the enums directly would make the API accept only the numeric
/// form on the way in while handing names back on the way out.
/// </summary>
public sealed record UpsertFooterLinkCommand(
    Guid? Id,
    string Column,
    string Visibility,
    Dictionary<string, string> Labels,
    string Url,
    int SortOrder,
    bool IsActive) : IRequest<AdminFooterLinkDto>
{
    public FooterColumn? ParsedColumn =>
        Enum.TryParse<FooterColumn>(Column, ignoreCase: true, out var column) ? column : null;

    public FooterLinkVisibility? ParsedVisibility =>
        Enum.TryParse<FooterLinkVisibility>(Visibility, ignoreCase: true, out var visibility)
            ? visibility
            : null;
}

public sealed record DeleteFooterLinkCommand(Guid Id) : IRequest<Unit>;

/// <summary>Alternative text is optional here — see the validator for why — so the field may be absent.</summary>
public sealed record UpsertFooterLogoCommand(
    Guid? Id,
    Dictionary<string, string>? Alts,
    string? Url,
    int SortOrder,
    bool IsActive) : IRequest<AdminFooterLogoDto>;

public sealed record DeleteFooterLogoCommand(Guid Id) : IRequest<Unit>;

public sealed record UploadFooterLogoImageCommand(Guid Id, Stream Content, string FileName, long SizeBytes)
    : IRequest<AdminFooterLogoDto>;

/// <summary>
/// The footer's headings and subtitle. Every field is optional and null means "leave alone", so a
/// page saves only the copy it owns.
/// </summary>
public sealed record UpdateFooterHeadingsCommand(
    Dictionary<string, string>? Subtitle = null,
    Dictionary<string, string>? ExploreHeading = null,
    Dictionary<string, string>? AccountHeading = null,
    Dictionary<string, string>? OrganisationHeading = null) : IRequest<Unit>;

// --------------------------------------------------------------------- Validation

public sealed class UpsertFooterLinkCommandValidator : AbstractValidator<UpsertFooterLinkCommand>
{
    public UpsertFooterLinkCommandValidator()
    {
        RuleFor(c => c.Column)
            .Must(c => Enum.TryParse<FooterColumn>(c, ignoreCase: true, out _))
            .WithMessage("Choose Explore, Account or Organisation.");

        RuleFor(c => c.Visibility)
            .Must(v => Enum.TryParse<FooterLinkVisibility>(v, ignoreCase: true, out _))
            .WithMessage("Choose Everyone, SignedIn or SignedOut.");

        RuleFor(c => c.SortOrder).GreaterThanOrEqualTo(0);

        RuleFor(c => c.Labels)
            .Must(TranslationRules.HasEnglish).WithMessage("An English label is required.")
            .Must(TranslationRules.AllSupported).WithMessage("A label uses an unsupported language.")
            .Must(l => TranslationRules.WithinLength(l, 120)).WithMessage("A label is too long (max 120).");

        RuleFor(c => c.Url)
            .NotEmpty().WithMessage("Enter where this link goes.")
            .MaximumLength(500)
            .Must(BeSafe)
            .WithMessage(
                "Use a full https:// address, an anchor like #services, or an in-app path like /tools.");
    }

    /// <summary>
    /// http(s), an anchor, or an app-relative path. Everything else — javascript:, data: — is a
    /// link somebody clicks without reading, which is exactly where those schemes do damage.
    /// </summary>
    internal static bool BeSafe(string? url)
    {
        var value = url?.Trim();
        if (string.IsNullOrEmpty(value)) return false;

        if (value.StartsWith('#')) return value.Length > 1;

        if (value.StartsWith('/'))
        {
            // "//evil.example" is protocol-relative and leaves the site, so it is not a path.
            return !value.StartsWith("//", StringComparison.Ordinal);
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);
    }
}

public sealed class UpsertFooterLogoCommandValidator : AbstractValidator<UpsertFooterLogoCommand>
{
    public UpsertFooterLogoCommandValidator()
    {
        RuleFor(c => c.SortOrder).GreaterThanOrEqualTo(0);

        // Alternative text is optional, unlike everywhere else copy is stored. A mark in this row
        // is presentational — it sits beside the subtitle that already says what the organisation
        // does — so the panel does not ask for one, and an empty alt is the correct rendering for
        // an image that carries no meaning of its own. Anything supplied is still bounded.
        RuleFor(c => c.Alts)
            .Must(TranslationRules.AllSupported).WithMessage("Alternative text uses an unsupported language.")
            .Must(a => TranslationRules.WithinLength(a, 200)).WithMessage("Alternative text is too long (max 200).");

        RuleFor(c => c.Url)
            .MaximumLength(500)
            .Must(url => UpsertFooterLinkCommandValidator.BeSafe(url))
            .When(c => !string.IsNullOrWhiteSpace(c.Url))
            .WithMessage("Use a full https:// address, or leave it empty for a mark that is not a link.");
    }
}

public sealed class UpdateFooterHeadingsCommandValidator : AbstractValidator<UpdateFooterHeadingsCommand>
{
    public UpdateFooterHeadingsCommandValidator()
    {
        Check(c => c.Subtitle, "subtitle", 300);
        Check(c => c.ExploreHeading, "heading", 60);
        Check(c => c.AccountHeading, "heading", 60);
        Check(c => c.OrganisationHeading, "heading", 60);
    }

    private void Check(
        Func<UpdateFooterHeadingsCommand, Dictionary<string, string>?> selector,
        string noun,
        int max)
    {
        RuleFor(c => selector(c)!)
            .Must(TranslationRules.HasEnglish).WithMessage($"An English {noun} is required.")
            .Must(TranslationRules.AllSupported).WithMessage($"A {noun} uses an unsupported language.")
            .Must(v => TranslationRules.WithinLength(v, max)).WithMessage($"A {noun} is too long (max {max}).")
            .When(c => selector(c) is not null);
    }
}

// ------------------------------------------------------------------------ Handlers

public sealed class FooterContentHandlers :
    IRequestHandler<GetFooterContentQuery, FooterContentDto>,
    IRequestHandler<GetAdminFooterContentQuery, AdminFooterContentDto>,
    IRequestHandler<GetFooterLogoImageQuery, FooterLogoDownload>,
    IRequestHandler<UpsertFooterLinkCommand, AdminFooterLinkDto>,
    IRequestHandler<DeleteFooterLinkCommand, Unit>,
    IRequestHandler<UpsertFooterLogoCommand, AdminFooterLogoDto>,
    IRequestHandler<DeleteFooterLogoCommand, Unit>,
    IRequestHandler<UploadFooterLogoImageCommand, AdminFooterLogoDto>,
    IRequestHandler<UpdateFooterHeadingsCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _auditLogger;
    private readonly IFileStorage _storage;
    private readonly IFileTypeValidator _typeValidator;

    public FooterContentHandlers(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IAuditLogger auditLogger,
        IFileStorage storage,
        IFileTypeValidator typeValidator)
    {
        _db = db;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
        _storage = storage;
        _typeValidator = typeValidator;
    }

    public async Task<FooterContentDto> Handle(
        GetFooterContentQuery request,
        CancellationToken cancellationToken)
    {
        var language = _currentUser.LanguageCode;

        var links = await _db.FooterLinks
            .AsNoTracking()
            .Where(l => l.IsActive)
            .Include(l => l.Translations)
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var logos = await _db.FooterLogos
            .AsNoTracking()
            .Where(l => l.IsActive && l.ImageStoragePath != null)
            .Include(l => l.Translations)
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var copy = await LoadCopyAsync(cancellationToken);

        List<FooterLinkDto> Column(FooterColumn column) => links
            .Where(l => l.Column == column)
            .Select(l => new FooterLinkDto(
                l.Id,
                l.ResolveLabel(language),
                l.Url,
                l.Visibility.ToString(),
                l.SortOrder))
            .ToList();

        return new FooterContentDto(
            Resolve(copy.Subtitle, language, FooterContentDefaults.Subtitle),
            Resolve(copy.ExploreHeading, language, FooterContentDefaults.ExploreHeading),
            Resolve(copy.AccountHeading, language, FooterContentDefaults.AccountHeading),
            Resolve(copy.OrganisationHeading, language, FooterContentDefaults.OrganisationHeading),
            Column(FooterColumn.Explore),
            Column(FooterColumn.Account),
            Column(FooterColumn.Organisation),
            logos
                .Select(l => new FooterLogoDto(
                    l.Id,
                    l.ResolveAlt(language),
                    $"content/footer/logos/{l.Id}/image",
                    string.IsNullOrWhiteSpace(l.Url) ? null : l.Url,
                    l.SortOrder))
                .ToList());
    }

    public async Task<AdminFooterContentDto> Handle(
        GetAdminFooterContentQuery request,
        CancellationToken cancellationToken)
    {
        var links = await _db.FooterLinks
            .AsNoTracking()
            .Include(l => l.Translations)
            .OrderBy(l => l.Column)
            .ThenBy(l => l.SortOrder)
            .ThenBy(l => l.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var logos = await _db.FooterLogos
            .AsNoTracking()
            .Include(l => l.Translations)
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var copy = await LoadCopyAsync(cancellationToken);

        return new AdminFooterContentDto(
            copy.Subtitle,
            copy.ExploreHeading,
            copy.AccountHeading,
            copy.OrganisationHeading,
            links.Select(ToAdminDto).ToList(),
            logos.Select(ToAdminDto).ToList());
    }

    public async Task<FooterLogoDownload> Handle(
        GetFooterLogoImageQuery request,
        CancellationToken cancellationToken)
    {
        var logo = await _db.FooterLogos
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(FooterLogo), request.Id);

        if (string.IsNullOrWhiteSpace(logo.ImageStoragePath)
            || !await _storage.ExistsAsync(logo.ImageStoragePath, cancellationToken))
        {
            throw new NotFoundException(nameof(FooterLogo), request.Id);
        }

        return new FooterLogoDownload(
            await _storage.OpenReadAsync(logo.ImageStoragePath, cancellationToken),
            logo.ImageContentType ?? "application/octet-stream",
            logo.ImageFileName ?? "logo");
    }

    public async Task<AdminFooterLinkDto> Handle(
        UpsertFooterLinkCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        FooterLink link;
        if (request.Id is { } id && id != Guid.Empty)
        {
            link = await _db.FooterLinks
                .Include(l => l.Translations)
                .FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(FooterLink), id);
        }
        else
        {
            link = new FooterLink { Url = string.Empty };
            _db.FooterLinks.Add(link);
        }

        // Validation has already rejected anything unparseable.
        link.Column = request.ParsedColumn!.Value;
        link.Visibility = request.ParsedVisibility!.Value;
        link.Url = request.Url.Trim();
        link.SortOrder = request.SortOrder;
        link.IsActive = request.IsActive;

        ApplyLabels(link, request.Labels);

        await _db.SaveChangesAsync(cancellationToken);
        await _auditLogger.LogAsync(
            request.Id is null ? "FooterLink.Created" : "FooterLink.Updated",
            nameof(FooterLink),
            link.Id,
            new { Column = link.Column.ToString(), link.Url },
            cancellationToken);

        return ToAdminDto(link);
    }

    public async Task<Unit> Handle(DeleteFooterLinkCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var link = await _db.FooterLinks.FirstOrDefaultAsync(l => l.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(FooterLink), request.Id);

        _db.FooterLinks.Remove(link);
        await _db.SaveChangesAsync(cancellationToken);
        await _auditLogger.LogAsync(
            "FooterLink.Deleted", nameof(FooterLink), request.Id, null, cancellationToken);

        return Unit.Value;
    }

    public async Task<AdminFooterLogoDto> Handle(
        UpsertFooterLogoCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        FooterLogo logo;
        if (request.Id is { } id && id != Guid.Empty)
        {
            logo = await _db.FooterLogos
                .Include(l => l.Translations)
                .FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(FooterLogo), id);
        }
        else
        {
            logo = new FooterLogo();
            _db.FooterLogos.Add(logo);
        }

        logo.Url = string.IsNullOrWhiteSpace(request.Url) ? null : request.Url.Trim();
        logo.SortOrder = request.SortOrder;
        logo.IsActive = request.IsActive;

        ApplyAlts(logo, request.Alts ?? []);

        await _db.SaveChangesAsync(cancellationToken);
        await _auditLogger.LogAsync(
            request.Id is null ? "FooterLogo.Created" : "FooterLogo.Updated",
            nameof(FooterLogo),
            logo.Id,
            null,
            cancellationToken);

        return ToAdminDto(logo);
    }

    public async Task<Unit> Handle(DeleteFooterLogoCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var logo = await _db.FooterLogos.FirstOrDefaultAsync(l => l.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(FooterLogo), request.Id);

        var path = logo.ImageStoragePath;

        _db.FooterLogos.Remove(logo);
        await _db.SaveChangesAsync(cancellationToken);

        // Only once the row is gone is the file discarded, so a failure here leaves no row
        // pointing at something that is not there.
        if (!string.IsNullOrWhiteSpace(path))
        {
            await _storage.DeleteAsync(path, cancellationToken);
        }

        await _auditLogger.LogAsync(
            "FooterLogo.Deleted", nameof(FooterLogo), request.Id, null, cancellationToken);

        return Unit.Value;
    }

    public async Task<AdminFooterLogoDto> Handle(
        UploadFooterLogoImageCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var logo = await _db.FooterLogos
            .Include(l => l.Translations)
            .FirstOrDefaultAsync(l => l.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(FooterLogo), request.Id);

        if (request.SizeBytes <= 0 || request.SizeBytes > FooterContentDefaults.MaxLogoBytes)
        {
            throw new DomainException(
                "footer.logo_too_large",
                $"The image must be between 1 byte and "
                + $"{FooterContentDefaults.MaxLogoBytes / (1024 * 1024)} MB.");
        }

        // Signature check, not an extension check: the declared name proves nothing.
        var detected = await _typeValidator.DetectAllowedContentTypeAsync(
            request.Content, request.FileName, cancellationToken);

        if (detected is not ("image/jpeg" or "image/png"))
        {
            throw new DomainException(
                "footer.logo_type_not_allowed",
                "The image must be a JPEG or a PNG.");
        }

        var previousPath = logo.ImageStoragePath;

        var storedPath = await _storage.SaveAsync(
            request.Content,
            FooterContentDefaults.StorageDirectory,
            request.FileName,
            logo.Id.ToString(),
            cancellationToken);

        logo.ImageStoragePath = storedPath;
        logo.ImageContentType = detected;
        logo.ImageFileName = request.FileName;

        await _db.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(previousPath) && previousPath != storedPath)
        {
            await _storage.DeleteAsync(previousPath, cancellationToken);
        }

        await _auditLogger.LogAsync(
            "FooterLogo.ImageUploaded",
            nameof(FooterLogo),
            logo.Id,
            new { request.FileName, request.SizeBytes },
            cancellationToken);

        return ToAdminDto(logo);
    }

    public async Task<Unit> Handle(
        UpdateFooterHeadingsCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        foreach (var lang in LandingLanguages.All)
        {
            await ApplyAsync(request.Subtitle, FooterContentDefaults.SubtitleKey(lang), lang);
            await ApplyAsync(request.ExploreHeading, FooterContentDefaults.ExploreHeadingKey(lang), lang);
            await ApplyAsync(request.AccountHeading, FooterContentDefaults.AccountHeadingKey(lang), lang);
            await ApplyAsync(
                request.OrganisationHeading, FooterContentDefaults.OrganisationHeadingKey(lang), lang);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _auditLogger.LogAsync(
            "FooterHeadings.Updated", nameof(SiteSetting), null, null, cancellationToken);

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

    private void ApplyLabels(FooterLink link, IReadOnlyDictionary<string, string> labels)
    {
        link.Translations.Clear();

        foreach (var lang in LandingLanguages.All)
        {
            var label = Lookup(labels, lang);
            if (!string.IsNullOrWhiteSpace(label))
            {
                link.Translations.Add(new FooterLinkTranslation
                {
                    LanguageCode = lang,
                    Label = label!.Trim(),
                });
            }
        }
    }

    private void ApplyAlts(FooterLogo logo, IReadOnlyDictionary<string, string> alts)
    {
        logo.Translations.Clear();

        foreach (var lang in LandingLanguages.All)
        {
            var alt = Lookup(alts, lang);
            if (!string.IsNullOrWhiteSpace(alt))
            {
                logo.Translations.Add(new FooterLogoTranslation
                {
                    LanguageCode = lang,
                    Alt = alt!.Trim(),
                });
            }
        }
    }

    private static AdminFooterLinkDto ToAdminDto(FooterLink link) => new(
        link.Id,
        link.Column.ToString(),
        link.Visibility.ToString(),
        link.Translations.ToDictionary(t => t.LanguageCode, t => t.Label, StringComparer.OrdinalIgnoreCase),
        link.Url,
        link.SortOrder,
        link.IsActive);

    private static AdminFooterLogoDto ToAdminDto(FooterLogo logo) => new(
        logo.Id,
        logo.Translations.ToDictionary(t => t.LanguageCode, t => t.Alt, StringComparer.OrdinalIgnoreCase),
        logo.Url,
        logo.ImageFileName,
        logo.HasImage,
        logo.IsShowable,
        logo.SortOrder,
        logo.IsActive);

    private async Task<FooterCopy> LoadCopyAsync(CancellationToken cancellationToken)
    {
        var prefixes = new[]
        {
            FooterContentDefaults.SubtitleKeyPrefix,
            FooterContentDefaults.ExploreHeadingKeyPrefix,
            FooterContentDefaults.AccountHeadingKeyPrefix,
            FooterContentDefaults.OrganisationHeadingKeyPrefix,
        };

        var settings = await _db.SiteSettings
            .AsNoTracking()
            .Where(s => prefixes.Any(prefix => s.Key.StartsWith(prefix)))
            .ToListAsync(cancellationToken);

        var copy = new FooterCopy(New(), New(), New(), New());

        foreach (var setting in settings)
        {
            // Most specific first: the heading prefixes all begin "footer.heading.", and the
            // subtitle's is a different branch, so no key can match two of them.
            if (TrySuffix(setting.Key, FooterContentDefaults.ExploreHeadingKeyPrefix, out var ex))
                copy.ExploreHeading[ex] = setting.Value;
            else if (TrySuffix(setting.Key, FooterContentDefaults.AccountHeadingKeyPrefix, out var ac))
                copy.AccountHeading[ac] = setting.Value;
            else if (TrySuffix(setting.Key, FooterContentDefaults.OrganisationHeadingKeyPrefix, out var or))
                copy.OrganisationHeading[or] = setting.Value;
            else if (TrySuffix(setting.Key, FooterContentDefaults.SubtitleKeyPrefix, out var su))
                copy.Subtitle[su] = setting.Value;
        }

        return copy;

        static Dictionary<string, string> New() => new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record FooterCopy(
        Dictionary<string, string> Subtitle,
        Dictionary<string, string> ExploreHeading,
        Dictionary<string, string> AccountHeading,
        Dictionary<string, string> OrganisationHeading);

    /// <summary>Splits "footer.subtitle.ar" into its trailing language code.</summary>
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

        foreach (var kv in map)
        {
            if (string.Equals(kv.Key, lang, StringComparison.OrdinalIgnoreCase)) return kv.Value;
        }

        return null;
    }
}
