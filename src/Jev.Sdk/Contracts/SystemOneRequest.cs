// SystemOneRequest.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the wire contracts.
// See requirements/requirements.md, R1, R5 and R6.
//
// Type: SystemOneRequest

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jev.Sdk;

/// <summary>
/// The body of a call to <c>POST /v1/systemone</c>: one state, one model, and one or more named
/// questions.
/// </summary>
public sealed class SystemOneRequest
{
    /// <summary>
    /// The content every question in this request refers to. May be plain text, an object, or an
    /// array. All questions see the same state and are evaluated independently.
    /// </summary>
    [JsonPropertyName("state")]
    public StructuredValue State { get; set; } = StructuredValue.Null;

    /// <summary>Name or alias of the model to use.</summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Questions to ask, keyed by a name you choose. The response identifies each answer by the
    /// same name. The key is never sent to the model and does not affect inference.
    /// </summary>
    [JsonPropertyName("questions")]
    public IDictionary<string, Question> Questions { get; set; } =
        new Dictionary<string, Question>(StringComparer.Ordinal);

    /// <summary>
    /// Any additional JSON members to send alongside the declared ones.
    /// </summary>
    /// <remarks>
    /// The vendor's SDKs expose this as <c>extra_body</c>, and their documentation describes
    /// unrecognised fields as a forward-compatibility escape hatch for options the SDK does not yet
    /// model. Without an equivalent here, a caller could not use a request field the API supports
    /// before this library models it.
    /// </remarks>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}
