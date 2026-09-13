using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Common.Models;
using DataVerification.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Admin.Rbac;

public sealed record GetAdminUsersQuery : PagedQuery, IRequest<PagedResult<AdminUserDto>>;

public sealed record AdminUserDto(
    Guid Id,
    string Email,
    string Username,
    string FullName,
    bool IsActive,
    string LanguageCode,
    DateTime? LastLoginAtUtc,
    DateTime CreatedAtUtc,
    IReadOnlyList<string> Permissions);

public sealed class GetAdminUsersQueryHandler
    : IRequestHandler<GetAdminUsersQuery, PagedResult<AdminUserDto>>
{
    private readonly IApplicationDbContext _db;

    public GetAdminUsersQueryHandler(IApplicationDbContext db) => _db = db;

    public Task<PagedResult<AdminUserDto>> Handle(
        GetAdminUsersQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = _db.AdminUsers
            .AsNoTracking()
            .Include(u => u.UserPermissions).ThenInclude(up => up.Permission)
            .AsQueryable();

        if (request.IsActive is { } isActive)
        {
            query = query.Where(u => u.IsActive == isActive);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(u =>
                EF.Functions.Like(u.Email, $"%{term}%")
                || EF.Functions.Like(u.Username, $"%{term}%")
                || EF.Functions.Like(u.FullName, $"%{term}%"));
        }

        query = query.OrderBy(u => u.FullName);

        return query.ToPagedResultAsync(
            request,
            user => new AdminUserDto(
                user.Id,
                user.Email,
                user.Username,
                user.FullName,
                user.IsActive,
                user.LanguageCode,
                user.LastLoginAtUtc,
                user.CreatedAtUtc,
                user.ResolvePermissions().OrderBy(name => name).ToList()),
            cancellationToken);
    }
}

/// <param name="Password">Required when creating; leave null on update to keep the current one.</param>
public sealed record UpsertAdminUserCommand(
    Guid? Id,
    string Email,
    string Username,
    string FullName,
    string? Password,
    bool IsActive,
    string LanguageCode,
    IReadOnlyList<string> Permissions) : IRequest<AdminUserDto>;

public sealed class UpsertAdminUserCommandValidator : AbstractValidator<UpsertAdminUserCommand>
{
    public UpsertAdminUserCommandValidator()
    {
        RuleFor(c => c.Email).NotEmpty().MaximumLength(320).EmailAddress();
        RuleFor(c => c.FullName).NotEmpty().MaximumLength(200);

        // '@' is excluded so the login form can tell a username from an email address without
        // asking the user which one they typed.
        RuleFor(c => c.Username)
            .NotEmpty()
            .MinimumLength(3)
            .MaximumLength(50)
            .Matches("^[A-Za-z0-9._-]+$")
            .WithMessage("A username may contain only letters, digits, dots, hyphens and underscores.");
        RuleFor(c => c.LanguageCode).NotEmpty().MaximumLength(10);
        RuleFor(c => c.Permissions).NotNull();

        // A new account must be given a password; an existing one keeps its current password
        // unless a replacement is supplied.
        RuleFor(c => c.Password)
            .NotEmpty().When(c => c.Id is null)
            .WithMessage("A password is required when creating an admin user.");

        RuleFor(c => c.Password)
            .MinimumLength(10).When(c => !string.IsNullOrEmpty(c.Password))
            .WithMessage("Passwords must be at least 10 characters.");
    }
}

