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
    /// Model release date as reported by the API, formatted <c>YYYY-MM-DD</c>.
    /// </summary>
    [JsonPropertyName("release_date")]
    public string ReleaseDate { get; set; } = string.Empty;

    /// <summary>
    /// The release date parsed as a <see cref="DateOnly"/>, when it is well formed.
    /// </summary>
    /// <remarks>
    /// Computed here, not reported by the API, so it is excluded from serialization.
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
            : null;
}
