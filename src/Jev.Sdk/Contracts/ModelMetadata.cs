// ModelMetadata.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the wire contracts.
// See requirements/requirements.md, R2.
//
// Type: ModelMetadata

using System.Text.Json.Serialization;

namespace Jev.Sdk;

/// <summary>
/// A model or model alias available to the authenticated account.
/// </summary>
public sealed class ModelMetadata
{
    /// <summary>Model name or alias, usable in a request's model member.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Human-readable description of the model and its capabilities.</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Model release date as reported by the API, verbatim.
    /// </summary>
    /// <remarks>
    /// The specification's description and example both show <c>YYYY-MM-DD</c>, but the live service
    /// returns a full ISO-8601 timestamp, for example
    /// <c>2026-09-10T18:38:01.391457+00:00</c>. The value is kept exactly as received rather than
    /// normalized, so a caller comparing against the API's own output sees no surprise. Use
    /// <see cref="ParsedReleaseDate"/> to read the date itself.
    /// </remarks>
    [JsonPropertyName("release_date")]
    public string ReleaseDate { get; set; } = string.Empty;

    /// <summary>
    /// The release date parsed as a <see cref="DateOnly"/>, when it can be read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Computed here, not reported by the API, so it is excluded from serialization.
    /// </para>
    /// <para>
    /// Both shapes the service is known to send are accepted: a plain <c>YYYY-MM-DD</c> date, which is
    /// what the specification documents and examples show, and a full ISO-8601 timestamp, which is what
    /// the live service actually returns. Only the date component is used, so the time and offset are
    /// discarded rather than converted — a release date is a calendar day, and shifting it by the
    /// caller's local offset would report the wrong day for anyone west of UTC.
    /// </para>
    /// <para>
    /// Returns null for a value neither shape can be read from, rather than throwing, because this is a
    /// convenience property and a caller inspecting a model list should not have to guard it.
    /// </para>
    /// </remarks>
    [JsonIgnore]
    public DateOnly? ParsedReleaseDate =>
        DateOnly.TryParseExact(
            ReleaseDate,
            "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out DateOnly parsed)
            ? parsed
            : ParseTimestampDate(ReleaseDate);

    /// <summary>
    /// Reads the date component from a full ISO-8601 timestamp, or returns null.
    /// </summary>
    /// <param name="releaseDate">The value exactly as the API reported it.</param>
    /// <returns>The date component, or null when the value is not a readable timestamp.</returns>
    /// <remarks>
    /// Tries the round-trip format first because that is what the service sends
    /// (<c>2026-09-10T18:38:01.391457+00:00</c>), then a general parse for a timestamp without an
    /// offset. The date is taken from the parsed value's own date part rather than from the local time,
    /// so the offset in the payload does not move the day.
    /// </remarks>
    private static DateOnly? ParseTimestampDate(string releaseDate)
    {
        if (string.IsNullOrWhiteSpace(releaseDate))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(
            releaseDate,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out DateTimeOffset offset))
        {
            return DateOnly.FromDateTime(offset.Date);
        }

        return null;
    }
}
