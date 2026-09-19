// NoulQuestion.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the question union.
// See requirements/requirements.md, R3 and R4.
//
// Type: NoulQuestion

using System.Text.Json.Serialization;

namespace Jev.Sdk;

/// <summary>
/// A yes/no question, or a statement to be judged true or false.
/// </summary>
/// <remarks>
/// The answer is a single probability. It carries no confidence value, because a
/// probability already is one; see <see cref="NoulAnswer"/>.
/// </remarks>
public sealed class NoulQuestion : Question
{
    /// <inheritdoc />
    public override string Type => QuestionTypes.Noul;

    /// <summary>
    /// The yes/no question or statement to evaluate. May be plain text or structured JSON.
    /// </summary>
    [JsonPropertyName("instructions")]
    public StructuredValue? Instructions { get; set; }

    /// <summary>
    /// Optional clarification of what counts as yes and what counts as no.
    /// </summary>
    [JsonPropertyName("criteria")]
    public NoulCriteria? Criteria { get; set; }
}

/// <summary>
/// Clarifies what a yes and a no mean for a <see cref="NoulQuestion"/>.
/// </summary>
public sealed class NoulCriteria
{
    /// <summary>What counts as a yes. May be plain text or structured JSON.</summary>
    [JsonPropertyName("true")]
    public StructuredValue? True { get; set; }

    /// <summary>What counts as a no. May be plain text or structured JSON.</summary>
    [JsonPropertyName("false")]
    public StructuredValue? False { get; set; }
}
