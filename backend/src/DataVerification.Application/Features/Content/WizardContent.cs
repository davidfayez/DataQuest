using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Content;

// -------------------------------------------------------------------- Public DTOs

/// <summary>
/// One step's heading, resolved to the language that was asked for.
///
/// Either field may be <c>null</c>, which means nobody has written that step's copy in that
/// language and the wizard should draw its own built-in wording instead.
/// </summary>
public sealed record WizardStepHeadingDto(string? Title, string? Subtitle);

/// <summary>The wizard's editable headings, keyed by step.</summary>
public sealed record WizardContentDto(IReadOnlyDictionary<string, WizardStepHeadingDto> Steps);

// --------------------------------------------------------------------- Admin DTOs

/// <summary>One step's heading in every language it has been written in.</summary>
public sealed record AdminWizardStepHeadingDto(
    string Step,
    IReadOnlyDictionary<string, string> Title,
    IReadOnlyDictionary<string, string> Subtitle);

public sealed record AdminWizardContentDto(IReadOnlyList<AdminWizardStepHeadingDto> Steps);

/// <summary>
/// The steps of the applicant's New application wizard whose heading an administrator can rewrite,
/// and the setting keys that hold it.
///
/// The document and review steps are deliberately absent: their copy carries a placeholder for the
/// upload limit, which free text could silently drop.
/// </summary>
public static class WizardContentSteps
{
    public const string Addressee = "addressee";
    public const string Personal = "personal";
    public const string Details = "details";
    public const string Summary = "summary";

    /// <summary>Every key starts here, which is what makes one query enough to load the lot.</summary>
    public const string KeyPrefix = "wizard.step.";

    public const string TitleField = "title";
    public const string SubtitleField = "subtitle";

    public static readonly IReadOnlyList<string> All = [Addressee, Personal, Details, Summary];

    public static bool IsKnown(string? step) =>
        step is not null && All.Contains(step, StringComparer.OrdinalIgnoreCase);

    public static string TitleKey(string step, string languageCode) =>
        $"{KeyPrefix}{step}.{TitleField}.{languageCode}";

    public static string SubtitleKey(string step, string languageCode) =>
        $"{KeyPrefix}{step}.{SubtitleField}.{languageCode}";
}

/// <summary>
/// The wording the wizard ships with, seeded so the panel opens on the real copy rather than on
/// empty boxes.
///
/// English and Arabic only. Those are the two the platform authors itself; every other locale keeps
/// the translation bundled with the web app until somebody writes one here.
/// </summary>
public static class WizardContentDefaults
{
    public static IReadOnlyList<(string Key, string Value)> SeedRows() =>
    [
        (WizardContentSteps.TitleKey(WizardContentSteps.Addressee, "en"),
            "Who is this request addressed to?"),
        (WizardContentSteps.SubtitleKey(WizardContentSteps.Addressee, "en"),
            "Name the body that will receive the verification result."),
        (WizardContentSteps.TitleKey(WizardContentSteps.Addressee, "ar"),
            "إلى من يوجَّه هذا الطلب؟"),
        (WizardContentSteps.SubtitleKey(WizardContentSteps.Addressee, "ar"),
            "حدّد الجهة التي ستستلم نتيجة التحقق."),

        (WizardContentSteps.TitleKey(WizardContentSteps.Personal, "en"), "Personal details"),
        (WizardContentSteps.SubtitleKey(WizardContentSteps.Personal, "en"),
            "Enter the applicant's name exactly as it appears on their documents, in both Arabic and English."),
        (WizardContentSteps.TitleKey(WizardContentSteps.Personal, "ar"), "البيانات الشخصية"),
        (WizardContentSteps.SubtitleKey(WizardContentSteps.Personal, "ar"),
            "أدخل اسم مقدّم الطلب كما هو مكتوب في مستنداته، بالعربية والإنجليزية معًا."),

        (WizardContentSteps.TitleKey(WizardContentSteps.Details, "en"), "What do you need verified?"),
        (WizardContentSteps.SubtitleKey(WizardContentSteps.Details, "en"),
            "Each choice narrows down the next one."),
        (WizardContentSteps.TitleKey(WizardContentSteps.Details, "ar"), "ما الذي تريد التحقق منه؟"),
        (WizardContentSteps.SubtitleKey(WizardContentSteps.Details, "ar"),
            "كل اختيار يحدّد الخيارات التالية."),

        (WizardContentSteps.TitleKey(WizardContentSteps.Summary, "en"), "Services summary"),
        (WizardContentSteps.SubtitleKey(WizardContentSteps.Summary, "en"),
            "Prices are confirmed by the server; this is what you will be charged."),
        (WizardContentSteps.TitleKey(WizardContentSteps.Summary, "ar"), "ملخص الخدمات"),
        (WizardContentSteps.SubtitleKey(WizardContentSteps.Summary, "ar"),
            "الأسعار معتمدة من الخادم، وهي المبالغ التي ستُحتسب عليك."),
    ];
}

// ------------------------------------------------------------------------ Queries

public sealed record GetWizardContentQuery : IRequest<WizardContentDto>;

public sealed record GetAdminWizardContentQuery : IRequest<AdminWizardContentDto>;

// ----------------------------------------------------------------------- Commands

/// <summary>
/// Rewrites one step's heading. Both fields are optional — an omitted one is left alone, so the two
/// boxes on the panel can be saved together without either clearing the other.
///
/// A language sent blank removes that override, and the step falls back to the wizard's own wording
/// for that language.
/// </summary>
public sealed record UpdateWizardStepHeadingCommand(
    string Step,
    Dictionary<string, string>? Title = null,
    Dictionary<string, string>? Subtitle = null) : IRequest<Unit>;

// --------------------------------------------------------------------- Validation

