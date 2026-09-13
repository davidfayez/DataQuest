using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace DataVerification.Application.Common.Models;

/// <summary>One page of results plus the totals the admin tables need to render their pager.</summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPrevious => Page > 1;

    public bool HasNext => Page < TotalPages;

    public static PagedResult<T> Empty(int page, int pageSize) => new([], page, pageSize, 0);
}

/// <summary>Paging and search parameters shared by every admin list endpoint.</summary>
public record PagedQuery
{
    private const int MaxPageSize = 200;

    private int _pageSize = 25;
    private int _page = 1;

    public int Page
    {
        get => _page;
        init => _page = value < 1 ? 1 : value;
    }

    /// <summary>Clamped so a client cannot ask for the whole table in one request.</summary>
    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = value switch
        {
            < 1 => 25,
            > MaxPageSize => MaxPageSize,
            _ => value,
        };
    }

    /// <summary>Free-text filter, matched against the Arabic and English names.</summary>
    public string? Search { get; init; }

    /// <summary>Null returns both active and inactive rows.</summary>
    public bool? IsActive { get; init; }

    /// <summary>
    /// The column to order by, named as the client knows it — <c>nameEn</c>, <c>code</c>,
    /// <c>createdAtUtc</c>. Matched case-insensitively against the entity's own properties, and
    /// ignored when it names nothing sortable.
    /// </summary>
    /// <remarks>
    /// Sorting has to happen here rather than in the browser. A table shows one page of twenty-five
    /// out of however many rows there are, so sorting what has already been fetched reorders that
    /// page and nothing else — ask for countries Z-first and you get the last of the A's.
    /// </remarks>
    public string? SortBy { get; init; }

    public bool SortDescending { get; init; }
}

public static class QueryableExtensions
{
    /// <summary>Runs the count and page queries and packages them for the client.</summary>
    /// <param name="sortAliases">
    /// Column names the client uses that differ from the entity's own. See <see cref="ApplySort"/>.
    /// </param>
    public static async Task<PagedResult<TResult>> ToPagedResultAsync<TSource, TResult>(
        this IQueryable<TSource> source,
        PagedQuery paging,
        Func<TSource, TResult> selector,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? sortAliases = null)
    {
        ArgumentNullException.ThrowIfNull(paging);
        ArgumentNullException.ThrowIfNull(selector);

        var totalCount = await source.CountAsync(cancellationToken);

        var items = await source
            .ApplySort(paging, sortAliases)
            .Skip((paging.Page - 1) * paging.PageSize)
            .Take(paging.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<TResult>(
            items.Select(selector).ToList(),
            paging.Page,
            paging.PageSize,
            totalCount);
    }

    /// <summary>
    /// The same, for a list whose rows are built in the database rather than in memory — one that
    /// counts a related table, say.
    /// </summary>
    /// <remarks>
    /// The projection is applied <em>after</em> ordering and paging, which is the whole point of
    /// the overload: ordering a queryable that has already been projected makes the projection
    /// itself the sort key, and a projection containing a subquery cannot be put in an ORDER BY.
    /// </remarks>
    public static async Task<PagedResult<TResult>> ToPagedResultInSqlAsync<TSource, TResult>(
        this IQueryable<TSource> source,
        PagedQuery paging,
        Expression<Func<TSource, TResult>> projection,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? sortAliases = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(paging);
        ArgumentNullException.ThrowIfNull(projection);

        var totalCount = await source.CountAsync(cancellationToken);

        var items = await source
            .ApplySort(paging, sortAliases)
            .Skip((paging.Page - 1) * paging.PageSize)
            .Take(paging.PageSize)
            .Select(projection)
            .ToListAsync(cancellationToken);

        return new PagedResult<TResult>(items, paging.Page, paging.PageSize, totalCount);
    }

    /// <summary>
    /// Orders by the column the client asked for, ahead of whatever default the handler applied.
    /// </summary>
    /// <remarks>
    /// The name is resolved against the entity's own public properties, so it can only ever name
    /// something the type really has — a caller cannot reach into arbitrary SQL through it. A name
    /// that matches nothing, or matches something that is not a column (a collection, a navigation
    /// to another entity), is ignored and the handler's own ordering stands. That is deliberate:
    /// a column the database cannot order by should leave the list alone rather than fail the
    /// request, and the front end marks those columns unsortable anyway.
    ///
    /// Chained as an extra <c>OrderBy</c> rather than replacing the existing one, which in LINQ
    /// makes it the primary key and leaves the handler's ordering as the tie-breaker — so rows
    /// that compare equal keep a stable, predictable order across pages.
    /// </remarks>
    /// <param name="aliases">
    /// Column names that do not match the entity's own — a list whose rows are projected into a
    /// shape of their own before they reach the client. <c>requestedAtUtc</c> on the screen may be
    /// <c>CreatedAtUtc</c> in the table.
    /// </param>
    public static IQueryable<T> ApplySort<T>(
        this IQueryable<T> source,
        PagedQuery paging,
        IReadOnlyDictionary<string, string>? aliases = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(paging);

        if (string.IsNullOrWhiteSpace(paging.SortBy))
        {
            return source;
        }

        var requested = paging.SortBy.Trim();

        if (aliases is not null && aliases.TryGetValue(requested, out var mapped))
        {
            requested = mapped;
        }

        var property = typeof(T).GetProperty(
            requested,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (property is null || !IsSortable(property.PropertyType))
        {
            return source;
        }

        var parameter = Expression.Parameter(typeof(T), "e");
        var member = Expression.MakeMemberAccess(parameter, property);
        var keySelector = Expression.Lambda(member, parameter);

        var method = paging.SortDescending ? "OrderByDescending" : "OrderBy";

        var call = Expression.Call(
            typeof(Queryable),
            method,
            [typeof(T), property.PropertyType],
            source.Expression,
            Expression.Quote(keySelector));

        return source.Provider.CreateQuery<T>(call);
    }

    /// <summary>
    /// True for the types a database can put in an ORDER BY: text, numbers, dates, booleans and
    /// enums, plus their nullable forms. Everything else — a collection, another entity — is not
    /// something to order by.
    /// </summary>
    private static bool IsSortable(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        return underlying.IsEnum
               || underlying == typeof(string)
               || underlying == typeof(Guid)
               || underlying == typeof(DateTime)
               || underlying == typeof(DateTimeOffset)
               || underlying == typeof(DateOnly)
               || underlying == typeof(TimeOnly)
               || underlying == typeof(bool)
               || (underlying.IsPrimitive && underlying != typeof(nint) && underlying != typeof(nuint))
               || underlying == typeof(decimal);
    }
}
