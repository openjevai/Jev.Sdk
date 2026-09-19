// JsonElementBox.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the wire contracts.
// See requirements/requirements.md, R7 and R14.
//
// Type: JsonElementBox

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jev.Sdk;

/// <summary>
/// Wraps a <see cref="JsonElement"/> so that it can be an optional member of a model.
/// </summary>
/// <remarks>
/// <see cref="JsonElement"/> is a struct, so an absent element and a default element are
/// indistinguishable. Boxing makes absence mean absence, which matters for the optional
/// members the server may or may not include.
/// </remarks>
[JsonConverter(typeof(JsonElementBoxConverter))]
public sealed class JsonElementBox
{
    /// <summary>Initialises a box around a JSON value.</summary>
    /// <param name="element">The value to hold.</param>
    public JsonElementBox(JsonElement element) => Element = element.Clone();

    /// <summary>The wrapped JSON.</summary>
    public JsonElement Element { get; }

    /// <summary>Returns the wrapped JSON as raw text.</summary>
    public override string ToString() => Element.GetRawText();
}

/// <summary>
/// Reads and writes <see cref="JsonElementBox"/>, preserving whatever JSON was present.
/// </summary>
public sealed class JsonElementBoxConverter : JsonConverter<JsonElementBox>
{
    /// <inheritdoc />
    public override JsonElementBox Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        return new JsonElementBox(document.RootElement);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, JsonElementBox value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        value.Element.WriteTo(writer);
    }
}
