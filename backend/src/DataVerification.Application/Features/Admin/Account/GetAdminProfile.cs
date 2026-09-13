using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Admin.Account;

/// <summary>The signed-in administrator's own profile, for the header and the account page.</summary>
public sealed record GetAdminProfileQuery : IRequest<AdminProfileDto>;

public sealed record AdminProfileDto(
    Guid Id,
    string FullName,
    string Email,
    string Username,
    string LanguageCode,
    bool HasAvatar,
    DateTime? LastLoginAtUtc,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions);

public sealed class GetAdminProfileQueryHandler : IRequestHandler<GetAdminProfileQuery, AdminProfileDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetAdminProfileQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<AdminProfileDto> Handle(
        GetAdminProfileQuery request,
        CancellationToken cancellationToken)
    {
        var adminId = _currentUser.AdminUserId
            ?? throw new ForbiddenAccessException("Only a signed-in administrator has a profile.");

        var admin = await _db.AdminUsers
            .AsNoTracking()
            .Include(u => u.UserPermissions).ThenInclude(up => up.Permission)
            .FirstOrDefaultAsync(u => u.Id == adminId, cancellationToken)
            ?? throw new NotFoundException("AdminUser", adminId);

        return new AdminProfileDto(
            admin.Id,
            admin.FullName,
            admin.Email,
            admin.Username,
            admin.LanguageCode,
            admin.AvatarStoragePath is not null,
            admin.LastLoginAtUtc,
            Array.Empty<string>(),
            admin.ResolvePermissions().ToArray());
    }
}