public sealed class UpdateWizardStepHeadingCommandValidator
    : AbstractValidator<UpdateWizardStepHeadingCommand>
{
    public UpdateWizardStepHeadingCommandValidator()
    {
        RuleFor(c => c.Step)
            .Must(WizardContentSteps.IsKnown)
            .WithMessage(
                $"Choose one of the wizard's steps: {string.Join(", ", WizardContentSteps.All)}.");

        RuleFor(c => c.Title)
            .Must(TranslationRules.AllSupported).WithMessage("The title uses an unsupported language.")
            .Must(t => TranslationRules.WithinLength(t, 200))
            .WithMessage("The title is too long (max 200).");

        RuleFor(c => c.Subtitle)
            .Must(TranslationRules.AllSupported)
            .WithMessage("The subtitle uses an unsupported language.")
            .Must(s => TranslationRules.WithinLength(s, 400))
            .WithMessage("The subtitle is too long (max 400).");
    }
}

// ----------------------------------------------------------------------- Handlers

public sealed class WizardContentHandlers :
    IRequestHandler<GetWizardContentQuery, WizardContentDto>,
    IRequestHandler<GetAdminWizardContentQuery, AdminWizardContentDto>,
    IRequestHandler<UpdateWizardStepHeadingCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _auditLogger;

    public WizardContentHandlers(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IAuditLogger auditLogger)
    {
        _db = db;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
    }

    public async Task<WizardContentDto> Handle(
        GetWizardContentQuery request,
        CancellationToken cancellationToken)
    {
        var language = _currentUser.LanguageCode;
        var copy = await LoadCopyAsync(cancellationToken);

        return new WizardContentDto(WizardContentSteps.All.ToDictionary(
            step => step,
            step => new WizardStepHeadingDto(
                Resolve(copy[step].Title, language),
                Resolve(copy[step].Subtitle, language)),
            StringComparer.OrdinalIgnoreCase));
    }

    public async Task<AdminWizardContentDto> Handle(
        GetAdminWizardContentQuery request,
        CancellationToken cancellationToken)
    {
        var copy = await LoadCopyAsync(cancellationToken);

        return new AdminWizardContentDto(WizardContentSteps.All
            .Select(step => new AdminWizardStepHeadingDto(step, copy[step].Title, copy[step].Subtitle))
            .ToList());
    }

    public async Task<Unit> Handle(
        UpdateWizardStepHeadingCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Validation has already refused anything else, so the stored keys keep one spelling.
        var step = WizardContentSteps.All.First(
            s => string.Equals(s, request.Step, StringComparison.OrdinalIgnoreCase));

        foreach (var lang in LandingLanguages.All)
        {
            await ApplyAsync(request.Title, WizardContentSteps.TitleKey(step, lang), lang);
            await ApplyAsync(request.Subtitle, WizardContentSteps.SubtitleKey(step, lang), lang);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _auditLogger.LogAsync(
            "WizardStepHeading.Updated", nameof(SiteSetting), null, new { Step = step }, cancellationToken);

        return Unit.Value;

        async Task ApplyAsync(Dictionary<string, string>? map, string key, string lang)
        {
            // A field the caller did not send is skipped rather than written as blank.
            if (map is null) return;

            var value = Lookup(map, lang);
            var row = await _db.SiteSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);

            // Blank means "use the wizard's own wording again", so the row goes rather than being
            // stored as an empty heading.
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

    private async Task<Dictionary<string, StepCopy>> LoadCopyAsync(CancellationToken cancellationToken)
    {
        var settings = await _db.SiteSettings
            .AsNoTracking()
            .Where(s => s.Key.StartsWith(WizardContentSteps.KeyPrefix))
            .ToListAsync(cancellationToken);

        var copy = WizardContentSteps.All.ToDictionary(
            step => step,
            _ => new StepCopy(New(), New()),
            StringComparer.OrdinalIgnoreCase);

        foreach (var setting in settings)
        {
            if (!TryParseKey(setting.Key, out var step, out var field, out var language)) continue;

            if (string.Equals(field, WizardContentSteps.TitleField, StringComparison.OrdinalIgnoreCase))
            {
                copy[step].Title[language] = setting.Value;
            }
            else if (string.Equals(
                field, WizardContentSteps.SubtitleField, StringComparison.OrdinalIgnoreCase))
            {
                copy[step].Subtitle[language] = setting.Value;
            }
        }

        return copy;

        static Dictionary<string, string> New() => new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record StepCopy(Dictionary<string, string> Title, Dictionary<string, string> Subtitle);

    /// <summary>
    /// Splits "wizard.step.addressee.subtitle.ar" into the step, the field and the language.
    /// Reading the parts rather than matching prefixes keeps "title" and any later key that starts
    /// with it from being mistaken for one another.
    /// </summary>
    private static bool TryParseKey(string key, out string step, out string field, out string language)
    {
        step = field = language = string.Empty;

        var parts = key.Split('.');
        if (parts.Length != 5 || parts[0] != "wizard" || parts[1] != "step") return false;

        (step, field, language) = (parts[2], parts[3], parts[4]);
        return WizardContentSteps.IsKnown(step) && language.Length > 0;
    }

    /// <summary>
    /// The language asked for, and nothing else.
    ///
    /// The marketing pages fall back to English because there is no other copy to show. The wizard
    /// is different: it ships translated in every locale the platform speaks, so answering a Turkish
    /// reader with an English override would be a step backwards. Nothing configured is reported as
    /// null, and the app draws its own translation.
    /// </summary>
    private static string? Resolve(IReadOnlyDictionary<string, string> map, string? language) =>
        !string.IsNullOrWhiteSpace(language)
        && map.TryGetValue(language, out var text)
        && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;

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
