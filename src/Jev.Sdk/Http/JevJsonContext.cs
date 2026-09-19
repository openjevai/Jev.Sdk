// JevJsonContext.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for serialization.
// See requirements/requirements.md, R14.
//
// Type: JevJsonContext
//
// The single source-generated serialization context. Every type the library puts on the wire
// is declared here, so the serializer never falls back to reflection, which is what makes
// trimming and Native AOT work, and which turns a missing type into a build error rather than
// a runtime surprise.
//
// The naming policy applies to property names only. Dictionary key naming is deliberately left
// unset, because question ids are dictionary keys: rewriting them would return answers under
// names the caller never supplied.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jev.Sdk;

/// <summary>
/// Source-generated serialization context for the library's own types.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = JsonCommentHandling.Disallow,
    NumberHandling = JsonNumberHandling.Strict,
    WriteIndented = false)]
[JsonSerializable(typeof(SystemOneRequest))]
[JsonSerializable(typeof(SystemOneResponse))]
[JsonSerializable(typeof(Usage))]
[JsonSerializable(typeof(ModelListResponse))]
[JsonSerializable(typeof(ModelMetadata))]
[JsonSerializable(typeof(ValidationErrorResponse))]
[JsonSerializable(typeof(ErrorDetails))]
[JsonSerializable(typeof(JsonElementBox))]
[JsonSerializable(typeof(StructuredValue))]
[JsonSerializable(typeof(NoulQuestion))]
[JsonSerializable(typeof(ChoiceQuestion))]
[JsonSerializable(typeof(ScoreQuestion))]
[JsonSerializable(typeof(NoulAnswer))]
[JsonSerializable(typeof(ChoiceAnswer))]
[JsonSerializable(typeof(ScoreAnswer))]
[JsonSerializable(typeof(NoulCriteria))]
public sealed partial class JevJsonContext : JsonSerializerContext
{
}
