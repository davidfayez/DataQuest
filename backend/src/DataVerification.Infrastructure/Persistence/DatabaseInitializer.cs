using DataVerification.Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DataVerification.Infrastructure.Persistence;

/// <summary>
/// Brings the database up to the schema and permission catalogue this build expects, on boot.
///
/// Deploying is then "copy the files and restart the app pool" — no migration script to run by
/// hand, and no window where the new binaries are live against the old schema. Both steps are
/// safe to repeat: EF skips migrations already in <c>__EFMigrationsHistory</c>, and the catalogue
/// sync only writes what differs.
/// </summary>
public sealed class DatabaseInitializer
{
    /// <summary>
    /// Names the lock every instance contends for. IIS overlapped recycling and web gardens both
    /// mean two workers can start at once; without this they would migrate concurrently.
    /// </summary>
    private const string LockResource = "DataVerification.DatabaseInitializer";

    /// <summary>
    /// Long enough for the loser of a race to wait out the winner's migrations, short enough that
    /// a genuinely stuck lock surfaces as a startup failure rather than a hang.
    /// </summary>
    private const int LockTimeoutMs = 120_000;

    private readonly ApplicationDbContext _db;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(ApplicationDbContext db, ILogger<DatabaseInitializer> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Applies pending migrations and syncs the permission catalogue, holding an application lock
    /// so only one instance does the work.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var connection = _db.Database.GetDbConnection();
        var wasOpen = connection.State == System.Data.ConnectionState.Open;

        // The lock is held for the session, so the connection has to stay open across both steps —
        // and EF reuses this same connection for the migrations below.
        if (!wasOpen)
        {
            await _db.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            await AcquireLockAsync(cancellationToken);

            try
            {
                await ApplyMigrationsAsync(cancellationToken);
                await SyncPermissionCatalogueAsync(cancellationToken);
            }
            finally
            {
                await ReleaseLockAsync(cancellationToken);
            }
        }
        finally
        {
            if (!wasOpen)
            {
                await _db.Database.CloseConnectionAsync();
            }
        }
    }

    private async Task ApplyMigrationsAsync(CancellationToken cancellationToken)
    {
        var pending = (await _db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

        if (pending.Count == 0)
        {
            _logger.LogInformation("Database schema is up to date.");
            return;
        }

        _logger.LogInformation(
            "Applying {Count} pending migration(s): {Migrations}.",
            pending.Count,
            string.Join(", ", pending));

        await _db.Database.MigrateAsync(cancellationToken);

        _logger.LogInformation("Database schema updated.");
    }

    /// <summary>
    /// Makes the <see cref="Permissions"/> catalogue in the database match the one in this build:
    /// adds what is new, refreshes the group and description of what exists, and removes what the
    /// build no longer defines (its grants go with it, through the cascade on the link tables).
    ///
    /// Catalogue rows only — nobody is granted anything. A new permission still has to be assigned,
    /// per user, from Admin users.
    /// </summary>
    private async Task SyncPermissionCatalogueAsync(CancellationToken cancellationToken)
    {
        var changes = await PermissionCatalogue.SyncAsync(_db, cancellationToken);

        if (changes.Added == 0 && changes.Updated == 0 && changes.Removed == 0)
        {
            _logger.LogInformation("Permission catalogue is up to date.");
            return;
        }

        _logger.LogInformation(
            "Permission catalogue synced: {Added} added, {Updated} updated, {Removed} removed.",
            changes.Added,
            changes.Updated,
            changes.Removed);
    }

    private async Task AcquireLockAsync(CancellationToken cancellationToken)
    {
        // sp_getapplock returns >= 0 when the lock was taken; RAISERROR turns anything else into an
        // exception here rather than a silent second migrator.
        await _db.Database.ExecuteSqlRawAsync(
            """
            DECLARE @result int;
            EXEC @result = sp_getapplock
                @Resource = {0},
                @LockMode = 'Exclusive',
                @LockOwner = 'Session',
                @LockTimeout = {1};
            IF @result < 0
                RAISERROR (N'Could not acquire the database initialisation lock (%d).', 16, 1, @result);
            """,
            [LockResource, LockTimeoutMs],
            cancellationToken);
    }

    private async Task ReleaseLockAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.Database.ExecuteSqlRawAsync(
                "EXEC sp_releaseapplock @Resource = {0}, @LockOwner = 'Session';",
                [LockResource],
                cancellationToken);
        }
        catch (Exception ex)
        {
            // Closing the connection releases a session lock anyway, so this is worth a line in the
            // log but never worth failing a startup that already did its work.
            _logger.LogWarning(ex, "Could not release the database initialisation lock.");
        }
    }
}
