// ChoiceQuestion.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the question union.
// See requirements/requirements.md, R3 and R4.
//
// Type: ChoiceQuestion

using System.Text.Json.Serialization;

namespace Jev.Sdk;

/// <summary>
/// A question that selects one option from a set you define.
/// </summary>
/// <remarks>
/// The answer names the highest-probability option and reports the full distribution plus a
/// confidence value. Give the complete list of options, and include an option such as
/// <c>other</c> when the list might not cover every input.
/// </remarks>
public sealed class ChoiceQuestion : Question
{
    /// <inheritdoc />
    public override string Type => QuestionTypes.Choice;

    /// <summary>What the model should decide. May be plain text or structured JSON.</summary>
    [JsonPropertyName("instructions")]
    public StructuredValue? Instructions { get; set; }

    /// <summary>
    /// Option names mapped to descriptions. A null description means the option is judged by
    /// its name alone. Order is not significant; this is not a ranked list.
    /// </summary>
    [JsonPropertyName("criteria")]
    public IDictionary<string, StructuredValue?> Criteria { get; set; } =
        new Dictionary<string, StructuredValue?>(StringComparer.Ordinal);
}
