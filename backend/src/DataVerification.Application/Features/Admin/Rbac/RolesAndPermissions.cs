using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Domain.Authorization;
using DataVerification.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Admin.Rbac;

// ------------------------------------------------------------- Permissions

/// <summary>
/// The permission catalogue, grouped by module and ordered for the role editor grid. Each entry
/// carries its action (View / Create / Update / Delete / …) so the client can lay the grid out in
/// columns without parsing the permission name.
/// </summary>
public sealed record GetPermissionsQuery : IRequest<IReadOnlyList<PermissionGroupDto>>;

public sealed record PermissionGroupDto(
    string Group,
    int Order,
    IReadOnlyList<PermissionDto> Permissions);

public sealed record PermissionDto(Guid Id, string Name, string Group, string Action, string? Description);

public sealed class GetPermissionsQueryHandler
    : IRequestHandler<GetPermissionsQuery, IReadOnlyList<PermissionGroupDto>>
{
    private readonly IApplicationDbContext _db;

    public GetPermissionsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<PermissionGroupDto>> Handle(
        GetPermissionsQuery request,
        CancellationToken cancellationToken)
    {
        // The DB holds the ids; the catalogue holds the presentation (module, action, order). Join
        // the two so the grid is driven by the catalogue and a stray legacy row cannot skew it.
        var idByName = await _db.Permissions
            .AsNoTracking()
            .ToDictionaryAsync(p => p.Name, p => p.Id, cancellationToken);

        var actionRank = Permissions.ActionOrder
            .Select((action, index) => (action, index))
            .ToDictionary(x => x.action, x => x.index);

        return Permissions.All
            .Where(definition => idByName.ContainsKey(definition.Name))
            .GroupBy(definition => (definition.Module, definition.ModuleOrder))
            .OrderBy(group => group.Key.ModuleOrder)
            .Select(group => new PermissionGroupDto(
                group.Key.Module,
                group.Key.ModuleOrder,
                group
                    .OrderBy(d => actionRank.TryGetValue(d.Action, out var rank) ? rank : int.MaxValue)
                    .Select(d => new PermissionDto(
                        idByName[d.Name], d.Name, d.Module, d.Action, d.Description))
                    .ToList()))
            .ToList();
    }
}

// ------------------------------------------------------------------- Roles

public sealed record GetRolesQuery : IRequest<IReadOnlyList<RoleDto>>;

public sealed record RoleDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystemRole,
    int UserCount,
    IReadOnlyList<string> Permissions);

public sealed class GetRolesQueryHandler : IRequestHandler<GetRolesQuery, IReadOnlyList<RoleDto>>
{
    private readonly IApplicationDbContext _db;

    public GetRolesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<RoleDto>> Handle(
        GetRolesQuery request,
        CancellationToken cancellationToken)
    {
        // A direct projection rather than two collection Includes. Loading both RolePermissions and
        // UserRoles on the same query multiplies them into a cartesian product — with a role that
        // has 44 permissions and many assigned users that becomes tens of thousands of rows and the
        // request hangs. Here the user count is a scalar subquery and the permission names are a
        // split collection projection, so neither collection inflates the other.
        var roles = await _db.Roles
            .AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new RoleDto(
                r.Id,
                r.Name,
                r.Description,
                r.IsSystemRole,
                r.UserRoles.Count,
                r.RolePermissions
                    .Where(rp => rp.Permission != null)
                    .Select(rp => rp.Permission!.Name)
                    .OrderBy(name => name)
                    .ToList()))
            .ToListAsync(cancellationToken);

        return roles;
    }
}

public sealed record UpsertRoleCommand(
    Guid? Id,
    string Name,
    string? Description,
    IReadOnlyList<string> Permissions) : IRequest<RoleDto>;

public sealed class UpsertRoleCommandValidator : AbstractValidator<UpsertRoleCommand>
{
    public UpsertRoleCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Description).MaximumLength(500);
        RuleFor(c => c.Permissions).NotNull();
    }
}

public sealed class UpsertRoleCommandHandler : IRequestHandler<UpsertRoleCommand, RoleDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditLogger _auditLogger;

    public UpsertRoleCommandHandler(IApplicationDbContext db, IAuditLogger auditLogger)
    {
        _db = db;
        _auditLogger = auditLogger;
    }

    public async Task<RoleDto> Handle(UpsertRoleCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var name = request.Name.Trim();

        var duplicate = await _db.Roles
            .AnyAsync(r => r.Name == name && (request.Id == null || r.Id != request.Id), cancellationToken);

        if (duplicate)
        {
            throw new ConflictException("role.duplicate_name", $"A role named '{name}' already exists.");
        }

        var requested = request.Permissions.Distinct().ToList();

        var permissions = await _db.Permissions
            .Where(p => requested.Contains(p.Name))
            .ToListAsync(cancellationToken);

        if (permissions.Count != requested.Count)
        {
            throw new NotFoundException("One or more of the permissions supplied do not exist.");
        }

        Role role;
        if (request.Id is { } id)
        {
            role = await _db.Roles
                .Include(r => r.RolePermissions)
                .Include(r => r.UserRoles)
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(Role), id);

            // Renaming a seeded role would break the constants the seeder and tests rely on.
            if (role.IsSystemRole && !string.Equals(role.Name, name, StringComparison.Ordinal))
            {
                throw new ConflictException(
                    "role.system_role_immutable_name",
                    "A built-in role cannot be renamed.");
            }
        }
        else
        {
            role = new Role { Name = name };
            _db.Roles.Add(role);
        }

        role.Name = name;
        role.Description = request.Description?.Trim();

        var wanted = permissions.Select(p => p.Id).ToHashSet();

        foreach (var link in role.RolePermissions.ToList().Where(link => !wanted.Contains(link.PermissionId)))
        {
            _db.RolePermissions.Remove(link);
            role.RolePermissions.Remove(link);
        }

        var existing = role.RolePermissions.Select(link => link.PermissionId).ToHashSet();

        foreach (var permissionId in wanted.Where(permissionId => !existing.Contains(permissionId)))
        {
            role.RolePermissions.Add(new RolePermission
            {
                RoleId = role.Id,
                PermissionId = permissionId,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            request.Id is null ? "Role.Created" : "Role.Updated",
            nameof(Role),
            role.Id,
            new { role.Name, Permissions = requested },
            cancellationToken);

        return new RoleDto(
            role.Id,
            role.Name,
            role.Description,
            role.IsSystemRole,
            role.UserRoles.Count,
            permissions.Select(p => p.Name).OrderBy(name => name).ToList());
    }
}

public sealed record DeleteRoleCommand(Guid Id) : IRequest;

public sealed class DeleteRoleCommandHandler : IRequestHandler<DeleteRoleCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditLogger _auditLogger;

    public DeleteRoleCommandHandler(IApplicationDbContext db, IAuditLogger auditLogger)
    {
        _db = db;
        _auditLogger = auditLogger;
    }

    public async Task Handle(DeleteRoleCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var role = await _db.Roles
            .Include(r => r.UserRoles)
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Role), request.Id);

        if (role.IsSystemRole)
        {
            throw new ConflictException("role.system_role_protected", "A built-in role cannot be deleted.");
        }

        if (role.UserRoles.Count > 0)
        {
            throw new ConflictException(
                "role.in_use",
                "Remove this role from every admin user before deleting it.");
        }

        _db.Roles.Remove(role);
        await _db.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync("Role.Deleted", nameof(Role), request.Id, new { role.Name }, cancellationToken);
    }
}
