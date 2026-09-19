// Usage.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the wire contracts.
// See requirements/requirements.md, R11.
//
// Type: Usage

using System.Text.Json.Serialization;

namespace Jev.Sdk;

/// <summary>
/// Token usage reported for a request.
/// </summary>
/// <remarks>
/// Output tokens are currently free of charge. This is worth surfacing in telemetry rather
/// than only in logs, because input tokens are what a caller pays for.
/// </remarks>
public sealed class Usage
{
    /// <summary>Billable input tokens used to evaluate the request.</summary>
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; set; }

    /// <summary>Output tokens used to answer the questions.</summary>
    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; set; }

    /// <summary>
    /// Total tokens across input and output.
    /// </summary>
    /// <remarks>
    /// Computed here, not reported by the API, so it is excluded from serialization. Writing it back
    /// would invent a field the service does not define.
    /// </remarks>
    [JsonIgnore]
    public int TotalTokens => InputTokens + OutputTokens;
}
