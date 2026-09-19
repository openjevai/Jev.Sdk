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
    /// <summary>Models and aliases available to the authenticated account.</summary>
    [JsonPropertyName("models")]
    public IList<ModelMetadata> Models { get; set; } = new List<ModelMetadata>();
}
