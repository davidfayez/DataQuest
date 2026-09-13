using DataVerification.Application.Common.Models;
using FluentAssertions;

namespace DataVerification.UnitTests.Common;

/// <summary>
/// Ordering a list by a column the client named.
///
/// The name arrives from a browser, so the rule that matters is what happens to a name that does
/// not describe a column: it is ignored and the handler's own ordering stands, rather than
/// throwing or — far worse — being taken at face value.
/// </summary>
public sealed class ApplySortTests
{
    private sealed class Row
    {
        public string Name { get; init; } = string.Empty;

        public int Rank { get; init; }

        public bool IsActive { get; init; }

        public DateTime CreatedAtUtc { get; init; }

        /// <summary>A collection is not something a database can order by.</summary>
        public List<string> Tags { get; init; } = [];
    }

    private static readonly Row[] Rows =
    [
        new() { Name = "Charlie", Rank = 2, IsActive = true, CreatedAtUtc = new DateTime(2026, 3, 1) },
        new() { Name = "alpha", Rank = 3, IsActive = false, CreatedAtUtc = new DateTime(2026, 1, 1) },
        new() { Name = "Bravo", Rank = 1, IsActive = true, CreatedAtUtc = new DateTime(2026, 2, 1) },
    ];

    private static IQueryable<Row> Source => Rows.AsQueryable();

    private static string[] Names(IQueryable<Row> rows) => rows.Select(r => r.Name).ToArray();

    [Fact]
    public void OrdersByTheNamedColumn()
    {
        Names(Source.ApplySort(new PagedQuery { SortBy = "Rank" }))
            .Should().Equal("Bravo", "Charlie", "alpha");
    }

    [Fact]
    public void OrdersDescendingWhenAsked()
    {
        Names(Source.ApplySort(new PagedQuery { SortBy = "Rank", SortDescending = true }))
            .Should().Equal("alpha", "Charlie", "Bravo");
    }

    [Fact]
    public void MatchesTheNameHoweverItIsCased()
    {
        // The browser sends camelCase; the property is PascalCase.
        Names(Source.ApplySort(new PagedQuery { SortBy = "rank" }))
            .Should().Equal("Bravo", "Charlie", "alpha");
    }

    [Fact]
    public void SortsDatesAndFlagsToo()
    {
        Names(Source.ApplySort(new PagedQuery { SortBy = "createdAtUtc" }))
            .Should().Equal("alpha", "Bravo", "Charlie");

        Names(Source.ApplySort(new PagedQuery { SortBy = "isActive" }))
            .First().Should().Be("alpha", "false sorts before true");
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("Name; DROP TABLE Countries")]
    [InlineData("../../etc/passwd")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void LeavesTheListAloneWhenTheNameDescribesNothing(string? sortBy)
    {
        // Unchanged order, and no exception: a column the database cannot order by should not be
        // able to fail the request, let alone reach it.
        Names(Source.ApplySort(new PagedQuery { SortBy = sortBy }))
            .Should().Equal("Charlie", "alpha", "Bravo");
    }

    [Fact]
    public void RefusesToOrderBySomethingThatIsNotAColumn()
    {
        Names(Source.ApplySort(new PagedQuery { SortBy = "Tags" }))
            .Should().Equal("Charlie", "alpha", "Bravo");
    }

    [Fact]
    public void TakesPrecedenceOverTheHandlersOwnOrdering()
    {
        // Handlers order their lists sensibly by default; an explicit sort has to win, with the
        // default left as the tie-breaker.
        var defaultOrdered = Source.OrderBy(r => r.Name);

        Names(defaultOrdered.ApplySort(new PagedQuery { SortBy = "Rank" }))
            .Should().Equal("Bravo", "Charlie", "alpha");
    }
}
