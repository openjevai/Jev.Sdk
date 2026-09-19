// StructuredValue.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the structured-value
// model. See requirements/requirements.md, R4.
//
// Type: StructuredValue

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Jev.Sdk;

/// <summary>
/// A value that may be a JSON string, object, array, number, boolean, or null.
/// </summary>
/// <remarks>
/// The TypeSafe API accepts structure wherever it accepts prose: the state, a question's
/// instructions, a Choice option description, and a Score level description can each be a
/// plain string or a JSON object or array. This type carries any of those shapes without loss,
/// so nothing a caller supplies is flattened into text on the way out.
/// </remarks>
[JsonConverter(typeof(StructuredValueJsonConverter))]
public sealed class StructuredValue
{
    private static readonly StructuredValue s_null = new(default, JsonShape.Null);

    private readonly JsonElement _element;

    private StructuredValue(JsonElement element, JsonShape shape)
    {
        _element = element;
        Shape = shape;
    }

    /// <summary>Which JSON shape this value holds.</summary>
    /// <remarks>
    /// Named for JSON's shapes rather than JSON's type keywords, so that members such as
    /// <c>Object</c> do not carry a type name.
    /// </remarks>
    public enum JsonShape
    {
        /// <summary>JSON null.</summary>
        Null,

        /// <summary>A JSON string.</summary>
        Text,

        /// <summary>A JSON object.</summary>
        Record,

        /// <summary>A JSON array.</summary>
        Sequence,

        /// <summary>A JSON number.</summary>
        Number,

        /// <summary>JSON true or false.</summary>
        Flag,
    }

    /// <summary>A shared instance representing JSON null.</summary>
    public static StructuredValue Null => s_null;

    /// <summary>The shape of this value.</summary>
    public JsonShape Shape { get; }

    /// <summary>
    /// The underlying JSON, always available. For a string value this is the JSON string
    /// element, not the unwrapped text; use <see cref="AsString"/> for the text.
    /// </summary>
    public JsonElement Element => _element;

    /// <summary>True when this value is JSON null.</summary>
    public bool IsNull => Shape == JsonShape.Null;

    /// <summary>
    /// Creates a value from text.
    /// </summary>
    /// <param name="text">The text, or null for a null value.</param>
    public static StructuredValue FromString(string? text) =>
        text is null ? s_null : new StructuredValue(JsonSerializer.SerializeToElement(text), JsonShape.Text);

    /// <summary>
    /// Creates a value from existing JSON. The element is copied, so the source document may
    /// be disposed afterwards.
    /// </summary>
    /// <param name="element">The JSON to hold.</param>
    public static StructuredValue FromJson(JsonElement element)
    {
        JsonElement copy = element.Clone();

        JsonShape shape = copy.ValueKind switch
        {
            JsonValueKind.Object => JsonShape.Record,
            JsonValueKind.Array => JsonShape.Sequence,
            JsonValueKind.String => JsonShape.Text,
            JsonValueKind.Number => JsonShape.Number,
            JsonValueKind.True or JsonValueKind.False => JsonShape.Flag,
            _ => JsonShape.Null,
        };

        return shape == JsonShape.Null ? s_null : new StructuredValue(copy, shape);
    }

    /// <summary>
    /// Creates a value from an arbitrary object, serialized with the supplied type information.
    /// </summary>
    /// <typeparam name="T">The object's type.</typeparam>
    /// <param name="value">The object. Null produces a null value.</param>
    /// <param name="typeInfo">
    /// Source-generated type information for <typeparamref name="T"/>. Required so that the
    /// library never reflects over a caller's types.
    /// </param>
    public static StructuredValue FromObject<T>(T? value, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);

        if (value is null)
        {
            return s_null;
        }

        JsonElement serialized = JsonSerializer.SerializeToElement(value, typeInfo);

        JsonShape shape = serialized.ValueKind switch
        {
            JsonValueKind.Object => JsonShape.Record,
            JsonValueKind.Array => JsonShape.Sequence,
            JsonValueKind.String => JsonShape.Text,
            JsonValueKind.Number => JsonShape.Number,
            JsonValueKind.True or JsonValueKind.False => JsonShape.Flag,
            _ => JsonShape.Null,
        };

        return new StructuredValue(serialized, shape);
    }

    /// <summary>Returns the text when this value is a JSON string, otherwise null.</summary>
    public string? AsString() => Shape == JsonShape.Text ? _element.GetString() : null;

    /// <summary>
    /// Returns this value as text: the string itself, or its raw JSON for other shapes.
    /// </summary>
    public string ToRawText() => Shape == JsonShape.Null ? string.Empty : _element.GetRawText();

    /// <summary>Creates a value from text.</summary>
    /// <param name="text">The text.</param>
    public static implicit operator StructuredValue(string? text) => FromString(text);

    /// <summary>Creates a value from existing JSON.</summary>
    /// <param name="element">The JSON.</param>
    public static implicit operator StructuredValue(JsonElement element) => FromJson(element);

    /// <inheritdoc />
    public override string ToString() => ToRawText();
}
