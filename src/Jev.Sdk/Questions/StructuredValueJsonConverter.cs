// StructuredValueJsonConverter.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the structured-value model.
// See requirements/requirements.md, R4 and R14.
//
// Type: StructuredValueJsonConverter
//
// Public because the source generator instantiates converters by name from the [JsonConverter]
// attribute, and an inaccessible converter cannot be resolved.
//
// HandleNull must be true, and that is not a detail. JsonConverter<T>.HandleNull defaults to false
// for reference types, which means the serializer handles a null token itself and never calls this
// converter. The effect is that a JSON null *inside a collection* becomes a C# null instead of a
// StructuredValue.Null, so a valid response such as a Choice option with a null description throws
// NullReferenceException when read. Declaring HandleNull makes this converter responsible for every
// null it sees, including those inside collections.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jev.Sdk;

/// <summary>
/// Reads and writes <see cref="StructuredValue"/>, preserving whatever JSON shape was present rather
/// than coercing it.
/// </summary>
public sealed class StructuredValueJsonConverter : JsonConverter<StructuredValue>
{
    /// <inheritdoc />
    public override bool HandleNull => true;

    /// <inheritdoc />
    public override StructuredValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return StructuredValue.Null;
        }

        // The document is disposed at the end of this scope, so the value must own its own copy.
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        return StructuredValue.FromJsonInPlace(document.RootElement.Clone());
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, StructuredValue value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (value is null || value.IsNull)
        {
            writer.WriteNullValue();
            return;
        }

        value.Element.WriteTo(writer);
    }
}
