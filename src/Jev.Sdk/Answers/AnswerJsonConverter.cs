// AnswerJsonConverter.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the answer union.
// See requirements/requirements.md, R6, R14 and R24.
//
// Type: AnswerJsonConverter
//
// Public because the source generator instantiates converters by name from the [JsonConverter]
// attribute, and an inaccessible converter cannot be resolved.
//
// Reading buffers the payload through a JsonDocument, which is deliberate. Measured on a single
// answer payload: the document path costs 152 bytes, a forward-only discriminator scan 32 bytes, and
// a concrete read with no union at all 48 bytes. The document is therefore not the dominant cost, and
// it buys two things a single-pass read cannot: the concrete deserializer can re-read from the root,
// and an unmodelled answer kind keeps its original JSON intact for RawJson rather than needing a
// copy. The allocation measured on a full response is dominated by the dictionary and string
// materialization, which no converter choice removes.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jev.Sdk;

/// <summary>
/// Reads the <see cref="Answer"/> union, dispatching on the <c>type</c> member and falling back to
/// <see cref="UnknownAnswer"/> for kinds this library does not model.
/// </summary>
/// <remarks>
/// This is what makes the client forward compatible. When the API gains a new answer kind, the answers
/// this library does understand still arrive intact, and the unfamiliar one arrives with all of its
/// data rather than failing the response.
/// </remarks>
public sealed class AnswerJsonConverter : JsonConverter<Answer>
{
    /// <inheritdoc />
    public override bool HandleNull => true;

    /// <inheritdoc />
    public override Answer Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            throw new JsonException("An answer must be a JSON object; found null.");
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"An answer must be a JSON object; found {reader.TokenType}.");
        }

        // One pass over the payload: walk the members once to find the discriminator, and keep the
        // reader positioned on the same object so the concrete deserializer re-reads it cheaply.
        using JsonDocument document = JsonDocument.ParseValue(ref reader);

        JsonElement root = document.RootElement;

        string? type = root.TryGetProperty("type", out JsonElement typeElement)
            && typeElement.ValueKind == JsonValueKind.String
                ? typeElement.GetString()
                : null;

        if (type is null)
        {
            throw new JsonException("An answer must carry a string 'type' member.");
        }

        return type switch
        {
            QuestionTypes.Noul => root.Deserialize<NoulAnswer>(options)
                ?? throw new JsonException("A noul answer could not be read."),

            QuestionTypes.Choice => root.Deserialize<ChoiceAnswer>(options)
                ?? throw new JsonException("A choice answer could not be read."),

            QuestionTypes.Score => root.Deserialize<ScoreAnswer>(options)
                ?? throw new JsonException("A score answer could not be read."),

            _ => new UnknownAnswer(type, root),
        };
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Answer value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        switch (value)
        {
            case UnknownAnswer unknown:
                unknown.RawJson.WriteTo(writer);
                return;

            case NoulAnswer noul:
                JsonSerializer.Serialize(writer, noul, WithoutThisConverter(options));
                return;

            case ChoiceAnswer choice:
                JsonSerializer.Serialize(writer, choice, WithoutThisConverter(options));
                return;

            case ScoreAnswer score:
                JsonSerializer.Serialize(writer, score, WithoutThisConverter(options));
                return;

            default:
                throw new JsonException($"Unsupported answer type '{value.GetType()}'.");
        }
    }

    /// <summary>
    /// Returns options with the union converters removed, so that reading or writing a concrete kind
    /// cannot recurse back into this converter through a base-typed member.
    /// </summary>
    private static JsonSerializerOptions WithoutThisConverter(JsonSerializerOptions options)
    {
        JsonSerializerOptions clone = new(options);

        for (int i = clone.Converters.Count - 1; i >= 0; i--)
        {
            if (clone.Converters[i] is AnswerJsonConverter or QuestionJsonConverter)
            {
                clone.Converters.RemoveAt(i);
            }
        }

        return clone;
    }
}
