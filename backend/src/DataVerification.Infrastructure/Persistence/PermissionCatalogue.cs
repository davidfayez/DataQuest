using DataVerification.Domain.Authorization;
using DataVerification.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Infrastructure.Persistence;

/// <summary>
/// Reconciles the <c>Permissions</c> table with the catalogue this build defines.
///
/// Shared by the development seeder and by <see cref="DatabaseInitializer"/>, which runs it in
/// every environment: a release that introduces a permission is useless until the catalogue row
/// exists, because nobody can be granted a permission the database has never heard of.
/// </summary>
internal static class PermissionCatalogue
{
    internal readonly record struct SyncResult(int Added, int Updated, int Removed);

    /// <summary>
    /// Adds new permissions, refreshes the group and description of existing ones, and removes any
    /// the build no longer defines. Writes only what differs, so a no-op costs one read.
    /// </summary>
    internal static async Task<SyncResult> SyncAsync(
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var existing = await db.Permissions.ToListAsync(cancellationToken);
        var byName = existing.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        var catalogueNames = Permissions.All
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = 0;
        var updated = 0;

        foreach (var definition in Permissions.All)
        {
            if (byName.TryGetValue(definition.Name, out var row))
            {
                // Keeps a renamed group or a reworded description in step, rather than leaving the
                // panel showing what the catalogue said at the time the row was first written.
                if (row.Group != definition.Module || row.Description != definition.Description)
                {
                    row.Group = definition.Module;
                    row.Description = definition.Description;
                    updated++;
                }
            }
            else
            {
                db.Permissions.Add(new Permission
                {
                    Name = definition.Name,
                    Group = definition.Module,
                    Description = definition.Description,
                });
                added++;
            }
        }

        // Prune permissions no longer in the catalogue — for example the coarse "Lookups.Manage"
        // that the granular per-page permissions replaced. Their grants go with them, through the
        // cascade on RolePermissions and AdminUserPermissions, so nothing keeps a dangling link.
        var removed = existing.Where(p => !catalogueNames.Contains(p.Name)).ToList();
        if (removed.Count > 0)
        {
            db.Permissions.RemoveRange(removed);
        }

        if (added > 0 || updated > 0 || removed.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return new SyncResult(added, updated, removed.Count);
    }
}
