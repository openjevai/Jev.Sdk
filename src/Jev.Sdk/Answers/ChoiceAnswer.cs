// ChoiceAnswer.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the answer union.
// See requirements/requirements.md, R3.
//
// Type: ChoiceAnswer

using System.Text.Json.Serialization;

namespace Jev.Sdk;

/// <summary>
/// The selected option, the probability of every option, and a confidence value.
/// </summary>
public sealed class ChoiceAnswer : Answer
{
    /// <inheritdoc />
    public override string Type => QuestionTypes.Choice;

    /// <summary>The option with the highest probability among the question's criteria.</summary>
    [JsonPropertyName("choice")]
    public string Choice { get; set; } = string.Empty;

    /// <summary>
    /// Probability of each option, keyed by option name. Values sum to approximately 1.
    /// </summary>
    [JsonPropertyName("probabilities")]
    public IDictionary<string, double> Probabilities { get; set; } =
        new Dictionary<string, double>(StringComparer.Ordinal);
}
