using System.Linq.Expressions;
using DataVerification.Application.Common.Exceptions;
using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Common.Models;
using DataVerification.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Features.Lookups.Admin;

/// <summary>
/// The shared engine behind every admin lookup CRUD screen. All nine lookups need the same
/// paging, bilingual search, uniqueness checks, referential-safety checks and audit writes, so
/// that behaviour lives here once and each lookup's handler only supplies what is specific to it.
/// </summary>
public sealed class AdminLookupService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _auditLogger;

    public AdminLookupService(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IAuditLogger auditLogger)
    {
        _db = db;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
    }

    public string Language => _currentUser.LanguageCode;

    /// <summary>
    /// Applies the active filter and a bilingual name search, then pages the result. Search
    /// matches either language so an Arabic-speaking admin and an English-speaking one can both
    /// find the same row.
    /// </summary>
    public async Task<PagedResult<TDto>> ListAsync<TEntity, TDto>(
        IQueryable<TEntity> source,
        PagedQuery paging,
        Func<TEntity, TDto> map,
        CancellationToken cancellationToken)
        where TEntity : LocalizedLookup
    {
        ArgumentNullException.ThrowIfNull(paging);

        var query = source.AsNoTracking();

        if (paging.IsActive is { } isActive)
        {
            query = query.Where(e => e.IsActive == isActive);
        }

        if (!string.IsNullOrWhiteSpace(paging.Search))
        {
            var term = paging.Search.Trim();
            // A lookup that has a code can be found by it too — the code is often the thing an
            // operator has in hand.
            query = typeof(ICodedLookup).IsAssignableFrom(typeof(TEntity))
                ? query.Where(e => EF.Functions.Like(e.NameEn, $"%{term}%")
                                   || EF.Functions.Like(e.NameAr, $"%{term}%")
                                   || EF.Functions.Like(EF.Property<string>(e, nameof(ICodedLookup.Code)), $"%{term}%"))
                : query.Where(e => EF.Functions.Like(e.NameEn, $"%{term}%")
                                   || EF.Functions.Like(e.NameAr, $"%{term}%"));
        }

        query = query.OrderBy(e => e.NameEn);

        return await query.ToPagedResultAsync(paging, map, cancellationToken);
    }

    /// <summary>Loads a row for editing, or reports 404.</summary>
    public async Task<TEntity> RequireAsync<TEntity>(
        DbSet<TEntity> set,
        Guid id,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var entity = await set.FirstOrDefaultAsync(
            BuildIdPredicate<TEntity>(id),
            cancellationToken);

        return entity ?? throw new NotFoundException(typeof(TEntity).Name, id);
    }

    /// <summary>
    /// Rejects a duplicate before the unique index does, so the admin sees a field-level message
    /// instead of a database error.
    /// </summary>
    public async Task EnsureUniqueAsync<TEntity>(
        DbSet<TEntity> set,
        Expression<Func<TEntity, bool>> duplicatePredicate,
        string code,
        string message,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        if (await set.AnyAsync(duplicatePredicate, cancellationToken))
        {
            throw new ConflictException(code, message);
        }
    }

    /// <summary>
    /// Removes a lookup row, but only when nothing references it. Lookups that are already in use
    /// are deactivated instead — deleting them would orphan historical applications that were
    /// priced and submitted against them.
    /// </summary>
    public async Task<LookupDeleteOutcome> DeleteOrDeactivateAsync<TEntity>(
        DbSet<TEntity> set,
        Guid id,
        Func<CancellationToken, Task<bool>> isReferenced,
        string auditAction,
        CancellationToken cancellationToken)
        where TEntity : LocalizedLookup
    {
        ArgumentNullException.ThrowIfNull(isReferenced);

        var entity = await RequireAsync(set, id, cancellationToken);

        if (await isReferenced(cancellationToken))
        {
            entity.IsActive = false;
            await _db.SaveChangesAsync(cancellationToken);
            await AuditAsync($"{auditAction}.Deactivated", typeof(TEntity).Name, id, null, cancellationToken);
            return LookupDeleteOutcome.Deactivated;
        }

        set.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        await AuditAsync($"{auditAction}.Deleted", typeof(TEntity).Name, id, null, cancellationToken);
        return LookupDeleteOutcome.Deleted;
    }

    public Task SaveAsync(CancellationToken cancellationToken) => _db.SaveChangesAsync(cancellationToken);

    public Task AuditAsync(
        string action,
        string entityType,
        Guid? entityId,
        object? data,
        CancellationToken cancellationToken) =>
        _auditLogger.LogAsync(action, entityType, entityId, data, cancellationToken);

    private static Expression<Func<TEntity, bool>> BuildIdPredicate<TEntity>(Guid id)
        where TEntity : class
    {
        var parameter = Expression.Parameter(typeof(TEntity), "e");
        var property = Expression.Property(parameter, nameof(Entity.Id));
        var body = Expression.Equal(property, Expression.Constant(id));
        return Expression.Lambda<Func<TEntity, bool>>(body, parameter);
    }
}

/// <summary>Tells the admin UI whether a row was removed or merely retired.</summary>
public enum LookupDeleteOutcome
{
    Deleted = 0,
    Deactivated = 1,
}
