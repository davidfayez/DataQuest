using System.Text.Json;
using DataVerification.API.Infrastructure;
using FluentAssertions;
using Xunit;

namespace DataVerification.UnitTests.Serialization;

/// <summary>
/// Every timestamp leaves the API as ISO-8601 UTC carrying its <c>Z</c>.
/// </summary>
/// <remarks>
/// This is not cosmetic. ECMAScript parses a date-time string with no offset as <em>local</em>
/// time, so an unlabelled UTC instant reached the browser as a local one and every screen showed
/// the UTC clock reading while claiming it was the viewer's own. The <c>Z</c> is the whole fix.
/// </remarks>
public sealed class UtcDateTimeConverterTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new UtcDateTimeConverter() },
    };

    private sealed record Stamped(DateTime CreatedAtUtc, DateTime? ReviewedAtUtc);

    [Fact]
    public void AUtcValueKeepsItsZ()
    {
        var value = new DateTime(2026, 8, 21, 15, 53, 45, DateTimeKind.Utc);

        var json = JsonSerializer.Serialize(new Stamped(value, null), Options);

        json.Should().Contain("2026-08-21T15:53:45.0000000Z");
    }

    [Fact]
    public void AnUnspecifiedValueIsLabelledRatherThanShifted()
    {
        // This is what EF hands back from a datetime2 column. The clock reading must survive
        // untouched — only the label is added.
        var value = new DateTime(2026, 8, 21, 15, 53, 45, DateTimeKind.Unspecified);

        var json = JsonSerializer.Serialize(new Stamped(value, null), Options);

        json.Should().Contain("2026-08-21T15:53:45.0000000Z");
    }

    [Fact]
    public void ALocalValueIsConvertedRatherThanRelabelled()
    {
        // A local value genuinely means a different instant, so it is converted, not stamped.
        var local = new DateTime(2026, 8, 21, 15, 53, 45, DateTimeKind.Local);

        var json = JsonSerializer.Serialize(new Stamped(local, null), Options);

        var expected = local.ToUniversalTime().ToString("O");
        json.Should().Contain(expected);
    }

    [Fact]
    public void NullablesAreCoveredToo()
    {
        var value = new DateTime(2026, 8, 21, 9, 0, 0, DateTimeKind.Unspecified);

        var json = JsonSerializer.Serialize(new Stamped(value, value), Options);

        // Both properties, not just the non-nullable one.
        json.Should().Contain("\"createdAtUtc\":\"2026-08-21T09:00:00.0000000Z\"");
        json.Should().Contain("\"reviewedAtUtc\":\"2026-08-21T09:00:00.0000000Z\"");
    }

    [Fact]
    public void ANullStaysNull()
    {
        var json = JsonSerializer.Serialize(
            new Stamped(new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc), null),
            Options);

        json.Should().Contain("\"reviewedAtUtc\":null");
    }

    [Fact]
    public void EveryWrittenValueRoundTripsBackToTheSameInstant()
    {
        var value = new DateTime(2026, 8, 21, 15, 53, 45, 123, DateTimeKind.Utc);

        var json = JsonSerializer.Serialize(new Stamped(value, value), Options);
        var read = JsonSerializer.Deserialize<Stamped>(json, Options)!;

        read.CreatedAtUtc.Should().Be(value);
        read.CreatedAtUtc.Kind.Should().Be(DateTimeKind.Utc);
        read.ReviewedAtUtc.Should().Be(value);
    }

    [Fact]
    public void AnInboundValueCarryingAnOffsetIsNormalisedToUtc()
    {
        // Somebody sending 18:53+03:00 means the same instant as 15:53Z, and that is what is kept.
        var json = """{"createdAtUtc":"2026-08-21T18:53:45+03:00","reviewedAtUtc":null}""";

        var read = JsonSerializer.Deserialize<Stamped>(json, Options)!;

        read.CreatedAtUtc.Should().Be(new DateTime(2026, 8, 21, 15, 53, 45, DateTimeKind.Utc));
        read.CreatedAtUtc.Kind.Should().Be(DateTimeKind.Utc);
    }
}
