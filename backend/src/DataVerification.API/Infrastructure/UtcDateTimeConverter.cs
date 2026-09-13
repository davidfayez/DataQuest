using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DataVerification.API.Infrastructure;

/// <summary>
/// Writes every <see cref="DateTime"/> as ISO-8601 in UTC, with the trailing <c>Z</c>.
/// </summary>
/// <remarks>
/// Without the <c>Z</c> the value is ambiguous, and browsers resolve that ambiguity the wrong way:
/// ECMAScript parses a date-time string carrying no offset as <em>local</em> time. A UTC instant
/// therefore arrived in the browser as a local one, and every screen showed the UTC clock reading
/// while claiming it was the viewer's own.
///
/// Every timestamp this platform stores is UTC — the properties say so, and nothing writes
/// <c>DateTime.Now</c> — so a value that reaches here unlabelled is UTC that lost its label on the
/// way out of SQL Server, and marking it is a correction rather than an assumption.
/// </remarks>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        // An inbound value carrying its own offset is authoritative; one without is UTC by the
        // same contract the rest of the platform keeps.
        if (reader.TryGetDateTimeOffset(out var offset))
        {
            return offset.UtcDateTime;
        }

        return DateTime.SpecifyKind(reader.GetDateTime(), DateTimeKind.Utc);
    }

    public override void Write(
        Utf8JsonWriter writer,
        DateTime value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            // Unspecified: read back from a datetime2 column, which only ever holds UTC here.
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

        // Round-trip format, so the value on the wire is what it always was plus its label.
        writer.WriteStringValue(utc.ToString("O", CultureInfo.InvariantCulture));
    }
}
