// ScoreQuestion.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the question union.
// See requirements/requirements.md, R3 and R4.
//
// Type: ScoreQuestion

using System.Text.Json.Serialization;

namespace Jev.Sdk;

/// <summary>
/// A question that rates the state along ordered levels you define.
/// </summary>
/// <remarks>
/// Position in <see cref="Criteria"/> determines the level number, starting at zero. The
/// answer is a probability-weighted average across those levels and may fall between them.
/// </remarks>
public sealed class ScoreQuestion : Question
{
    /// <inheritdoc />
    public override string Type => QuestionTypes.Score;

    /// <summary>What the model should rate. May be plain text or structured JSON.</summary>
    [JsonPropertyName("instructions")]
    public StructuredValue? Instructions { get; set; }

    /// <summary>
    /// Level descriptions in ascending order. The position of a description determines its
    /// level number, starting at zero.
    /// </summary>
    [JsonPropertyName("criteria")]
    public IList<StructuredValue> Criteria { get; set; } = new List<StructuredValue>();
}
