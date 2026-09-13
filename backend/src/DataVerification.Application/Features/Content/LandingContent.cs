using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Entities;
using DataVerification.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Content;

// -------------------------------------------------------------------- Public DTOs

/// <summary>A single landing feature card, resolved to one language for the public site.</summary>
public sealed record LandingFeatureDto(
    Guid Id,
    string Icon,
    string Title,
    string Body,
    int SortOrder,
    bool IsPublished);

/// <summary>One figure in the landing statistics strip, resolved to one language.</summary>
/// <param name="Icon">Icon key, or null when the figure is drawn without one.</param>
public sealed record LandingStatDto(
    Guid Id,
    string? Icon,
    string Value,
    string Label,
    int SortOrder);

/// <summary>One step in the "how it works" section, resolved to one language.</summary>
/// <remarks>
/// No number: the card's "01", "02" comes from the step's place in this list, so reordering
/// renumbers them without an operator editing any copy.
/// </remarks>
public sealed record LandingStepDto(Guid Id, string Icon, string Title, string Body, int SortOrder);

/// <summary>One body in the "trusted for verification with" strip, resolved to one language.</summary>
/// <param name="Icon">Icon key, or null to draw the strip's neutral default mark.</param>
public sealed record LandingTrustEntryDto(Guid Id, string? Icon, string Name, int SortOrder);

/// <summary>The whole "features" section in one language: heading plus the ordered cards.</summary>
public sealed record LandingContentDto(
    string Eyebrow,
    string Title,
    IReadOnlyList<LandingFeatureDto> Features,
    /// <summary>
    /// The statistics strip above the features. Empty when an administrator has published none,
    /// which the site reads as "do not draw the strip" rather than as an error.
    /// </summary>
    IReadOnlyList<LandingStatDto> Stats,
    /// <summary>The heading above the trust strip, e.g. "Trusted for verification with".</summary>
    string TrustTitle,
    /// <summary>The bodies named in the trust strip. Empty hides the whole strip, heading included.</summary>
    IReadOnlyList<LandingTrustEntryDto> TrustedBy,
    /// <summary>The eyebrow and title above the "how it works" steps.</summary>
    string HowEyebrow,
    string HowTitle,
    /// <summary>The steps themselves. Empty hides the whole section, heading included.</summary>
    IReadOnlyList<LandingStepDto> Steps,
    /// <summary><c>Grid</c> or <c>Carousel</c> — sent by name so the site reads it, not a number.</summary>
    string HowLayout,
    /// <summary>How many cards sit abreast on a wide screen. Narrow screens always stack.</summary>
    int HowColumns,
    /// <summary>The closing call to action: its heading, its line of copy and its button.</summary>
    string CtaTitle,
    string CtaBody,
    string CtaButton,
    /// <summary>Where the button goes. An in-app path, an anchor, or a full https:// address.</summary>
    string CtaLink);

// --------------------------------------------------------------------- Admin DTOs

/// <summary>A landing card with every language's copy, for the editor.</summary>
public sealed record AdminLandingFeatureDto(
    Guid Id,
    string Icon,
    IReadOnlyDictionary<string, string> Titles,
    IReadOnlyDictionary<string, string> Bodies,
    int SortOrder,
    bool IsPublished);

/// <summary>A landing statistic with every language's copy, for the editor.</summary>
public sealed record AdminLandingStatDto(
    Guid Id,
    string? Icon,
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyDictionary<string, string> Labels,
    int SortOrder,
    bool IsPublished);

/// <summary>A "how it works" step with every language's copy, for the editor.</summary>
public sealed record AdminLandingStepDto(
    Guid Id,
    string Icon,
    IReadOnlyDictionary<string, string> Titles,
    IReadOnlyDictionary<string, string> Bodies,
    int SortOrder,
    bool IsPublished);

/// <summary>A trust-strip entry with every language's name, for the editor.</summary>
public sealed record AdminLandingTrustEntryDto(
    Guid Id,
    string? Icon,
    IReadOnlyDictionary<string, string> Names,
    int SortOrder,
    bool IsPublished);

/// <summary>The full editable section: the headings (per language) plus every card, figure and body.</summary>
public sealed record AdminLandingContentDto(
    IReadOnlyDictionary<string, string> Eyebrow,
    IReadOnlyDictionary<string, string> Title,
    IReadOnlyList<AdminLandingFeatureDto> Features,
    IReadOnlyList<AdminLandingStatDto> Stats,
    IReadOnlyDictionary<string, string> TrustTitle,
    IReadOnlyList<AdminLandingTrustEntryDto> TrustedBy,
    IReadOnlyDictionary<string, string> HowEyebrow,
    IReadOnlyDictionary<string, string> HowTitle,
    IReadOnlyList<AdminLandingStepDto> Steps,
    /// <summary>By name, matching the public DTO and what the editor sends back.</summary>
    string HowLayout,
    int HowColumns,
    IReadOnlyDictionary<string, string> CtaTitle,
    IReadOnlyDictionary<string, string> CtaBody,
    IReadOnlyDictionary<string, string> CtaButton,
    /// <summary>Not per language: one destination serves every locale.</summary>
    string CtaLink);

/// <summary>Setting keys and the built-in English copy used until an admin overrides it.</summary>
public static class LandingContentDefaults
{
    public const string EyebrowKeyPrefix = "landing.features.eyebrow";
    public const string TitleKeyPrefix = "landing.features.title";

    public const string TrustTitleKeyPrefix = "landing.trust.title";
    public const string HowEyebrowKeyPrefix = "landing.how.eyebrow";
    public const string HowTitleKeyPrefix = "landing.how.title";
    public const string CtaTitleKeyPrefix = "landing.cta.title";
    public const string CtaBodyKeyPrefix = "landing.cta.body";
    public const string CtaButtonKeyPrefix = "landing.cta.button";

    /// <summary>
    /// How the steps are arranged. Not per language — a layout choice is the same in every locale,
    /// so these two are single settings rather than one per language like the copy above.
    /// </summary>
    public const string HowLayoutKey = "landing.how.layout";
    public const string HowColumnsKey = "landing.how.columns";

