// QuestionJsonConverter.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the question union.
// See requirements/requirements.md, R6 and R14.
//
// Type: QuestionJsonConverter
//
// Public because the source generator instantiates converters by name from the
// [JsonConverter] attribute, and an inaccessible converter cannot be resolved.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jev.Sdk;

/// <summary>
/// Reads the <see cref="Question"/> union, dispatching on the <c>type</c> member and falling
/// back to <see cref="RawQuestion"/> for kinds this library does not model.
/// </summary>
/// <remarks>
/// A discriminated union would normally be expressed with
/// <see cref="JsonPolymorphicAttribute"/>, but its behaviour on an unrecognised discriminator
/// is to throw, which would fail an entire request or response because of one unfamiliar kind.
/// This converter falls back instead, which is what makes the client forward compatible.
/// </remarks>
public sealed class QuestionJsonConverter : JsonConverter<Question>
{
    /// <inheritdoc />
    public override Question Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"A question must be a JSON object; found {root.ValueKind}.");
        }

        string? type = root.TryGetProperty("type", out JsonElement typeElement) && typeElement.ValueKind == JsonValueKind.String
            ? typeElement.GetString()
            : null;

        if (type is null)
        {
            throw new JsonException("A question must carry a string 'type' member.");
        }

        return type switch
        {
            QuestionTypes.Noul => ReadConcrete<NoulQuestion>(root, options),
            QuestionTypes.Choice => ReadConcrete<ChoiceQuestion>(root, options),
            QuestionTypes.Score => ReadConcrete<ScoreQuestion>(root, options),
            _ => new RawQuestion(type, root),
        };
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Question value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        switch (value)
        {
            case RawQuestion raw:
                raw.RawJson.WriteTo(writer);
                return;

            case NoulQuestion noul:
                WriteConcrete(writer, noul, options);
                return;

            case ChoiceQuestion choice:
                WriteConcrete(writer, choice, options);
                return;

            case ScoreQuestion score:
                WriteConcrete(writer, score, options);
                return;

            default:
                throw new JsonException($"Unsupported question type '{value.GetType()}'.");
        }
    }

    /// <summary>
    /// Reads a concrete kind with the converter removed from the options, so that reading a
    /// member typed as the base kind cannot recurse back into this converter.
    /// </summary>
    private static T ReadConcrete<T>(JsonElement element, JsonSerializerOptions options)
        where T : Question =>
        element.Deserialize<T>(WithoutThisConverter(options))
        ?? throw new JsonException($"A {typeof(T).Name} could not be read.");

    private static void WriteConcrete<T>(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        where T : Question =>
        JsonSerializer.Serialize(writer, value, WithoutThisConverter(options));

    private static JsonSerializerOptions WithoutThisConverter(JsonSerializerOptions options)
    {
        JsonSerializerOptions clone = new(options);

        for (int i = clone.Converters.Count - 1; i >= 0; i--)
        {
            if (clone.Converters[i] is QuestionJsonConverter or AnswerJsonConverter)
            {
                clone.Converters.RemoveAt(i);
            }
        }

        return clone;
    }
}
