using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Common;
using DataVerification.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Content;

/// <summary>
/// The platform's logo, uploaded from the admin panel.
///
/// Held in <c>SiteSettings</c> rather than a table of its own: there is exactly one, and three
/// rows keyed by name need no migration and no entity for a single image. The bytes live in file
/// storage like every other upload; only the pointer is a setting.
/// </summary>
public static class BrandingDefaults
{
    public const string LogoPathKey = "branding.logo.path";
    public const string LogoContentTypeKey = "branding.logo.contentType";
    public const string LogoFileNameKey = "branding.logo.fileName";

    /// <summary>Where the logo file is written, alongside the other upload directories.</summary>
    public const string StorageDirectory = "branding";

    /// <summary>A logo is a small mark; anything larger is a photograph pasted in by mistake.</summary>
    public const long MaxLogoBytes = 2 * 1024 * 1024;
}

/// <summary>
/// What the sites need to draw the brand mark.
/// </summary>
/// <param name="HasLogo">
/// False when nothing has been uploaded, which both apps read as "use the mark bundled with the
/// build" — so the header is never empty, and removing the upload restores the original artwork.
/// </param>
/// <param name="Version">
/// Changes whenever the logo is replaced. The sites append it to the image URL so a new upload is
/// fetched immediately rather than after a cached copy expires.
/// </param>
public sealed record BrandingDto(bool HasLogo, string? FileName, string Version);

public sealed record GetBrandingQuery : IRequest<BrandingDto>;

/// <summary>The logo's bytes, for the public endpoint that serves them.</summary>
public sealed record LogoDownload(Stream Content, string ContentType, string FileName);

public sealed record GetLogoQuery : IRequest<LogoDownload>;

public sealed record UploadLogoCommand(Stream Content, string FileName, long SizeBytes)
    : IRequest<BrandingDto>;

/// <summary>Removes the uploaded logo, after which the bundled mark applies again.</summary>
public sealed record DeleteLogoCommand : IRequest<BrandingDto>;

