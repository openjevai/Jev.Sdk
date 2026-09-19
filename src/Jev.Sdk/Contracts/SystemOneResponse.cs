// SystemOneResponse.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the wire contracts.
// See requirements/requirements.md, R1 and R6.
//
// Type: SystemOneResponse

using System.Text.Json.Serialization;

namespace Jev.Sdk;

/// <summary>
/// The body returned by <c>POST /v1/systemone</c>: one answer per question, the model that
/// produced them, and token usage.
/// </summary>
public sealed class SystemOneResponse
{
    /// <summary>
    /// The model that answered. May differ from the alias supplied in the request, because an
    /// alias such as <c>jev-latest</c> can resolve to a dated model name.
    /// </summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Answers keyed by the question names supplied in the request. Each answer's kind
    /// matches its question's kind.
    /// </summary>
    [JsonPropertyName("answers")]
    public IDictionary<string, Answer> Answers { get; set; } =
        new Dictionary<string, Answer>(StringComparer.Ordinal);

    /// <summary>Token usage for this evaluation.</summary>
    [JsonPropertyName("usage")]
    public Usage? Usage { get; set; }

    /// <summary>
    /// Returns the answer stored under <paramref name="questionId"/>.
    /// </summary>
    /// <param name="questionId">The name supplied as a question key.</param>
    /// <exception cref="KeyNotFoundException">No answer was returned under that name.</exception>
    public Answer this[string questionId] =>
        Answers.TryGetValue(questionId, out Answer? answer)
            ? answer
            : throw new KeyNotFoundException(
                $"No answer was returned for question '{questionId}'. Returned questions: {string.Join(", ", Answers.Keys)}.");
}
