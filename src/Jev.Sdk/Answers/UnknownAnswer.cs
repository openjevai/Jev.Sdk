// UnknownAnswer.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the answer union.
// See requirements/requirements.md, R6.
//
// Type: UnknownAnswer

using System.Text.Json;

namespace Jev.Sdk;

/// <summary>
/// An answer whose kind this library does not model, preserved exactly as received.
/// </summary>
/// <remarks>
/// This is what makes the client forward compatible. When the API gains a new answer kind,
/// the answers this library does understand still arrive intact, and the unfamiliar one
/// arrives here with all of its data rather than failing the response.
/// </remarks>
public sealed class UnknownAnswer : Answer
{
    private readonly string _type;

    /// <summary>Initialises an unknown answer.</summary>
    /// <param name="type">The wire value of the <c>type</c> member.</param>
    /// <param name="rawJson">The complete original JSON for this answer.</param>
    public UnknownAnswer(string type, JsonElement rawJson)
    {
        ArgumentNullException.ThrowIfNull(type);
        _type = type;
        RawJson = rawJson.Clone();
    }

    /// <inheritdoc />
    public override string Type => _type;

    /// <summary>The complete original JSON for this answer, exactly as received.</summary>
    public JsonElement RawJson { get; }
}
