// ValidationErrorResponse.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for error mapping.
// See requirements/requirements.md, R7.
//
// Type: ValidationErrorResponse

using System.Text.Json.Serialization;

namespace Jev.Sdk;

/// <summary>
/// The body returned with a 422 response.
/// </summary>
public sealed class ValidationErrorResponse
{
    /// <summary>Validation failures describing which request values are missing or invalid.</summary>
    [JsonPropertyName("detail")]
    public IList<ErrorDetails> Detail { get; set; } = new List<ErrorDetails>();
}