public sealed class UpsertAdminUserCommandHandler
    : IRequestHandler<UpsertAdminUserCommand, AdminUserDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IPasswordHashingService _passwordHasher;
    private readonly IAuditLogger _auditLogger;

    public UpsertAdminUserCommandHandler(
        IApplicationDbContext db,
        IPasswordHashingService passwordHasher,
        IAuditLogger auditLogger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _auditLogger = auditLogger;
    }

    public async Task<AdminUserDto> Handle(
        UpsertAdminUserCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var email = request.Email.Trim();
        var username = request.Username.Trim().ToLowerInvariant();

        var duplicate = await _db.AdminUsers
            .AnyAsync(u => u.Email == email && (request.Id == null || u.Id != request.Id), cancellationToken);

        if (duplicate)
        {
            throw new ConflictException("admin_user.duplicate_email", $"'{email}' is already in use.");
        }

        var duplicateUsername = await _db.AdminUsers
            .AnyAsync(
                u => u.Username == username && (request.Id == null || u.Id != request.Id),
                cancellationToken);

        if (duplicateUsername)
        {
            throw new ConflictException(
                "admin_user.duplicate_username",
                $"The username '{username}' is already in use.");
        }

        var wantedNames = request.Permissions
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var permissions = await _db.Permissions
            .Where(p => wantedNames.Contains(p.Name))
            .ToListAsync(cancellationToken);

        if (permissions.Count != wantedNames.Count)
        {
            throw new NotFoundException("One or more of the permissions supplied do not exist.");
        }

        AdminUser user;
        if (request.Id is { } id)
        {
            user = await _db.AdminUsers
                .Include(u => u.UserPermissions)
                .FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(AdminUser), id);
        }
        else
        {
            user = new AdminUser
            {
                Email = email,
                Username = username,
                FullName = request.FullName,
                PasswordHash = string.Empty,
            };
            _db.AdminUsers.Add(user);
        }

        user.Email = email;
        user.Username = username;
        user.FullName = request.FullName.Trim();
        user.IsActive = request.IsActive;
        user.LanguageCode = request.LanguageCode.ToLowerInvariant();

        if (!string.IsNullOrEmpty(request.Password))
        {
            user.PasswordHash = _passwordHasher.Hash(request.Password);
        }

        var wantedIds = permissions.Select(p => p.Id).ToHashSet();

        foreach (var link in user.UserPermissions.ToList().Where(link => !wantedIds.Contains(link.PermissionId)))
        {
            _db.AdminUserPermissions.Remove(link);
            user.UserPermissions.Remove(link);
        }

        var existing = user.UserPermissions.Select(link => link.PermissionId).ToHashSet();

        foreach (var permissionId in wantedIds.Where(permissionId => !existing.Contains(permissionId)))
        {
            user.UserPermissions.Add(new AdminUserPermission
            {
                AdminUserId = user.Id,
                PermissionId = permissionId,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        // The password itself is never written to the audit trail, only the fact it changed.
        await _auditLogger.LogAsync(
            request.Id is null ? "AdminUser.Created" : "AdminUser.Updated",
            nameof(AdminUser),
            user.Id,
            new
            {
                user.Email,
                user.Username,
                user.IsActive,
                Permissions = permissions.Select(p => p.Name).OrderBy(name => name),
                PasswordChanged = !string.IsNullOrEmpty(request.Password),
            },
            cancellationToken);

        return new AdminUserDto(
            user.Id,
            user.Email,
            user.Username,
            user.FullName,
            user.IsActive,
            user.LanguageCode,
            user.LastLoginAtUtc,
            user.CreatedAtUtc,
            permissions.Select(p => p.Name).OrderBy(name => name).ToList());
    }
}

/// <summary>Deactivates rather than deletes, so the audit trail keeps a resolvable actor.</summary>
public sealed record SetAdminUserActiveCommand(Guid Id, bool IsActive) : IRequest;

public sealed class SetAdminUserActiveCommandHandler : IRequestHandler<SetAdminUserActiveCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _auditLogger;

    public SetAdminUserActiveCommandHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IAuditLogger auditLogger)
    {
        _db = db;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
    }

    public async Task Handle(SetAdminUserActiveCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Locking yourself out is never the intent, and recovering requires database access.
        if (_currentUser.AdminUserId == request.Id && !request.IsActive)
        {
            throw new ConflictException(
                "admin_user.cannot_deactivate_self",
                "You cannot deactivate your own account.");
        }

        var user = await _db.AdminUsers.FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(AdminUser), request.Id);

        user.IsActive = request.IsActive;
        await _db.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            request.IsActive ? "AdminUser.Activated" : "AdminUser.Deactivated",
            nameof(AdminUser),
            user.Id,
            new { user.Email },
            cancellationToken);
    }
}
