// Question.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the question union.
// See requirements/requirements.md, R3, R6 and R14.
//
// Type: Question

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jev.Sdk;

/// <summary>
/// A question about the supplied state. Create one of the three concrete kinds:
/// <see cref="NoulQuestion"/>, <see cref="ChoiceQuestion"/>, or <see cref="ScoreQuestion"/>.
/// </summary>
/// <remarks>
/// Deserializing an answer whose kind this library does not model yields a
/// <see cref="RawQuestion"/>, which preserves the original JSON. A question kind added to
/// the API after this library was built therefore does not fail; it simply arrives
/// unmodelled.
/// </remarks>
[JsonConverter(typeof(QuestionJsonConverter))]
public abstract class Question
{
    /// <summary>The wire value of this question's kind: <c>noul</c>, <c>choice</c>, or <c>score</c>.</summary>
    [JsonPropertyName("type")]
    public abstract string Type { get; }

    /// <summary>
    /// Any JSON members present on the wire that this library does not model. Preserved so
    /// that a round trip does not discard information.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }

    /// <summary>
    /// Creates a yes/no question: the answer is the probability that the statement is true.
    /// </summary>
    /// <param name="instructions">What to evaluate.</param>
    /// <param name="criteria">
    /// Optional clarification of what counts as yes and no. Either or both members may be
    /// null.
    /// </param>
    /// <returns>A question to place in a request's question map.</returns>
    public static NoulQuestion Noul(StructuredValue? instructions, NoulCriteria? criteria = null) =>
        new() { Instructions = instructions, Criteria = criteria };

    /// <summary>
    /// Creates a question that selects one option from a set. The answer includes the chosen
    /// option, its probability distribution, and a confidence value.
    /// </summary>
    /// <param name="instructions">What the model should decide.</param>
    /// <param name="criteria">
    /// Option names and descriptions. A null description means the option is judged by its
    /// name alone. At least one option is required.
    /// </param>
    /// <returns>A question to place in a request's question map.</returns>
    public static ChoiceQuestion Choice(StructuredValue? instructions, IDictionary<string, StructuredValue?> criteria) =>
        new() { Instructions = instructions, Criteria = criteria };

    /// <summary>
    /// Creates a question that rates the state along ordered levels. The answer is a
    /// probability-weighted position on that scale.
    /// </summary>
    /// <param name="instructions">What the model should rate.</param>
    /// <param name="criteria">
    /// Level descriptions in ascending order. Position determines the level number, starting
    /// at zero. At least two levels are required for a meaningful result.
    /// </param>
    /// <returns>A question to place in a request's question map.</returns>
    public static ScoreQuestion Score(StructuredValue? instructions, params StructuredValue[] criteria) =>
        new() { Instructions = instructions, Criteria = criteria };

    /// <summary>Creates a Score question from a sequence of level descriptions.</summary>
    /// <param name="instructions">What the model should rate.</param>
    /// <param name="criteria">Level descriptions in ascending order.</param>
    /// <returns>A question to place in a request's question map.</returns>
    public static ScoreQuestion Score(StructuredValue? instructions, IEnumerable<StructuredValue> criteria) =>
        new() { Instructions = instructions, Criteria = criteria.ToArray() };

    /// <summary>Creates a Score question from plain level descriptions.</summary>
    /// <param name="instructions">What the model should rate, as plain text.</param>
    /// <param name="levels">Level descriptions in ascending order.</param>
    /// <returns>A question to place in a request's question map.</returns>
    public static ScoreQuestion Score(string instructions, params string[] levels) =>
        new()
        {
            Instructions = StructuredValue.FromString(instructions),
            Criteria = [.. levels.Select(StructuredValue.FromString)],
        };

    /// <summary>Creates a Choice question from plain option descriptions.</summary>
    /// <param name="instructions">What the model should decide, as plain text.</param>
    /// <param name="options">Option names and descriptions. A null description is allowed.</param>
    /// <returns>A question to place in a request's question map.</returns>
    public static ChoiceQuestion Choice(string instructions, IDictionary<string, string?> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Dictionary<string, StructuredValue?> criteria = new(options.Count, StringComparer.Ordinal);

        foreach (KeyValuePair<string, string?> option in options)
        {
            criteria[option.Key] = StructuredValue.FromString(option.Value);
        }

        return new ChoiceQuestion
        {
            Instructions = StructuredValue.FromString(instructions),
            Criteria = criteria,
        };
    }

    /// <summary>Creates a Noul question from plain text instructions.</summary>
    /// <param name="instructions">The yes/no question or statement to evaluate.</param>
    /// <returns>A question to place in a request's question map.</returns>
    public static NoulQuestion Noul(string instructions) =>
        new() { Instructions = StructuredValue.FromString(instructions) };
}