    /// <summary>One destination for every language, so it is a single setting like the layout.</summary>
    public const string CtaLinkKey = "landing.cta.link";

    /// <summary>What the section looked like before it was configurable.</summary>
    public const LandingSectionLayout DefaultLayout = LandingSectionLayout.Grid;
    public const int DefaultColumns = 3;

    /// <summary>More than four cards abreast leaves no room for the body text under each.</summary>
    public const int MinColumns = 1;
    public const int MaxColumns = 4;

    public const string Eyebrow = "Built for peace of mind";
    public const string Title = "Everything about your verification, in the open";
    public const string TrustTitle = "Trusted for verification with";
    public const string HowEyebrow = "How it works";
    public const string HowTitle = "From email to verified document in three steps";
    public const string CtaTitle = "Your documents, verified by the source.";
    public const string CtaBody =
        "Start with just your email address. You will receive your order credentials within a minute.";
    public const string CtaButton = "Start verification now";

    /// <summary>Where the button went before it was configurable.</summary>
    public const string CtaLink = "/register";

    public static string EyebrowKey(string languageCode) => $"{EyebrowKeyPrefix}.{languageCode}";
    public static string TitleKey(string languageCode) => $"{TitleKeyPrefix}.{languageCode}";
    public static string TrustTitleKey(string languageCode) => $"{TrustTitleKeyPrefix}.{languageCode}";
    public static string HowEyebrowKey(string languageCode) => $"{HowEyebrowKeyPrefix}.{languageCode}";
    public static string HowTitleKey(string languageCode) => $"{HowTitleKeyPrefix}.{languageCode}";
    public static string CtaTitleKey(string languageCode) => $"{CtaTitleKeyPrefix}.{languageCode}";
    public static string CtaBodyKey(string languageCode) => $"{CtaBodyKeyPrefix}.{languageCode}";
    public static string CtaButtonKey(string languageCode) => $"{CtaButtonKeyPrefix}.{languageCode}";
}

// ------------------------------------------------------------------------ Queries

/// <summary>Public read: published cards plus the heading, resolved to the caller's language.</summary>
public sealed record GetLandingContentQuery : IRequest<LandingContentDto>;

/// <summary>Admin read: every card (including unpublished) with all translations, plus the heading.</summary>
public sealed record GetAdminLandingContentQuery : IRequest<AdminLandingContentDto>;

// ----------------------------------------------------------------------- Commands

public sealed record UpsertLandingFeatureCommand(
    Guid? Id,
    string Icon,
    Dictionary<string, string> Titles,
    Dictionary<string, string> Bodies,
    int SortOrder,
    bool IsPublished) : IRequest<AdminLandingFeatureDto>;

public sealed record DeleteLandingFeatureCommand(Guid Id) : IRequest<Unit>;

public sealed record UpsertLandingStatCommand(
    Guid? Id,
    string? Icon,
    Dictionary<string, string> Values,
    Dictionary<string, string> Labels,
    int SortOrder,
    bool IsPublished) : IRequest<AdminLandingStatDto>;

public sealed record DeleteLandingStatCommand(Guid Id) : IRequest<Unit>;

public sealed record UpsertLandingTrustEntryCommand(
    Guid? Id,
    string? Icon,
    Dictionary<string, string> Names,
    int SortOrder,
    bool IsPublished) : IRequest<AdminLandingTrustEntryDto>;

public sealed record DeleteLandingTrustEntryCommand(Guid Id) : IRequest<Unit>;

public sealed record UpsertLandingStepCommand(
    Guid? Id,
    string Icon,
    Dictionary<string, string> Titles,
    Dictionary<string, string> Bodies,
    int SortOrder,
    bool IsPublished) : IRequest<AdminLandingStepDto>;

public sealed record DeleteLandingStepCommand(Guid Id) : IRequest<Unit>;

/// <summary>How the "how it works" cards are arranged. Not per language.</summary>
/// <param name="Layout">
/// <c>Grid</c> or <c>Carousel</c>, by name rather than by number. The rest of this API takes enums
/// as integers, but a layout is read and written by people editing a page — and the public DTO
/// already reports it by name, so both directions now say the same word.
/// </param>
public sealed record UpdateLandingStepsLayoutCommand(string Layout, int Columns) : IRequest<Unit>
{
    /// <summary>The parsed layout, or the default when the name is not one this build knows.</summary>
    public LandingSectionLayout ParsedLayout =>
        Enum.TryParse<LandingSectionLayout>(Layout, ignoreCase: true, out var parsed)
            ? parsed
            : LandingContentDefaults.DefaultLayout;
}

public sealed class UpdateLandingStepsLayoutCommandValidator
    : AbstractValidator<UpdateLandingStepsLayoutCommand>
{
    public UpdateLandingStepsLayoutCommandValidator()
    {
        RuleFor(c => c.Layout)
            .Must(value => Enum.TryParse<LandingSectionLayout>(value, ignoreCase: true, out _))
            .WithMessage(
                $"Choose one of: {string.Join(", ", Enum.GetNames<LandingSectionLayout>())}.");

        RuleFor(c => c.Columns)
            .InclusiveBetween(LandingContentDefaults.MinColumns, LandingContentDefaults.MaxColumns)
            .WithMessage(
                $"Choose between {LandingContentDefaults.MinColumns} and "
                + $"{LandingContentDefaults.MaxColumns} cards per row.");
    }
}

