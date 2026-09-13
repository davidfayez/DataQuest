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
/// One entry in the site header.
/// </summary>
/// <param name="Key">
/// Names a built-in entry whose label and icon the app already has, or null for a row an operator
/// added.
/// </param>
/// <param name="Label">
/// The operator's wording for the language asked for, or null to use the app's own. A row with no
/// key always carries a label, since there would be nothing to fall back to.
/// </param>
/// <param name="Visibility">
/// Sent to every visitor and applied by the site, like the footer: the header is fetched
/// anonymously, and a per-session response would be uncacheable and would put "is this reader
/// signed in" into the cache key.
/// </param>
public sealed record HeaderLinkDto(
    Guid Id,
    string? Key,
    string? Label,
    string Url,
    string Visibility,
    int SortOrder);

public sealed record HeaderContentDto(IReadOnlyList<HeaderLinkDto> Links);

// --------------------------------------------------------------------- Admin DTOs

public sealed record AdminHeaderLinkDto(
    Guid Id,
    string? Key,
    IReadOnlyDictionary<string, string> Labels,
    string Url,
    string Visibility,
    int SortOrder,
    bool IsActive);

public sealed record AdminHeaderContentDto(IReadOnlyList<AdminHeaderLinkDto> Links);

/// <summary>The entries the site ships with, and the order they ship in.</summary>
public static class HeaderContentDefaults
{
    public const string Home = "home";
    public const string How = "how";
    public const string Services = "services";
    public const string Coverage = "coverage";
    public const string Knowledge = "knowledge";
    public const string Contact = "contact";

    /// <summary>
    /// The header as delivered: Home, How it works, Services, Coverage, Knowledge, Contact us.
    ///
    /// The three landing anchors are <c>SignedOut</c> because that is where they lead — a signed-in
    /// visitor has their own applications, wallet and tickets in the same bar. An operator can show
    /// them to everyone from the panel.
    /// </summary>
    public static IReadOnlyList<(string Key, HeaderLinkVisibility Visibility, string Url, int Sort)> Rows() =>
    [
        (Home, HeaderLinkVisibility.Everyone, "/", 0),
        (How, HeaderLinkVisibility.SignedOut, "#how", 1),
        (Services, HeaderLinkVisibility.SignedOut, "#services", 2),
        (Coverage, HeaderLinkVisibility.SignedOut, "#coverage", 3),
        (Knowledge, HeaderLinkVisibility.Everyone, "/tools", 4),
        (Contact, HeaderLinkVisibility.Everyone, "/contact", 5),
    ];
}

// ------------------------------------------------------------------------ Queries

public sealed record GetHeaderContentQuery : IRequest<HeaderContentDto>;

public sealed record GetAdminHeaderContentQuery : IRequest<AdminHeaderContentDto>;

// ----------------------------------------------------------------------- Commands

/// <summary>
/// Visibility travels as a name ("Everyone", "SignedIn"), not a number, because that is how the
/// read side reports it.
///
/// Labels are optional for a built-in entry — leaving them empty is what keeps the app's own ten
/// translations in use — and required for a row an operator added.
/// </summary>
public sealed record UpsertHeaderLinkCommand(
    Guid? Id,
    string? Key,
    Dictionary<string, string>? Labels,
    string Url,
    string Visibility,
    int SortOrder,
    bool IsActive) : IRequest<AdminHeaderLinkDto>
{
    public HeaderLinkVisibility? ParsedVisibility =>
        Enum.TryParse<HeaderLinkVisibility>(Visibility, ignoreCase: true, out var visibility)
            ? visibility
            : null;

    /// <summary>Trimmed and lower-cased, so "Home" and "home" cannot become two built-in rows.</summary>
    public string? NormalisedKey =>
        string.IsNullOrWhiteSpace(Key) ? null : Key.Trim().ToLowerInvariant();
}

public sealed record DeleteHeaderLinkCommand(Guid Id) : IRequest<Unit>;

// --------------------------------------------------------------------- Validation

public sealed class UpsertHeaderLinkCommandValidator : AbstractValidator<UpsertHeaderLinkCommand>
{
    public UpsertHeaderLinkCommandValidator()
    {
        RuleFor(c => c.Visibility)
            .Must(v => Enum.TryParse<HeaderLinkVisibility>(v, ignoreCase: true, out _))
            .WithMessage("Choose Everyone, SignedIn or SignedOut.");

        RuleFor(c => c.SortOrder).GreaterThanOrEqualTo(0);

        RuleFor(c => c.Key)
            .Must(key => string.IsNullOrWhiteSpace(key) || key.Trim().Length <= 40)
            .WithMessage("A key is at most 40 characters.");

        RuleFor(c => c.Labels)
            .Must(TranslationRules.AllSupported).WithMessage("A label uses an unsupported language.")
            .Must(l => TranslationRules.WithinLength(l, 120)).WithMessage("A label is too long (max 120).");

        // Only a row of the operator's own needs wording: a built-in entry left blank keeps the
        // translation the app ships with, which is the point of leaving it blank.
        RuleFor(c => c.Labels)
            .Must(TranslationRules.HasEnglish)
            .When(c => c.NormalisedKey is null)
            .WithMessage("An English label is required.");

        RuleFor(c => c.Url)
            .NotEmpty().WithMessage("Enter where this link goes.")
            .MaximumLength(500)
            .Must(UpsertFooterLinkCommandValidator.BeSafe)
            .WithMessage(
                "Use a full https:// address, an anchor like #services, or an in-app path like /tools.");
    }
}

