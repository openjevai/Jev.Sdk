// NoulAnswer.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the answer union.
// See requirements/requirements.md, R3.
//
// Type: NoulAnswer

using System.Text.Json.Serialization;

namespace Jev.Sdk;

/// <summary>
/// The probability that a yes/no statement is true.
/// </summary>
/// <remarks>
/// Values near 1 favour yes, values near 0 favour no, and values near 0.5 mean the model gives
/// both equal weight. A value of 0.5 does not mean "medium" in any other sense; a spectrum
/// belongs in a <see cref="ScoreQuestion"/> with defined levels.
/// <para>
/// This answer carries no confidence. The probability already expresses the model's certainty,
/// and a derived number beside it would invite treating the two as interchangeable.
/// </para>
/// </remarks>
public sealed class NoulAnswer : Answer
{
    /// <inheritdoc />
    public override string Type => QuestionTypes.Noul;

    /// <summary>The probability of yes or true, from 0 to 1.</summary>
    [JsonPropertyName("noul")]
    public double Noul { get; set; }
}
