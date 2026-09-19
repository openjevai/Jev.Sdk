// SystemOneResponse.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the wire contracts.
// See requirements/requirements.md, R1, R3, R6 and R7.
//
// Type: SystemOneResponse

using System.Text.Json.Serialization;

namespace Jev.Sdk;

/// <summary>
/// The body returned by <c>POST /v1/systemone</c>: one answer per question, the model that produced
/// them, and token usage.
/// </summary>
public sealed class SystemOneResponse : JevResponse
{
    private static readonly Dictionary<string, Answer> s_noAnswers = new(StringComparer.Ordinal);

    private IDictionary<string, Answer>? _answers =
        new Dictionary<string, Answer>(StringComparer.Ordinal);

    /// <summary>
    /// The model that answered. May differ from the alias supplied in the request, because an alias
    /// such as <c>jev-latest</c> can resolve to a dated model name.
    /// </summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// The answers as deserialized, or null when the server sent a JSON null for the member.
    /// </summary>
    /// <remarks>
    /// This is the raw view, kept nullable so a caller can tell a null apart from an empty result.
    /// Prefer <see cref="AnswersOrEmpty"/> for reading, and note that the wire member is written from
    /// a different property: see <see cref="AnswersForWire"/>.
    /// </remarks>
    [JsonIgnore]
    public IDictionary<string, Answer>? Answers
    {
        get => _answers;
        set => _answers = value;
    }

    /// <summary>
    /// The answers, never null. An empty dictionary when the server reported none or nulled the
    /// member.
    /// </summary>
    [JsonIgnore]
    public IDictionary<string, Answer> AnswersOrEmpty => _answers ?? s_noAnswers;

    /// <summary>
    /// The wire member for <c>answers</c>, which the specification declares required.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Answers"/> for one reason: serialization must always emit the member.
    /// A null in <see cref="Answers"/> would otherwise be dropped by the
    /// <c>WhenWritingNull</c> policy, producing a body that omits a required field — a round trip that
    /// silently emits invalid JSON. Writing through this property means the member is present on every
    /// serialized response, as an empty object when there are no answers.
    /// </remarks>
    [JsonPropertyName("answers")]
    public IDictionary<string, Answer> AnswersForWire
    {
        get => _answers ?? s_noAnswers;
        set => _answers = value;
    }

    /// <summary>Token usage for this evaluation.</summary>
    [JsonPropertyName("usage")]
    public Usage? Usage { get; set; }

    /// <summary>
    /// Returns the answer stored under <paramref name="questionId"/>.
    /// </summary>
    /// <param name="questionId">The name supplied as a question key.</param>
    /// <exception cref="KeyNotFoundException">No answer was returned under that name.</exception>
    public Answer this[string questionId]
    {
        get
        {
            IDictionary<string, Answer> answers = AnswersOrEmpty;

            return answers.TryGetValue(questionId, out Answer? answer)
                ? answer
                : throw new KeyNotFoundException(
                    $"No answer was returned for question '{questionId}'. "
                    + $"Returned questions: {string.Join(", ", answers.Keys)}.");
        }
    }
}