/// <summary>
/// Updates whichever headings are supplied.
/// </summary>
/// <remarks>
/// Every field is optional and null means "leave this one alone". Each heading is edited on the
/// page of the section it belongs to now, so a page sends only the copy it owns — the trust page
/// must not have to echo the features heading back just to save its own, and an older client that
/// knows nothing of a heading must not blank it by omission.
/// </remarks>
public sealed record UpdateLandingHeadingCommand(
    Dictionary<string, string>? Eyebrow = null,
    Dictionary<string, string>? Title = null,
    Dictionary<string, string>? TrustTitle = null,
    Dictionary<string, string>? HowEyebrow = null,
    Dictionary<string, string>? HowTitle = null,
    Dictionary<string, string>? CtaTitle = null,
    Dictionary<string, string>? CtaBody = null,
    Dictionary<string, string>? CtaButton = null,
    /// <summary>Null leaves the stored destination alone, like every field here.</summary>
    string? CtaLink = null) : IRequest<Unit>;

// --------------------------------------------------------------------- Validation

/// <summary>Shared rules for the per-language dictionaries the landing editor submits.</summary>
internal static class TranslationRules
{
    /// <summary>English is the guaranteed fallback, so it must always be present and non-empty.</summary>
    public static bool HasEnglish(IReadOnlyDictionary<string, string>? map) =>
        map is not null
        && map.Any(kv =>
            string.Equals(kv.Key, LandingLanguages.Default, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(kv.Value));

    public static bool AllSupported(IReadOnlyDictionary<string, string>? map) =>
        map is null || map.Keys.All(LandingLanguages.IsSupported);

    public static bool WithinLength(IReadOnlyDictionary<string, string>? map, int max) =>
        map is null || map.Values.All(v => (v ?? string.Empty).Length <= max);
}

public sealed class UpsertLandingFeatureCommandValidator : AbstractValidator<UpsertLandingFeatureCommand>
{
    public UpsertLandingFeatureCommandValidator()
    {
        RuleFor(c => c.Icon).NotEmpty().MaximumLength(50);
        RuleFor(c => c.SortOrder).GreaterThanOrEqualTo(0);

        RuleFor(c => c.Titles)
            .Must(TranslationRules.HasEnglish).WithMessage("An English title is required.")
            .Must(TranslationRules.AllSupported).WithMessage("A title uses an unsupported language.")
            .Must(t => TranslationRules.WithinLength(t, 200)).WithMessage("A title is too long (max 200).");

        RuleFor(c => c.Bodies)
            .Must(TranslationRules.HasEnglish).WithMessage("An English body is required.")
            .Must(TranslationRules.AllSupported).WithMessage("A body uses an unsupported language.")
            .Must(b => TranslationRules.WithinLength(b, 1000)).WithMessage("A body is too long (max 1000).");
    }
}

public sealed class UpsertLandingStatCommandValidator : AbstractValidator<UpsertLandingStatCommand>
{
    public UpsertLandingStatCommandValidator()
    {
        RuleFor(c => c.SortOrder).GreaterThanOrEqualTo(0);

        // Optional, and only length-checked: the site falls back to drawing no glyph for a key it
        // does not recognise, so an unknown one costs an icon rather than the whole figure.
        RuleFor(c => c.Icon).MaximumLength(50);

        RuleFor(c => c.Values)
            .Must(TranslationRules.HasEnglish).WithMessage("An English figure is required.")
            .Must(TranslationRules.AllSupported).WithMessage("A figure uses an unsupported language.")
            .Must(v => TranslationRules.WithinLength(v, 50)).WithMessage("A figure is too long (max 50).");

        RuleFor(c => c.Labels)
            .Must(TranslationRules.HasEnglish).WithMessage("An English label is required.")
            .Must(TranslationRules.AllSupported).WithMessage("A label uses an unsupported language.")
            .Must(l => TranslationRules.WithinLength(l, 200)).WithMessage("A label is too long (max 200).");
    }
}

public sealed class UpsertLandingStepCommandValidator : AbstractValidator<UpsertLandingStepCommand>
{
    public UpsertLandingStepCommandValidator()
    {
        RuleFor(c => c.Icon).NotEmpty().MaximumLength(50);
        RuleFor(c => c.SortOrder).GreaterThanOrEqualTo(0);

        RuleFor(c => c.Titles)
            .Must(TranslationRules.HasEnglish).WithMessage("An English title is required.")
            .Must(TranslationRules.AllSupported).WithMessage("A title uses an unsupported language.")
            .Must(t => TranslationRules.WithinLength(t, 200)).WithMessage("A title is too long (max 200).");

        RuleFor(c => c.Bodies)
            .Must(TranslationRules.HasEnglish).WithMessage("An English body is required.")
            .Must(TranslationRules.AllSupported).WithMessage("A body uses an unsupported language.")
            .Must(b => TranslationRules.WithinLength(b, 1000)).WithMessage("A body is too long (max 1000).");
    }
}

public sealed class UpsertLandingTrustEntryCommandValidator
    : AbstractValidator<UpsertLandingTrustEntryCommand>
{
    public UpsertLandingTrustEntryCommandValidator()
    {
        RuleFor(c => c.SortOrder).GreaterThanOrEqualTo(0);
        RuleFor(c => c.Icon).MaximumLength(50);

        RuleFor(c => c.Names)
            .Must(TranslationRules.HasEnglish).WithMessage("An English name is required.")
            .Must(TranslationRules.AllSupported).WithMessage("A name uses an unsupported language.")
            .Must(n => TranslationRules.WithinLength(n, 200)).WithMessage("A name is too long (max 200).");
    }
}

public sealed class UpdateLandingHeadingCommandValidator : AbstractValidator<UpdateLandingHeadingCommand>
{
    public UpdateLandingHeadingCommandValidator()
    {
        // Each is checked only when sent. Omitting one means "leave the stored heading alone",
        // which is what lets a page save its own copy without echoing the others back.
        RuleFor(c => c.Eyebrow!)
            .Must(TranslationRules.HasEnglish).WithMessage("An English eyebrow is required.")
            .Must(TranslationRules.AllSupported).WithMessage("The eyebrow uses an unsupported language.")
            .Must(e => TranslationRules.WithinLength(e, 200)).WithMessage("An eyebrow is too long (max 200).")
            .When(c => c.Eyebrow is not null);

        RuleFor(c => c.Title!)
            .Must(TranslationRules.HasEnglish).WithMessage("An English title is required.")
            .Must(TranslationRules.AllSupported).WithMessage("The title uses an unsupported language.")
            .Must(t => TranslationRules.WithinLength(t, 200)).WithMessage("A title is too long (max 200).")
            .When(c => c.Title is not null);

        RuleFor(c => c.TrustTitle!)
            .Must(TranslationRules.HasEnglish).WithMessage("An English trust heading is required.")
            .Must(TranslationRules.AllSupported).WithMessage("The trust heading uses an unsupported language.")
            .Must(t => TranslationRules.WithinLength(t, 200)).WithMessage("A trust heading is too long (max 200).")
            .When(c => c.TrustTitle is not null);

        RuleFor(c => c.HowEyebrow!)
            .Must(TranslationRules.HasEnglish).WithMessage("An English eyebrow is required.")
            .Must(TranslationRules.AllSupported).WithMessage("The eyebrow uses an unsupported language.")
            .Must(e => TranslationRules.WithinLength(e, 200)).WithMessage("An eyebrow is too long (max 200).")
            .When(c => c.HowEyebrow is not null);

        RuleFor(c => c.HowTitle!)
            .Must(TranslationRules.HasEnglish).WithMessage("An English title is required.")
            .Must(TranslationRules.AllSupported).WithMessage("The title uses an unsupported language.")
            .Must(t => TranslationRules.WithinLength(t, 200)).WithMessage("A title is too long (max 200).")
            .When(c => c.HowTitle is not null);
        RuleFor(c => c.CtaTitle!)
            .Must(TranslationRules.HasEnglish).WithMessage("An English call-to-action title is required.")
            .Must(TranslationRules.AllSupported).WithMessage("The title uses an unsupported language.")
            .Must(t => TranslationRules.WithinLength(t, 200)).WithMessage("The title is too long (max 200).")
            .When(c => c.CtaTitle is not null);

        RuleFor(c => c.CtaBody!)
            .Must(TranslationRules.HasEnglish).WithMessage("English call-to-action copy is required.")
            .Must(TranslationRules.AllSupported).WithMessage("The copy uses an unsupported language.")
            .Must(b => TranslationRules.WithinLength(b, 400)).WithMessage("The copy is too long (max 400).")
            .When(c => c.CtaBody is not null);

        RuleFor(c => c.CtaButton!)
            .Must(TranslationRules.HasEnglish).WithMessage("An English button label is required.")
            .Must(TranslationRules.AllSupported).WithMessage("The label uses an unsupported language.")
            .Must(b => TranslationRules.WithinLength(b, 60)).WithMessage("The label is too long (max 60).")
            .When(c => c.CtaButton is not null);

        // The same rule the footer links use: a button nobody reads before clicking is exactly
        // where javascript: and data: addresses do damage.
        RuleFor(c => c.CtaLink!)
            .NotEmpty().WithMessage("Enter where the button goes.")
            .MaximumLength(500)
            .Must(UpsertFooterLinkCommandValidator.BeSafe)
            .WithMessage(
                "Use a full https:// address, an anchor like #services, or an in-app path like /register.")
            .When(c => c.CtaLink is not null);
    }
}

// ------------------------------------------------------------------------ Handlers

public sealed class LandingContentHandlers :
    IRequestHandler<GetLandingContentQuery, LandingContentDto>,
    IRequestHandler<GetAdminLandingContentQuery, AdminLandingContentDto>,
    IRequestHandler<UpsertLandingFeatureCommand, AdminLandingFeatureDto>,
    IRequestHandler<DeleteLandingFeatureCommand, Unit>,
    IRequestHandler<UpsertLandingStatCommand, AdminLandingStatDto>,
    IRequestHandler<DeleteLandingStatCommand, Unit>,
    IRequestHandler<UpsertLandingTrustEntryCommand, AdminLandingTrustEntryDto>,
    IRequestHandler<DeleteLandingTrustEntryCommand, Unit>,
    IRequestHandler<UpsertLandingStepCommand, AdminLandingStepDto>,
    IRequestHandler<DeleteLandingStepCommand, Unit>,
    IRequestHandler<UpdateLandingStepsLayoutCommand, Unit>,
    IRequestHandler<UpdateLandingHeadingCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _auditLogger;