public sealed class BrandingHandlers :
    IRequestHandler<GetBrandingQuery, BrandingDto>,
    IRequestHandler<GetLogoQuery, LogoDownload>,
    IRequestHandler<UploadLogoCommand, BrandingDto>,
    IRequestHandler<DeleteLogoCommand, BrandingDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IFileStorage _storage;
    private readonly IFileTypeValidator _typeValidator;
    private readonly IAuditLogger _auditLogger;

    public BrandingHandlers(
        IApplicationDbContext db,
        IFileStorage storage,
        IFileTypeValidator typeValidator,
        IAuditLogger auditLogger)
    {
        _db = db;
        _storage = storage;
        _typeValidator = typeValidator;
        _auditLogger = auditLogger;
    }

    public async Task<BrandingDto> Handle(GetBrandingQuery request, CancellationToken cancellationToken) =>
        await DescribeAsync(cancellationToken);

    public async Task<LogoDownload> Handle(GetLogoQuery request, CancellationToken cancellationToken)
    {
        var rows = await LoadRowsAsync(cancellationToken);

        var path = rows.GetValueOrDefault(BrandingDefaults.LogoPathKey)?.Value;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new NotFoundException("Logo", Guid.Empty);
        }

        var contentType = rows.GetValueOrDefault(BrandingDefaults.LogoContentTypeKey)?.Value
            ?? "application/octet-stream";
        var fileName = rows.GetValueOrDefault(BrandingDefaults.LogoFileNameKey)?.Value ?? "logo";

        // A pointer with no file behind it is a broken deployment, not a 500: say it is missing.
        if (!await _storage.ExistsAsync(path, cancellationToken))
        {
            throw new NotFoundException("Logo", Guid.Empty);
        }

        return new LogoDownload(
            await _storage.OpenReadAsync(path, cancellationToken),
            contentType,
            fileName);
    }

    public async Task<BrandingDto> Handle(UploadLogoCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.SizeBytes <= 0 || request.SizeBytes > BrandingDefaults.MaxLogoBytes)
        {
            throw new DomainException(
                "branding.logo_too_large",
                $"The logo must be between 1 byte and {BrandingDefaults.MaxLogoBytes / (1024 * 1024)} MB.");
        }

        // Signature check, not an extension check: the declared name proves nothing.
        var detected = await _typeValidator.DetectAllowedContentTypeAsync(
            request.Content, request.FileName, cancellationToken);

        if (detected is not ("image/jpeg" or "image/png"))
        {
            throw new DomainException(
                "branding.logo_type_not_allowed",
                "The logo must be a JPEG or a PNG.");
        }

        var rows = await LoadRowsAsync(cancellationToken);
        var previousPath = rows.GetValueOrDefault(BrandingDefaults.LogoPathKey)?.Value;

        var storedPath = await _storage.SaveAsync(
            request.Content,
            BrandingDefaults.StorageDirectory,
            request.FileName,
            "logo",
            cancellationToken);

        Set(rows, BrandingDefaults.LogoPathKey, storedPath);
        Set(rows, BrandingDefaults.LogoContentTypeKey, detected);
        Set(rows, BrandingDefaults.LogoFileNameKey, request.FileName);

        await _db.SaveChangesAsync(cancellationToken);

        // Only once the new pointer is safely recorded is the old file discarded.
        if (!string.IsNullOrWhiteSpace(previousPath) && previousPath != storedPath)
        {
            await _storage.DeleteAsync(previousPath, cancellationToken);
        }

        await _auditLogger.LogAsync(
            "Branding.LogoUploaded",
            nameof(SiteSetting),
            null,
            new { request.FileName, request.SizeBytes },
            cancellationToken);

        return await DescribeAsync(cancellationToken);
    }

    public async Task<BrandingDto> Handle(DeleteLogoCommand request, CancellationToken cancellationToken)
    {
        var rows = await LoadRowsAsync(cancellationToken);
        var path = rows.GetValueOrDefault(BrandingDefaults.LogoPathKey)?.Value;

        foreach (var row in rows.Values)
        {
            _db.SiteSettings.Remove(row);
        }

        await _db.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(path))
        {
            await _storage.DeleteAsync(path, cancellationToken);
        }

        await _auditLogger.LogAsync(
            "Branding.LogoRemoved", nameof(SiteSetting), null, null, cancellationToken);

        return await DescribeAsync(cancellationToken);
    }

    private async Task<Dictionary<string, SiteSetting>> LoadRowsAsync(CancellationToken cancellationToken)
    {
        var keys = new[]
        {
            BrandingDefaults.LogoPathKey,
            BrandingDefaults.LogoContentTypeKey,
            BrandingDefaults.LogoFileNameKey,
        };

        return await _db.SiteSettings
            .Where(s => keys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, cancellationToken);
    }

    private void Set(Dictionary<string, SiteSetting> rows, string key, string value)
    {
        if (rows.TryGetValue(key, out var row))
        {
            row.Value = value;
            return;
        }

        var created = new SiteSetting { Key = key, Value = value };
        _db.SiteSettings.Add(created);
        rows[key] = created;
    }

    private async Task<BrandingDto> DescribeAsync(CancellationToken cancellationToken)
    {
        var rows = await _db.SiteSettings
            .AsNoTracking()
            .Where(s => s.Key == BrandingDefaults.LogoPathKey
                        || s.Key == BrandingDefaults.LogoFileNameKey)
            .ToListAsync(cancellationToken);

        var pathRow = rows.Find(s => s.Key == BrandingDefaults.LogoPathKey);
        var hasLogo = !string.IsNullOrWhiteSpace(pathRow?.Value);

        // Derived from when the pointer last changed, so replacing the logo changes the URL the
        // sites request and a cached copy of the old one is never shown.
        var stamp = pathRow?.UpdatedAtUtc ?? pathRow?.CreatedAtUtc;

        return new BrandingDto(
            hasLogo,
            rows.Find(s => s.Key == BrandingDefaults.LogoFileNameKey)?.Value,
            stamp?.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0");
    }
}
