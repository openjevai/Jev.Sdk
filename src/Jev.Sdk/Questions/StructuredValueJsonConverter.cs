// StructuredValueJsonConverter.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the structured-value
// model. See requirements/requirements.md, R4 and R14.
//
// Type: StructuredValueJsonConverter
//
// Public because the source generator instantiates converters by name from the
// [JsonConverter] attribute, and an inaccessible converter cannot be resolved.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jev.Sdk;

/// <summary>
/// Reads and writes <see cref="StructuredValue"/>, preserving whatever JSON shape was
/// present rather than coercing it.
/// </summary>
public sealed class StructuredValueJsonConverter : JsonConverter<StructuredValue>
{
    /// <inheritdoc />
    public override StructuredValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        return StructuredValue.FromJson(document.RootElement);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, StructuredValue value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        if (value.IsNull)
        {
            writer.WriteNullValue();
            return;
        }

        value.Element.WriteTo(writer);
    }
}
