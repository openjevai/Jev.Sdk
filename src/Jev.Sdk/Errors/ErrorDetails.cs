// ErrorDetails.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for error mapping.
// See requirements/requirements.md, R7.
//
// Type: ErrorDetails

using System.Text.Json.Serialization;

namespace Jev.Sdk;

/// <summary>
/// One entry from the <c>detail</c> array of a 422 validation failure, identifying which
/// part of the request the server rejected and why.
/// </summary>
public sealed class ErrorDetails
{
    /// <summary>
    /// Path to the invalid value: the request location followed by field names and array
    /// indices, for example <c>["body", "questions", "urgency", "score", "criteria"]</c>.
    /// </summary>
    [JsonPropertyName("loc")]
    public IList<object> Location { get; set; } = new List<object>();

    /// <summary>Human-readable explanation of the failure.</summary>
    [JsonPropertyName("msg")]
    public string Message { get; set; } = string.Empty;

    /// <summary>Machine-readable validation error code, such as <c>missing</c>.</summary>
    [JsonPropertyName("type")]
    public string ErrorType { get; set; } = string.Empty;

    /// <summary>The input value that failed validation, when the server includes it.</summary>
    [JsonPropertyName("input")]
    public JsonElementBox? Input { get; set; }

    /// <summary>Additional context for the failure, when the server includes it.</summary>
    [JsonPropertyName("ctx")]
    public JsonElementBox? Context { get; set; }

    /// <summary>
    /// The location rendered as a dotted path, for example
    /// <c>body.questions.urgency.score.criteria</c>.
    /// </summary>
    public string LocationPath => string.Join('.', Location.Select(static part => part?.ToString() ?? string.Empty));

    /// <inheritdoc />
    public override string ToString() => $"{LocationPath}: {Message} ({ErrorType})";
}
