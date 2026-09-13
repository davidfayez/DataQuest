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

/// <summary>
/// One entry on the public tools page, resolved to a single language.
/// <paramref name="ImageUrl"/> is a relative API path the browser can put straight into an
/// <c>img</c> tag; it is null for a video entry.
/// </summary>
public sealed record ToolResourceDto(
    Guid Id,
    string Kind,
    string Name,
    string Description,
    string? VideoUrl,
    string? ImageUrl,
    int SortOrder);

// --------------------------------------------------------------------- Admin DTOs

/// <summary>An entry with every language's copy, for the editor.</summary>
public sealed record AdminToolResourceDto(
    Guid Id,
    string Kind,
    IReadOnlyDictionary<string, string> Names,
    IReadOnlyDictionary<string, string> Descriptions,
    string? VideoUrl,
    string? ImageFileName,
    bool HasImage,
    int SortOrder,
    bool IsPublished);

// ------------------------------------------------------------------------ Queries

/// <summary>Public read: published, complete entries in display order, in the caller's language.</summary>
public sealed record GetToolResourcesQuery : IRequest<IReadOnlyList<ToolResourceDto>>;

/// <summary>Admin read: every entry, including unpublished ones, with all translations.</summary>
public sealed record GetAdminToolResourcesQuery : IRequest<IReadOnlyList<AdminToolResourceDto>>;

/// <summary>Streams an entry's uploaded image. Public, because the tools page is public.</summary>
public sealed record GetToolResourceImageQuery(Guid Id) : IRequest<ToolImageDownload>;

public sealed record ToolImageDownload(Stream Content, string ContentType, string FileName);

// ----------------------------------------------------------------------- Commands

public sealed record UpsertToolResourceCommand(
    Guid? Id,
    ToolResourceKind Kind,
    Dictionary<string, string> Names,
    Dictionary<string, string> Descriptions,
    string? VideoUrl,
    int SortOrder,
    bool IsPublished) : IRequest<AdminToolResourceDto>;

public sealed record DeleteToolResourceCommand(Guid Id) : IRequest<Unit>;

/// <summary>Attaches or replaces the image on an entry whose kind is Image.</summary>
public sealed record UploadToolResourceImageCommand(
    Guid Id,
    string FileName,
    long SizeBytes,
    Stream Content) : IRequest<AdminToolResourceDto>;

// --------------------------------------------------------------------- Validation

public sealed class UpsertToolResourceCommandValidator : AbstractValidator<UpsertToolResourceCommand>
{
    /// <summary>Kept well under the column so a pasted tracking-laden URL still fits.</summary>
    private const int MaxVideoUrlLength = 2000;

    public UpsertToolResourceCommandValidator()
    {
        RuleFor(c => c.SortOrder).GreaterThanOrEqualTo(0);
        RuleFor(c => c.Kind).IsInEnum();

        RuleFor(c => c.Names)
            .Must(TranslationRules.HasEnglish).WithMessage("An English name is required.")
            .Must(TranslationRules.AllSupported).WithMessage("A name uses an unsupported language.")
            .Must(n => TranslationRules.WithinLength(n, 200)).WithMessage("A name is too long (max 200).");

        RuleFor(c => c.Descriptions)
            .Must(TranslationRules.HasEnglish).WithMessage("An English description is required.")
            .Must(TranslationRules.AllSupported).WithMessage("A description uses an unsupported language.")
            .Must(d => TranslationRules.WithinLength(d, 2000)).WithMessage("A description is too long (max 2000).");

        // A video entry is useless without its link; an image entry gets its file in a second step,
        // so nothing is required of it here.
        When(c => c.Kind == ToolResourceKind.Video, () =>
        {
            RuleFor(c => c.VideoUrl)
                .NotEmpty().WithMessage("A video link is required.")
                .MaximumLength(MaxVideoUrlLength)
                .Must(BeAnHttpUrl).WithMessage("The video link must be a http:// or https:// address.");
        });
    }

    private static bool BeAnHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}

// ------------------------------------------------------------------------ Handlers

