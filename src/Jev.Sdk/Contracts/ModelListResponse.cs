// ModelListResponse.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the wire contracts.
// See requirements/requirements.md, R2.
//
// Type: ModelListResponse

using System.Text.Json.Serialization;

namespace Jev.Sdk;

/// <summary>
/// The body returned by <c>GET /v1/models</c>.
/// </summary>
public sealed class ModelListResponse : JevResponse
{
    /// <summary>
    /// Models and aliases available to the authenticated account.
    /// </summary>
    /// <remarks>
    /// Settable to null because a JSON null is a legal value for the member, and deserialization will
    /// produce one. Callers and the client both treat a null as "no models reported" rather than as a
    /// fault, so it never surfaces as an ArgumentNullException.
    /// </remarks>
    [JsonPropertyName("models")]
    public IList<ModelMetadata>? Models { get; set; } = new List<ModelMetadata>();

    /// <summary>The models, never null. An empty list when the server reported none.</summary>
    [JsonIgnore]
    public IReadOnlyList<ModelMetadata> ModelsOrEmpty => Models is null ? [] : [.. Models];
}