    public LandingContentHandlers(IApplicationDbContext db, ICurrentUser currentUser, IAuditLogger auditLogger)
    {
        _db = db;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
    }

    public async Task<LandingContentDto> Handle(GetLandingContentQuery request, CancellationToken cancellationToken)
    {
        var language = _currentUser.LanguageCode;

        var features = await _db.LandingFeatures
            .AsNoTracking()
            .Where(f => f.IsPublished)
            .Include(f => f.Translations)
            .OrderBy(f => f.SortOrder)
            .ThenBy(f => f.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var stats = await _db.LandingStats
            .AsNoTracking()
            .Where(s => s.IsPublished)
            .Include(s => s.Translations)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var trusted = await _db.LandingTrustEntries
            .AsNoTracking()
            .Where(e => e.IsPublished)
            .Include(e => e.Translations)
            .OrderBy(e => e.SortOrder)
            .ThenBy(e => e.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var steps = await _db.LandingSteps
            .AsNoTracking()
            .Where(s => s.IsPublished)
            .Include(s => s.Translations)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var heading = await LoadHeadingAsync(cancellationToken);
        var stepsLayout = await LoadStepsLayoutAsync(cancellationToken);
        var ctaLink = await LoadCtaLinkAsync(cancellationToken);

        return new LandingContentDto(
            ResolveSetting(heading.Eyebrow, language, LandingContentDefaults.Eyebrow),
            ResolveSetting(heading.Title, language, LandingContentDefaults.Title),
            features
                .Select(f => new LandingFeatureDto(
                    f.Id,
                    f.Icon,
                    f.ResolveTitle(language),
                    f.ResolveBody(language),
                    f.SortOrder,
                    f.IsPublished))
                .ToList(),
            stats
                .Select(s => new LandingStatDto(
                    s.Id,
                    string.IsNullOrWhiteSpace(s.Icon) ? null : s.Icon,
                    s.ResolveValue(language),
                    s.ResolveLabel(language),
                    s.SortOrder))
                .ToList(),
            ResolveSetting(heading.TrustTitle, language, LandingContentDefaults.TrustTitle),
            trusted
                .Select(e => new LandingTrustEntryDto(
                    e.Id,
                    string.IsNullOrWhiteSpace(e.Icon) ? null : e.Icon,
                    e.ResolveName(language),
                    e.SortOrder))
                .ToList(),
            ResolveSetting(heading.HowEyebrow, language, LandingContentDefaults.HowEyebrow),
            ResolveSetting(heading.HowTitle, language, LandingContentDefaults.HowTitle),
            steps
                .Select(s => new LandingStepDto(
                    s.Id,
                    s.Icon,
                    s.ResolveTitle(language),
                    s.ResolveBody(language),
                    s.SortOrder))
                .ToList(),
            stepsLayout.Layout.ToString(),
            stepsLayout.Columns,
            ResolveSetting(heading.CtaTitle, language, LandingContentDefaults.CtaTitle),
            ResolveSetting(heading.CtaBody, language, LandingContentDefaults.CtaBody),
            ResolveSetting(heading.CtaButton, language, LandingContentDefaults.CtaButton),
            ctaLink);
    }

    public async Task<AdminLandingContentDto> Handle(GetAdminLandingContentQuery request, CancellationToken cancellationToken)
    {
        var features = await _db.LandingFeatures
            .AsNoTracking()
            .Include(f => f.Translations)
            .OrderBy(f => f.SortOrder)
            .ThenBy(f => f.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var stats = await _db.LandingStats
            .AsNoTracking()
            .Include(s => s.Translations)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var trusted = await _db.LandingTrustEntries
            .AsNoTracking()
            .Include(e => e.Translations)
            .OrderBy(e => e.SortOrder)
            .ThenBy(e => e.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var steps = await _db.LandingSteps
            .AsNoTracking()
            .Include(s => s.Translations)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var heading = await LoadHeadingAsync(cancellationToken);
        var stepsLayout = await LoadStepsLayoutAsync(cancellationToken);
        var ctaLink = await LoadCtaLinkAsync(cancellationToken);

        return new AdminLandingContentDto(
            heading.Eyebrow,
            heading.Title,
            features
                .Select(f => new AdminLandingFeatureDto(
                    f.Id,
                    f.Icon,
                    f.Translations.ToDictionary(t => t.LanguageCode, t => t.Title, StringComparer.OrdinalIgnoreCase),
                    f.Translations.ToDictionary(t => t.LanguageCode, t => t.Body, StringComparer.OrdinalIgnoreCase),
                    f.SortOrder,
                    f.IsPublished))
                .ToList(),
            stats
                .Select(s => new AdminLandingStatDto(
                    s.Id,
                    s.Icon,
                    s.Translations.ToDictionary(t => t.LanguageCode, t => t.Value, StringComparer.OrdinalIgnoreCase),
                    s.Translations.ToDictionary(t => t.LanguageCode, t => t.Label, StringComparer.OrdinalIgnoreCase),
                    s.SortOrder,
                    s.IsPublished))
                .ToList(),
            heading.TrustTitle,
            trusted
                .Select(e => new AdminLandingTrustEntryDto(
                    e.Id,
                    e.Icon,
                    e.Translations.ToDictionary(t => t.LanguageCode, t => t.Name, StringComparer.OrdinalIgnoreCase),
                    e.SortOrder,
                    e.IsPublished))
                .ToList(),
            heading.HowEyebrow,
            heading.HowTitle,
            steps
                .Select(s => new AdminLandingStepDto(
                    s.Id,
                    s.Icon,
                    s.Translations.ToDictionary(t => t.LanguageCode, t => t.Title, StringComparer.OrdinalIgnoreCase),
                    s.Translations.ToDictionary(t => t.LanguageCode, t => t.Body, StringComparer.OrdinalIgnoreCase),
                    s.SortOrder,
                    s.IsPublished))
                .ToList(),
            stepsLayout.Layout.ToString(),
            stepsLayout.Columns,
            heading.CtaTitle,
            heading.CtaBody,
            heading.CtaButton,
            ctaLink);
    }

    public async Task<AdminLandingStepDto> Handle(
        UpsertLandingStepCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        LandingStep step;
        if (request.Id is { } id)
        {
            step = await _db.LandingSteps
                .Include(s => s.Translations)
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                ?? throw new Common.Exceptions.NotFoundException(nameof(LandingStep), id);
        }
        else
        {
            step = new LandingStep { Icon = request.Icon.Trim() };
            _db.LandingSteps.Add(step);
        }

        step.Icon = request.Icon.Trim();
        step.SortOrder = request.SortOrder;
        step.IsPublished = request.IsPublished;

        var desired = BuildTranslations(request.Titles, request.Bodies);

        step.Translations.Clear();
        foreach (var (lang, value) in desired)
        {
            step.Translations.Add(new LandingStepTranslation
            {
                LanguageCode = lang,
                Title = value.Title,
                Body = value.Body,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _auditLogger.LogAsync(
            request.Id is null ? "LandingStep.Created" : "LandingStep.Updated",
            nameof(LandingStep),
            step.Id,
            new { Title = desired.GetValueOrDefault(LandingLanguages.Default).Title },
            cancellationToken);

        return new AdminLandingStepDto(
            step.Id,
            step.Icon,
            desired.ToDictionary(kv => kv.Key, kv => kv.Value.Title, StringComparer.OrdinalIgnoreCase),
            desired.ToDictionary(kv => kv.Key, kv => kv.Value.Body, StringComparer.OrdinalIgnoreCase),
            step.SortOrder,
            step.IsPublished);
    }

    public async Task<Unit> Handle(DeleteLandingStepCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var step = await _db.LandingSteps.FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new Common.Exceptions.NotFoundException(nameof(LandingStep), request.Id);

        _db.LandingSteps.Remove(step);
        await _db.SaveChangesAsync(cancellationToken);
        await _auditLogger.LogAsync("LandingStep.Deleted", nameof(LandingStep), request.Id, null, cancellationToken);

        return Unit.Value;
    }

    public async Task<Unit> Handle(
        UpdateLandingStepsLayoutCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await ApplyHeadingLanguageAsync(
            LandingContentDefaults.HowLayoutKey, request.ParsedLayout.ToString(), cancellationToken);
        await ApplyHeadingLanguageAsync(
            LandingContentDefaults.HowColumnsKey,
            request.Columns.ToString(System.Globalization.CultureInfo.InvariantCulture),
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        await _auditLogger.LogAsync(
            "LandingStepsLayout.Updated",
            nameof(SiteSetting),
            null,
            new { Layout = request.ParsedLayout.ToString(), request.Columns },
            cancellationToken);

        return Unit.Value;
    }

    public async Task<AdminLandingTrustEntryDto> Handle(
        UpsertLandingTrustEntryCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        LandingTrustEntry entry;
        if (request.Id is { } id)
        {
            entry = await _db.LandingTrustEntries
                .Include(e => e.Translations)
                .FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
                ?? throw new Common.Exceptions.NotFoundException(nameof(LandingTrustEntry), id);
        }
        else
        {
            entry = new LandingTrustEntry();
            _db.LandingTrustEntries.Add(entry);
        }

        entry.Icon = string.IsNullOrWhiteSpace(request.Icon) ? null : request.Icon.Trim();
        entry.SortOrder = request.SortOrder;
        entry.IsPublished = request.IsPublished;

        // One field rather than two, so a language counts as translated on the name alone.
        var desired = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var lang in LandingLanguages.All)
        {
            var name = Lookup(request.Names, lang);
            if (!string.IsNullOrWhiteSpace(name))
            {
                desired[lang] = name!.Trim();
            }
        }

        entry.Translations.Clear();
        foreach (var (lang, name) in desired)
        {
            entry.Translations.Add(new LandingTrustEntryTranslation
            {
                LanguageCode = lang,
                Name = name,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _auditLogger.LogAsync(
            request.Id is null ? "LandingTrustEntry.Created" : "LandingTrustEntry.Updated",
            nameof(LandingTrustEntry),
            entry.Id,
            new { Name = desired.GetValueOrDefault(LandingLanguages.Default) },
            cancellationToken);

        return new AdminLandingTrustEntryDto(
            entry.Id,
            entry.Icon,
            desired,
            entry.SortOrder,
            entry.IsPublished);
    }

    public async Task<Unit> Handle(
        DeleteLandingTrustEntryCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entry = await _db.LandingTrustEntries
            .FirstOrDefaultAsync(e => e.Id == request.Id, cancellationToken)
            ?? throw new Common.Exceptions.NotFoundException(nameof(LandingTrustEntry), request.Id);

        _db.LandingTrustEntries.Remove(entry);
        await _db.SaveChangesAsync(cancellationToken);
        await _auditLogger.LogAsync(
            "LandingTrustEntry.Deleted", nameof(LandingTrustEntry), request.Id, null, cancellationToken);

        return Unit.Value;
    }

    public async Task<AdminLandingStatDto> Handle(
        UpsertLandingStatCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        LandingStat stat;
        if (request.Id is { } id)
        {
            stat = await _db.LandingStats
                .Include(s => s.Translations)
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                ?? throw new Common.Exceptions.NotFoundException(nameof(LandingStat), id);
        }
        else
        {
            stat = new LandingStat();
            _db.LandingStats.Add(stat);
        }

        // A blank choice is stored as "no icon" rather than as an empty string, so the public DTO
        // and the editor agree on what "none" looks like.
        stat.Icon = string.IsNullOrWhiteSpace(request.Icon) ? null : request.Icon.Trim();
        stat.SortOrder = request.SortOrder;
        stat.IsPublished = request.IsPublished;

        // Same rule as a feature card: a language counts as translated only when both halves are
        // filled, because a figure with no caption says nothing and a caption with no figure is
        // an empty card.
        var desired = BuildTranslations(request.Values, request.Labels);

        stat.Translations.Clear();
        foreach (var (lang, value) in desired)
        {
            stat.Translations.Add(new LandingStatTranslation
            {
                LanguageCode = lang,
                Value = value.Title,
                Label = value.Body,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _auditLogger.LogAsync(
            request.Id is null ? "LandingStat.Created" : "LandingStat.Updated",
            nameof(LandingStat),
            stat.Id,
            new { Value = desired.GetValueOrDefault(LandingLanguages.Default).Title },
            cancellationToken);

        return new AdminLandingStatDto(
            stat.Id,
            stat.Icon,
            desired.ToDictionary(kv => kv.Key, kv => kv.Value.Title, StringComparer.OrdinalIgnoreCase),
            desired.ToDictionary(kv => kv.Key, kv => kv.Value.Body, StringComparer.OrdinalIgnoreCase),
            stat.SortOrder,
            stat.IsPublished);
    }

    public async Task<Unit> Handle(DeleteLandingStatCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stat = await _db.LandingStats.FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new Common.Exceptions.NotFoundException(nameof(LandingStat), request.Id);

        _db.LandingStats.Remove(stat);
        await _db.SaveChangesAsync(cancellationToken);
        await _auditLogger.LogAsync("LandingStat.Deleted", nameof(LandingStat), request.Id, null, cancellationToken);

        return Unit.Value;
    }

    public async Task<AdminLandingFeatureDto> Handle(UpsertLandingFeatureCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        LandingFeature feature;
        if (request.Id is { } id)
        {
            feature = await _db.LandingFeatures
                .Include(f => f.Translations)
                .FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
                ?? throw new Common.Exceptions.NotFoundException(nameof(LandingFeature), id);
        }
        else
        {
            feature = new LandingFeature { Icon = request.Icon.Trim() };
            _db.LandingFeatures.Add(feature);
        }

        feature.Icon = request.Icon.Trim();
        feature.SortOrder = request.SortOrder;
        feature.IsPublished = request.IsPublished;

        // A translation row only exists for a language that has both a title and a body — the two
        // are NOT NULL, and a half-filled language is treated as "not translated yet".
        var desired = BuildTranslations(request.Titles, request.Bodies);

        feature.Translations.Clear();
        foreach (var (lang, value) in desired)
        {
            feature.Translations.Add(new LandingFeatureTranslation
            {
                LanguageCode = lang,
                Title = value.Title,
                Body = value.Body,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _auditLogger.LogAsync(
            request.Id is null ? "LandingFeature.Created" : "LandingFeature.Updated",
            nameof(LandingFeature),
            feature.Id,
            new { Title = desired.GetValueOrDefault(LandingLanguages.Default).Title },
            cancellationToken);

        return new AdminLandingFeatureDto(
            feature.Id,
            feature.Icon,
            desired.ToDictionary(kv => kv.Key, kv => kv.Value.Title, StringComparer.OrdinalIgnoreCase),
            desired.ToDictionary(kv => kv.Key, kv => kv.Value.Body, StringComparer.OrdinalIgnoreCase),
            feature.SortOrder,
            feature.IsPublished);
    }

    public async Task<Unit> Handle(DeleteLandingFeatureCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var feature = await _db.LandingFeatures.FirstOrDefaultAsync(f => f.Id == request.Id, cancellationToken)
            ?? throw new Common.Exceptions.NotFoundException(nameof(LandingFeature), request.Id);

        _db.LandingFeatures.Remove(feature);
        await _db.SaveChangesAsync(cancellationToken);
        await _auditLogger.LogAsync("LandingFeature.Deleted", nameof(LandingFeature), request.Id, null, cancellationToken);

        return Unit.Value;
    }

    public async Task<Unit> Handle(UpdateLandingHeadingCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        foreach (var lang in LandingLanguages.All)
        {
            // A heading the caller did not send is skipped entirely rather than written as blank.
            if (request.Eyebrow is not null)
            {
                await ApplyHeadingLanguageAsync(
                    LandingContentDefaults.EyebrowKey(lang),
                    Lookup(request.Eyebrow, lang),
                    cancellationToken);
            }

            if (request.Title is not null)
            {
                await ApplyHeadingLanguageAsync(
                    LandingContentDefaults.TitleKey(lang),
                    Lookup(request.Title, lang),
                    cancellationToken);
            }

            if (request.TrustTitle is not null)
            {
                await ApplyHeadingLanguageAsync(
                    LandingContentDefaults.TrustTitleKey(lang),
                    Lookup(request.TrustTitle, lang),
                    cancellationToken);
            }

            if (request.HowEyebrow is not null)
            {
                await ApplyHeadingLanguageAsync(
                    LandingContentDefaults.HowEyebrowKey(lang),
                    Lookup(request.HowEyebrow, lang),
                    cancellationToken);
            }

            if (request.HowTitle is not null)
            {
                await ApplyHeadingLanguageAsync(
                    LandingContentDefaults.HowTitleKey(lang),
                    Lookup(request.HowTitle, lang),
                    cancellationToken);
            }

            if (request.CtaTitle is not null)
            {
                await ApplyHeadingLanguageAsync(
                    LandingContentDefaults.CtaTitleKey(lang),
                    Lookup(request.CtaTitle, lang),
                    cancellationToken);
            }

            if (request.CtaBody is not null)
            {
                await ApplyHeadingLanguageAsync(
                    LandingContentDefaults.CtaBodyKey(lang),
                    Lookup(request.CtaBody, lang),
                    cancellationToken);
            }

            if (request.CtaButton is not null)
            {
                await ApplyHeadingLanguageAsync(
                    LandingContentDefaults.CtaButtonKey(lang),
                    Lookup(request.CtaButton, lang),
                    cancellationToken);
            }
        }

        // Not per language, so it sits outside the loop rather than being written ten times.
        if (request.CtaLink is not null)
        {
            await ApplyHeadingLanguageAsync(
                LandingContentDefaults.CtaLinkKey, request.CtaLink, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _auditLogger.LogAsync("LandingHeading.Updated", "SiteSetting", null, null, cancellationToken);

        return Unit.Value;
    }

    // --------------------------------------------------------------------- Helpers

    private static Dictionary<string, (string Title, string Body)> BuildTranslations(
        IReadOnlyDictionary<string, string> titles,
        IReadOnlyDictionary<string, string> bodies)
    {
        var result = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);

        foreach (var lang in LandingLanguages.All)
        {
            var title = Lookup(titles, lang);
            var body = Lookup(bodies, lang);
            if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(body))
            {
                result[lang] = (title!.Trim(), body!.Trim());
            }
        }

        return result;
    }

    /// <summary>Case-insensitive lookup, since JSON keys arrive with whatever casing the client sent.</summary>
    private static string? Lookup(IReadOnlyDictionary<string, string>? map, string lang)
    {
        if (map is null)
        {
            return null;
        }

        foreach (var kv in map)
        {
            if (string.Equals(kv.Key, lang, StringComparison.OrdinalIgnoreCase))
            {
                return kv.Value;
            }
        }

        return null;
    }

    private static string ResolveSetting(
        IReadOnlyDictionary<string, string> byLanguage,
        string language,
        string fallback)
    {
        var value = Lookup(byLanguage, language);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value!;
        }

        var english = Lookup(byLanguage, LandingLanguages.Default);
        return string.IsNullOrWhiteSpace(english) ? fallback : english!;
    }

    private async Task<HeadingCopy> LoadHeadingAsync(CancellationToken cancellationToken)
    {
        var settings = await _db.SiteSettings.AsNoTracking()
            .Where(s => s.Key.StartsWith(LandingContentDefaults.EyebrowKeyPrefix)
                        || s.Key.StartsWith(LandingContentDefaults.TitleKeyPrefix)
                        || s.Key.StartsWith(LandingContentDefaults.TrustTitleKeyPrefix)
                        || s.Key.StartsWith(LandingContentDefaults.HowEyebrowKeyPrefix)
                        || s.Key.StartsWith(LandingContentDefaults.HowTitleKeyPrefix)
                        || s.Key.StartsWith(LandingContentDefaults.CtaTitleKeyPrefix)
                        || s.Key.StartsWith(LandingContentDefaults.CtaBodyKeyPrefix)
                        || s.Key.StartsWith(LandingContentDefaults.CtaButtonKeyPrefix))
            .ToListAsync(cancellationToken);

        var eyebrow = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var title = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var trustTitle = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var howEyebrow = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var howTitle = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ctaTitle = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ctaBody = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ctaButton = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var setting in settings)
        {
            // Most specific first. The features keys are "landing.features.*" and these are
            // "landing.how.*" and "landing.trust.*", so none nests inside another today — but the
            // order is what keeps that true if one ever does.
            if (TryLanguageSuffix(setting.Key, LandingContentDefaults.HowEyebrowKeyPrefix, out var heLang))
            {
                howEyebrow[heLang] = setting.Value;
            }
            else if (TryLanguageSuffix(setting.Key, LandingContentDefaults.HowTitleKeyPrefix, out var htLang))
            {
                howTitle[htLang] = setting.Value;
            }
            else if (TryLanguageSuffix(setting.Key, LandingContentDefaults.TrustTitleKeyPrefix, out var trLang))
            {
                trustTitle[trLang] = setting.Value;
            }
            else if (TryLanguageSuffix(setting.Key, LandingContentDefaults.CtaTitleKeyPrefix, out var ctLang))
            {
                ctaTitle[ctLang] = setting.Value;
            }
            else if (TryLanguageSuffix(setting.Key, LandingContentDefaults.CtaBodyKeyPrefix, out var cbLang))
            {
                ctaBody[cbLang] = setting.Value;
            }
            else if (TryLanguageSuffix(setting.Key, LandingContentDefaults.CtaButtonKeyPrefix, out var cnLang))
            {
                ctaButton[cnLang] = setting.Value;
            }
            else if (TryLanguageSuffix(setting.Key, LandingContentDefaults.EyebrowKeyPrefix, out var eLang))
            {
                eyebrow[eLang] = setting.Value;
            }
            else if (TryLanguageSuffix(setting.Key, LandingContentDefaults.TitleKeyPrefix, out var tLang))
            {
                title[tLang] = setting.Value;
            }
        }

        return new HeadingCopy(eyebrow, title, trustTitle, howEyebrow, howTitle, ctaTitle, ctaBody, ctaButton);
    }

    private sealed record HeadingCopy(
        Dictionary<string, string> Eyebrow,
        Dictionary<string, string> Title,
        Dictionary<string, string> TrustTitle,
        Dictionary<string, string> HowEyebrow,
        Dictionary<string, string> HowTitle,
        Dictionary<string, string> CtaTitle,
        Dictionary<string, string> CtaBody,
        Dictionary<string, string> CtaButton);

    /// <summary>
    /// Where the closing call to action's button goes, falling back to the registration page it
    /// pointed at before it was configurable. A blank stored value is treated as absent rather
    /// than as a button that goes nowhere.
    /// </summary>
    private async Task<string> LoadCtaLinkAsync(CancellationToken cancellationToken)
    {
        var stored = await _db.SiteSettings
            .AsNoTracking()
            .Where(setting => setting.Key == LandingContentDefaults.CtaLinkKey)
            .Select(setting => setting.Value)
            .FirstOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(stored) ? LandingContentDefaults.CtaLink : stored;
    }

    /// <summary>
    /// How the steps are arranged, falling back to what the section looked like before it was
    /// configurable. A stored value that cannot be read is treated as absent rather than as an
    /// error: a bad row in a settings table must not take the landing page down.
    /// </summary>
    private async Task<(LandingSectionLayout Layout, int Columns)> LoadStepsLayoutAsync(
        CancellationToken cancellationToken)
    {
        var rows = await _db.SiteSettings
            .AsNoTracking()
            .Where(s => s.Key == LandingContentDefaults.HowLayoutKey
                        || s.Key == LandingContentDefaults.HowColumnsKey)
            .ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);

        var layout = Enum.TryParse<LandingSectionLayout>(
            rows.GetValueOrDefault(LandingContentDefaults.HowLayoutKey),
            ignoreCase: true,
            out var parsedLayout)
            ? parsedLayout
            : LandingContentDefaults.DefaultLayout;

        var columns = int.TryParse(
            rows.GetValueOrDefault(LandingContentDefaults.HowColumnsKey),
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsedColumns)
            && parsedColumns >= LandingContentDefaults.MinColumns
            && parsedColumns <= LandingContentDefaults.MaxColumns
            ? parsedColumns
            : LandingContentDefaults.DefaultColumns;

        return (layout, columns);
    }

    /// <summary>Splits "landing.features.eyebrow.ar" into its trailing language code.</summary>
    private static bool TryLanguageSuffix(string key, string prefix, out string language)
    {
        language = string.Empty;
        var withDot = prefix + ".";
        if (!key.StartsWith(withDot, StringComparison.Ordinal))
        {
            return false;
        }

        language = key[withDot.Length..];
        return language.Length > 0 && !language.Contains('.');
    }

    /// <summary>Upserts a non-empty heading value, or removes the row when the language is cleared.</summary>
    private async Task ApplyHeadingLanguageAsync(string key, string? value, CancellationToken cancellationToken)
    {
        var setting = await _db.SiteSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);

        if (string.IsNullOrWhiteSpace(value))
        {
            if (setting is not null)
            {
                _db.SiteSettings.Remove(setting);
            }

            return;
        }

        if (setting is null)
        {
            _db.SiteSettings.Add(new SiteSetting { Key = key, Value = value.Trim() });
        }
        else
        {
            setting.Value = value.Trim();
        }
    }
}