public sealed class ToolResourceHandlers :
    IRequestHandler<GetToolResourcesQuery, IReadOnlyList<ToolResourceDto>>,
    IRequestHandler<GetAdminToolResourcesQuery, IReadOnlyList<AdminToolResourceDto>>,
    IRequestHandler<GetToolResourceImageQuery, ToolImageDownload>,
    IRequestHandler<UpsertToolResourceCommand, AdminToolResourceDto>,
    IRequestHandler<DeleteToolResourceCommand, Unit>,
    IRequestHandler<UploadToolResourceImageCommand, AdminToolResourceDto>
{
    /// <summary>An illustration for a help page never needs more than this.</summary>
    public const long MaxImageBytes = 5 * 1024 * 1024;

    private const string StorageDirectory = "tools";

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorage _storage;
    private readonly IFileTypeValidator _typeValidator;
    private readonly IAuditLogger _auditLogger;

    public ToolResourceHandlers(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IFileStorage storage,
        IFileTypeValidator typeValidator,
        IAuditLogger auditLogger)
    {
        _db = db;
        _currentUser = currentUser;
        _storage = storage;
        _typeValidator = typeValidator;
        _auditLogger = auditLogger;
    }

    public async Task<IReadOnlyList<ToolResourceDto>> Handle(
        GetToolResourcesQuery request,
        CancellationToken cancellationToken)
    {
        var language = _currentUser.LanguageCode;

        var entries = await _db.ToolResources
            .AsNoTracking()
            .Include(r => r.Translations)
            .Where(r => r.IsPublished)
            .OrderBy(r => r.SortOrder)
            .ThenBy(r => r.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        // IsShowable is computed, so the "half-finished entries stay hidden" rule is applied here
        // rather than in SQL.
        return entries
            .Where(r => r.IsShowable)
            .Select(r => ToPublicDto(r, language))
            .ToList();
    }

    public async Task<IReadOnlyList<AdminToolResourceDto>> Handle(
        GetAdminToolResourcesQuery request,
        CancellationToken cancellationToken)
    {
        var entries = await _db.ToolResources
            .AsNoTracking()
            .Include(r => r.Translations)
            .OrderBy(r => r.SortOrder)
            .ThenBy(r => r.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return entries.Select(ToAdminDto).ToList();
    }

    public async Task<ToolImageDownload> Handle(
        GetToolResourceImageQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entry = await _db.ToolResources
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(ToolResource), request.Id);

        if (!entry.HasImage)
        {
            throw new NotFoundException("Image for tool entry", request.Id);
        }

        var content = await _storage.OpenReadAsync(entry.ImageStoragePath!, cancellationToken);

        return new ToolImageDownload(
            content,
            entry.ImageContentType ?? "application/octet-stream",
            entry.ImageFileName ?? "image");
    }

    public async Task<AdminToolResourceDto> Handle(
        UpsertToolResourceCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ToolResource entry;

        if (request.Id is { } id)
        {
            entry = await _db.ToolResources
                .Include(r => r.Translations)
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(ToolResource), id);

            // Switching an entry from image to video leaves an orphan file behind, so it goes now.
            if (entry.Kind != request.Kind && entry.HasImage)
            {
                await _storage.DeleteAsync(entry.ImageStoragePath!, cancellationToken);
                entry.ImageStoragePath = null;
                entry.ImageContentType = null;
                entry.ImageFileName = null;
            }
        }
        else
        {
            entry = new ToolResource { Kind = request.Kind };
            _db.ToolResources.Add(entry);
        }

        entry.Kind = request.Kind;
        entry.SortOrder = request.SortOrder;
        entry.IsPublished = request.IsPublished;
        entry.VideoUrl = request.Kind == ToolResourceKind.Video ? request.VideoUrl?.Trim() : null;

        ApplyTranslations(entry, request.Names, request.Descriptions);

        await _db.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            request.Id is null ? "ToolResource.Created" : "ToolResource.Updated",
            nameof(ToolResource),
            entry.Id,
            new { entry.Kind, entry.SortOrder, entry.IsPublished },
            cancellationToken);

        return ToAdminDto(entry);
    }

    public async Task<Unit> Handle(DeleteToolResourceCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entry = await _db.ToolResources
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(ToolResource), request.Id);

        // Remove the file first: a failure here must not leave a row pointing at nothing.
        if (entry.HasImage)
        {
            await _storage.DeleteAsync(entry.ImageStoragePath!, cancellationToken);
        }

        _db.ToolResources.Remove(entry);
        await _db.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            "ToolResource.Deleted", nameof(ToolResource), request.Id, null, cancellationToken);

        return Unit.Value;
    }

    public async Task<AdminToolResourceDto> Handle(
        UploadToolResourceImageCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entry = await _db.ToolResources
            .Include(r => r.Translations)
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(ToolResource), request.Id);

        if (entry.Kind != ToolResourceKind.Image)
        {
            throw new DomainException(
                "tool.not_an_image_entry",
                "Only an entry of kind Image can carry an uploaded picture.");
        }

        if (request.SizeBytes <= 0 || request.SizeBytes > MaxImageBytes)
        {
            throw new DomainException(
                "tool.image_too_large",
                $"The image must be between 1 byte and {MaxImageBytes / (1024 * 1024)} MB.");
        }

        // Signature check, not an extension check: the declared name proves nothing.
        var detected = await _typeValidator.DetectAllowedContentTypeAsync(
            request.Content, request.FileName, cancellationToken);

        if (detected is not ("image/jpeg" or "image/png"))
        {
            throw new DomainException(
                "tool.image_type_not_allowed",
                "The image must be a JPEG or a PNG.");
        }

        var previousPath = entry.ImageStoragePath;

        var storedPath = await _storage.SaveAsync(
            request.Content,
            StorageDirectory,
            request.FileName,
            entry.Id.ToString(),
            cancellationToken);

        entry.ImageStoragePath = storedPath;
        entry.ImageContentType = detected;
        entry.ImageFileName = request.FileName;

        await _db.SaveChangesAsync(cancellationToken);

        // Only once the new file is safely recorded is the old one discarded.
        if (!string.IsNullOrWhiteSpace(previousPath) && previousPath != storedPath)
        {
            await _storage.DeleteAsync(previousPath, cancellationToken);
        }

        await _auditLogger.LogAsync(
            "ToolResource.ImageUploaded",
            nameof(ToolResource),
            entry.Id,
            new { request.FileName, request.SizeBytes },
            cancellationToken);

        return ToAdminDto(entry);
    }

    /// <summary>
    /// Replaces the entry's translations with exactly what was submitted, so clearing a language in
    /// the editor removes it rather than leaving stale copy behind.
    /// </summary>
    private static void ApplyTranslations(
        ToolResource entry,
        IReadOnlyDictionary<string, string> names,
        IReadOnlyDictionary<string, string> descriptions)
    {
        var languages = names.Keys
            .Concat(descriptions.Keys)
            .Select(code => code.ToLowerInvariant())
            .Where(LandingLanguages.IsSupported)
            .Distinct()
            .ToList();

        foreach (var stale in entry.Translations.Where(t => !languages.Contains(t.LanguageCode)).ToList())
        {
            entry.Translations.Remove(stale);
        }

        foreach (var language in languages)
        {
            var name = Lookup(names, language);
            var description = Lookup(descriptions, language);

            // A language with neither field filled in is simply not a translation.
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(description))
            {
                var blank = entry.Translations.FirstOrDefault(t => t.LanguageCode == language);
                if (blank is not null)
                {
                    entry.Translations.Remove(blank);
                }

                continue;
            }

            var existing = entry.Translations.FirstOrDefault(t => t.LanguageCode == language);
            if (existing is null)
            {
                entry.Translations.Add(new ToolResourceTranslation
                {
                    LanguageCode = language,
                    Name = name,
                    Description = description,
                });
            }
            else
            {
                existing.Name = name;
                existing.Description = description;
            }
        }
    }

    private static string Lookup(IReadOnlyDictionary<string, string> map, string language)
    {
        foreach (var pair in map)
        {
            if (string.Equals(pair.Key, language, StringComparison.OrdinalIgnoreCase))
            {
                return (pair.Value ?? string.Empty).Trim();
            }
        }

        return string.Empty;
    }

    private static ToolResourceDto ToPublicDto(ToolResource entry, string? language) => new(
        entry.Id,
        entry.Kind.ToString(),
        entry.ResolveName(language),
        entry.ResolveDescription(language),
        entry.Kind == ToolResourceKind.Video ? entry.VideoUrl : null,
        entry.HasImage ? $"content/tools/{entry.Id}/image" : null,
        entry.SortOrder);

    private static AdminToolResourceDto ToAdminDto(ToolResource entry) => new(
        entry.Id,
        entry.Kind.ToString(),
        entry.Translations.ToDictionary(t => t.LanguageCode, t => t.Name),
        entry.Translations.ToDictionary(t => t.LanguageCode, t => t.Description),
        entry.VideoUrl,
        entry.ImageFileName,
        entry.HasImage,
        entry.SortOrder,
        entry.IsPublished);
}
