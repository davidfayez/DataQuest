using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Admin.Account;

/// <summary>
/// Sets the signed-in administrator's profile photo. The stream must be seekable so the real image
/// type can be sniffed before anything is written to storage; only JPEG and PNG are accepted.
/// </summary>
public sealed record UploadAdminAvatarCommand(string FileName, long SizeBytes, Stream Content)
    : IRequest<AdminAvatarResult>;

/// <param name="HasAvatar">Always true on success; lets the client refresh the header immediately.</param>
public sealed record AdminAvatarResult(bool HasAvatar);

public sealed class UploadAdminAvatarCommandHandler
    : IRequestHandler<UploadAdminAvatarCommand, AdminAvatarResult>
{
    // Deliberately below the 5 MB document cap: a header thumbnail never needs that much.
    private const long MaxAvatarBytes = 2 * 1024 * 1024;

    private static readonly HashSet<string> AllowedTypes =
        new(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png" };

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorage _storage;
    private readonly IFileTypeValidator _typeValidator;
    private readonly IDateTimeProvider _clock;

    public UploadAdminAvatarCommandHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IFileStorage storage,
        IFileTypeValidator typeValidator,
        IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _storage = storage;
        _typeValidator = typeValidator;
        _clock = clock;
    }

    public async Task<AdminAvatarResult> Handle(
        UploadAdminAvatarCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var adminId = _currentUser.AdminUserId
            ?? throw new ForbiddenAccessException("Only a signed-in administrator can set an avatar.");

        if (request.SizeBytes is <= 0 or > MaxAvatarBytes)
        {
            throw new ValidationException("A profile photo must be a JPEG or PNG up to 2 MB.");
        }

        // Sniff the leading bytes; an extension is trivially forged, so it is never trusted.
        var contentType = await _typeValidator.DetectAllowedContentTypeAsync(
            request.Content, request.FileName, cancellationToken);

        if (contentType is null || !AllowedTypes.Contains(contentType))
        {
            throw new ValidationException("A profile photo must be a JPEG or PNG image.");
        }

        var admin = await _db.AdminUsers.FirstOrDefaultAsync(u => u.Id == adminId, cancellationToken)
            ?? throw new NotFoundException("AdminUser", adminId);

        // Replacing an existing photo: remove the old bytes so orphans do not accumulate.
        var previousPath = admin.AvatarStoragePath;

        request.Content.Position = 0;
        var storagePath = await _storage.SaveAsync(
            request.Content,
            $"admins/{adminId:N}",
            request.FileName,
            adminId.ToString("N"),
            cancellationToken);

        admin.SetAvatar(storagePath, _clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);

        if (previousPath is not null && previousPath != storagePath)
        {
            await _storage.DeleteAsync(previousPath, cancellationToken);
        }

        return new AdminAvatarResult(HasAvatar: true);
    }
}

/// <summary>Streams the signed-in administrator's own avatar for display in the header.</summary>
public sealed record GetAdminAvatarQuery : IRequest<AdminAvatarDownload>;

public sealed record AdminAvatarDownload(string ContentType, Stream Content);

public sealed class GetAdminAvatarQueryHandler
    : IRequestHandler<GetAdminAvatarQuery, AdminAvatarDownload>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorage _storage;

    public GetAdminAvatarQueryHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IFileStorage storage)
    {
        _db = db;
        _currentUser = currentUser;
        _storage = storage;
    }

    public async Task<AdminAvatarDownload> Handle(
        GetAdminAvatarQuery request,
        CancellationToken cancellationToken)
    {
        var adminId = _currentUser.AdminUserId
            ?? throw new ForbiddenAccessException("Only a signed-in administrator has an avatar.");

        var path = await _db.AdminUsers
            .AsNoTracking()
            .Where(u => u.Id == adminId)
            .Select(u => u.AvatarStoragePath)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrEmpty(path))
        {
            throw new NotFoundException("This administrator has no profile photo.");
        }

        var content = await _storage.OpenReadAsync(path, cancellationToken);

        // Only JPEG and PNG were ever accepted, so the extension is a safe content-type source.
        var contentType = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? "image/png"
            : "image/jpeg";

        return new AdminAvatarDownload(contentType, content);
    }
}
