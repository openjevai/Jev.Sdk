// Answer.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the answer union.
// See requirements/requirements.md, R3 and R6.
//
// Type: Answer

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jev.Sdk;

/// <summary>
/// An answer to a question. The concrete kind always corresponds to the question that
/// produced it: a <c>noul</c> question yields a <see cref="NoulAnswer"/>, and so on.
/// </summary>
/// <remarks>
/// An answer whose kind this library does not model arrives as an
/// <see cref="UnknownAnswer"/>, preserving the original JSON. A new answer kind therefore
/// does not fail the response, and the other answers in the same payload are unaffected.
/// </remarks>
[JsonConverter(typeof(AnswerJsonConverter))]
public abstract class Answer
{
    /// <summary>The wire value of this answer's kind: <c>noul</c>, <c>choice</c>, or <c>score</c>.</summary>
    [JsonPropertyName("type")]
    public abstract string Type { get; }

    /// <summary>
    /// How certain the model is, from 0 to 1, when the API reports one.
    /// </summary>
    /// <remarks>
    /// A Choice or Score answer carries a confidence derived from its probability
    /// distribution. A Noul answer carries none, and this member stays null for one, because
    /// the probability already expresses the model's certainty and a second number would
    /// invite treating it as a threshold. The value is reported exactly as received and is
    /// never recomputed here.
    /// </remarks>
    [JsonPropertyName("confidence")]
    public double? Confidence { get; set; }

    /// <summary>
    /// Any JSON members present on the wire that this library does not model.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }

    /// <summary>
    /// Returns this answer as a <see cref="NoulAnswer"/>.
    /// </summary>
    /// <exception cref="InvalidCastException">This answer is a different kind.</exception>
    public NoulAnswer AsNoul() => this as NoulAnswer
        ?? throw new InvalidCastException($"This answer is a '{Type}' answer, not a '{QuestionTypes.Noul}' answer.");

    /// <summary>
    /// Returns this answer as a <see cref="ChoiceAnswer"/>.
    /// </summary>
    /// <exception cref="InvalidCastException">This answer is a different kind.</exception>
    public ChoiceAnswer AsChoice() => this as ChoiceAnswer
        ?? throw new InvalidCastException($"This answer is a '{Type}' answer, not a '{QuestionTypes.Choice}' answer.");

    /// <summary>
    /// Returns this answer as a <see cref="ScoreAnswer"/>.
    /// </summary>
    /// <exception cref="InvalidCastException">This answer is a different kind.</exception>
    public ScoreAnswer AsScore() => this as ScoreAnswer
        ?? throw new InvalidCastException($"This answer is a '{Type}' answer, not a '{QuestionTypes.Score}' answer.");
}
