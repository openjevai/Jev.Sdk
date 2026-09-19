// RawQuestion.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the question union.
// See requirements/requirements.md, R6.
//
// Type: RawQuestion

using System.Text.Json;

namespace Jev.Sdk;

/// <summary>
/// A question whose kind this library does not model, preserved exactly as received.
/// </summary>
/// <remarks>
/// This exists so that a question kind added to the API after this library was built does
/// not cause a failure. The original JSON is retained and can be read or round-tripped.
/// </remarks>
public sealed class RawQuestion : Question
{
    private readonly string _type;

    /// <summary>Initialises a raw question.</summary>
    /// <param name="type">The wire value of the <c>type</c> member.</param>
    /// <param name="rawJson">The complete original JSON for this question.</param>
    public RawQuestion(string type, JsonElement rawJson)
    {
        ArgumentNullException.ThrowIfNull(type);
        _type = type;
        RawJson = rawJson.Clone();
    }

    /// <inheritdoc />
    public override string Type => _type;

    /// <summary>The complete original JSON for this question, exactly as received.</summary>
    public JsonElement RawJson { get; }
}
