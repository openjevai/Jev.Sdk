// ScoreAnswer.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the answer union.
// See requirements/requirements.md, R3.
//
// Type: ScoreAnswer

using System.Text.Json.Serialization;

namespace Jev.Sdk;

/// <summary>
/// A rating along the levels the question defined, with the rubric, the distribution, and a
/// confidence value.
/// </summary>
public sealed class ScoreAnswer : Answer
{
    /// <inheritdoc />
    public override string Type => QuestionTypes.Score;

    /// <summary>
    /// The probability-weighted average of the rubric levels. May fall between levels, and is
    /// never rounded by this library.
    /// </summary>
    [JsonPropertyName("score")]
    public double Score { get; set; }

    /// <summary>
    /// The question's levels, keyed by level number as a string, so a score can be read back in
    /// context.
    /// </summary>
    [JsonPropertyName("legend")]
    public IDictionary<string, StructuredValue> Legend { get; set; } =
        new Dictionary<string, StructuredValue>(StringComparer.Ordinal);

    /// <summary>
    /// Probability of each level, using the same string keys as <see cref="Legend"/>. Values sum
    /// to approximately 1.
    /// </summary>
    [JsonPropertyName("probabilities")]
    public IDictionary<string, double> Probabilities { get; set; } =
        new Dictionary<string, double>(StringComparer.Ordinal);

    /// <summary>
    /// Returns the description of the level nearest the reported score, or null when the legend
    /// is empty.
    /// </summary>
    /// <remarks>
    /// A convenience for display. The score itself remains the authoritative value and is
    /// unaffected; use <see cref="Probabilities"/> when the distinction matters.
    /// </remarks>
    public StructuredValue? NearestLevelDescription()
    {
        if (Legend.Count == 0)
        {
            return null;
        }

        StructuredValue? nearest = null;
        double nearestDistance = double.MaxValue;

        foreach (KeyValuePair<string, StructuredValue> level in Legend)
        {
            if (!int.TryParse(
                    level.Key,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int index))
            {
                continue;
            }

            double distance = Math.Abs(Score - index);

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = level.Value;
            }
        }

        return nearest;
    }
}