// ----------------------------------------------------------------------- Handlers

public sealed class HeaderContentHandlers :
    IRequestHandler<GetHeaderContentQuery, HeaderContentDto>,
    IRequestHandler<GetAdminHeaderContentQuery, AdminHeaderContentDto>,
    IRequestHandler<UpsertHeaderLinkCommand, AdminHeaderLinkDto>,
    IRequestHandler<DeleteHeaderLinkCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _auditLogger;

    public HeaderContentHandlers(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IAuditLogger auditLogger)
    {
        _db = db;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
    }

    public async Task<HeaderContentDto> Handle(
        GetHeaderContentQuery request,
        CancellationToken cancellationToken)
    {
        var language = _currentUser.LanguageCode;

        var links = await Ordered(_db.HeaderLinks.Where(l => l.IsActive))
            .ToListAsync(cancellationToken);

        return new HeaderContentDto(links
            .Select(l => new HeaderLinkDto(
                l.Id,
                l.Key,
                l.ResolveLabel(language),
                l.Url,
                l.Visibility.ToString(),
                l.SortOrder))
            .ToList());
    }

    public async Task<AdminHeaderContentDto> Handle(
        GetAdminHeaderContentQuery request,
        CancellationToken cancellationToken)
    {
        var links = await Ordered(_db.HeaderLinks).ToListAsync(cancellationToken);

        return new AdminHeaderContentDto(links.Select(ToAdminDto).ToList());
    }

    public async Task<AdminHeaderLinkDto> Handle(
        UpsertHeaderLinkCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        HeaderLink link;
        if (request.Id is { } id && id != Guid.Empty)
        {
            link = await _db.HeaderLinks
                .Include(l => l.Translations)
                .FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(HeaderLink), id);
        }
        else
        {
            link = new HeaderLink { Url = string.Empty };
            _db.HeaderLinks.Add(link);
        }

        var key = request.NormalisedKey;
        if (key is not null)
        {
            // The unique index would catch this, but as a 500 from the database rather than
            // something the panel can put next to the field.
            var duplicate = await _db.HeaderLinks
                .AnyAsync(other => other.Key == key && other.Id != link.Id, cancellationToken);

            if (duplicate)
            {
                throw new ConflictException(
                    "header_link.duplicate_key", $"Another header entry already uses the key '{key}'.");
            }
        }

        link.Key = key;
        // Validation has already rejected anything unparseable.
        link.Visibility = request.ParsedVisibility!.Value;
        link.Url = request.Url.Trim();
        link.SortOrder = request.SortOrder;
        link.IsActive = request.IsActive;

        ApplyLabels(link, request.Labels);

        await _db.SaveChangesAsync(cancellationToken);
        await _auditLogger.LogAsync(
            request.Id is null ? "HeaderLink.Created" : "HeaderLink.Updated",
            nameof(HeaderLink),
            link.Id,
            new { link.Key, link.Url, link.SortOrder },
            cancellationToken);

        return ToAdminDto(link);
    }

    public async Task<Unit> Handle(DeleteHeaderLinkCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var link = await _db.HeaderLinks.FirstOrDefaultAsync(l => l.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(HeaderLink), request.Id);

        _db.HeaderLinks.Remove(link);
        await _db.SaveChangesAsync(cancellationToken);
        await _auditLogger.LogAsync(
            "HeaderLink.Deleted", nameof(HeaderLink), request.Id, null, cancellationToken);

        return Unit.Value;
    }

    // --------------------------------------------------------------------- Helpers

    /// <summary>
    /// The operator's arrangement, with the creation time breaking ties so two rows given the same
    /// position keep a stable order rather than swapping between requests.
    /// </summary>
    private static IQueryable<HeaderLink> Ordered(IQueryable<HeaderLink> source) => source
        .AsNoTracking()
        .Include(l => l.Translations)
        .OrderBy(l => l.SortOrder)
        .ThenBy(l => l.CreatedAtUtc);

    private static AdminHeaderLinkDto ToAdminDto(HeaderLink link) => new(
        link.Id,
        link.Key,
        link.Translations.ToDictionary(t => t.LanguageCode, t => t.Label, StringComparer.OrdinalIgnoreCase),
        link.Url,
        link.Visibility.ToString(),
        link.SortOrder,
        link.IsActive);

    /// <summary>
    /// Replaces the overrides wholesale: a language left blank is an override removed, which is how
    /// an operator puts an entry back to the app's own wording.
    /// </summary>
    private static void ApplyLabels(HeaderLink link, IReadOnlyDictionary<string, string>? labels)
    {
        link.Translations.Clear();

        if (labels is null) return;

        foreach (var lang in LandingLanguages.All)
        {
            var label = labels
                .FirstOrDefault(kv => string.Equals(kv.Key, lang, StringComparison.OrdinalIgnoreCase))
                .Value;

            if (!string.IsNullOrWhiteSpace(label))
            {
                link.Translations.Add(new HeaderLinkTranslation
                {
                    LanguageCode = lang,
                    Label = label.Trim(),
                });
            }
        }
    }
}
