// WorldState.cs
// Part of Jev.Sdk.World. The live state of a run, typed by the schema.
//
// Values are held generically because the engine does not know what a world contains: a field is a
// bool, an int, or some text, and the engine reads and writes it by key. The schema supplied the
// meaning; this supplies the storage.
//
// The state description is generated here rather than written by hand, which is the point of the
// `describe` entries in the schema: the model is told the situation in prose, and the author said how
// each value reads.

using System.Globalization;
using System.Text.Json;

namespace Jev.Sdk.World;

/// <summary>
/// The current values of a world's state, plus the description sent to the model.
/// </summary>
public sealed class WorldState
{
    private readonly WorldDefinition _world;
    private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);

    /// <summary>Creates a state initialised from its schema.</summary>
    /// <param name="world">The validated world this state belongs to.</param>
    public WorldState(WorldDefinition world)
    {
        ArgumentNullException.ThrowIfNull(world);

        _world = world;

        foreach (StateField field in world.State)
        {
            _values[field.Key] = InitialValue(field);
        }
    }

    /// <summary>How many turns have been taken.</summary>
    public int Turns { get; private set; }

    /// <summary>Whether the run has been won.</summary>
    public bool Won { get; private set; }

    /// <summary>Whether the run has been lost.</summary>
    public bool Lost { get; private set; }

    /// <summary>True when the run is over, either way.</summary>
    public bool IsOver => Won || Lost;

    /// <summary>
    /// Reads a boolean field.
    /// </summary>
    /// <param name="key">The field's key.</param>
    /// <returns>Its value.</returns>
    public bool GetBool(string key) =>
        _values.TryGetValue(key, out object? value) && value is bool b ? b : false;

    /// <summary>
    /// Reads a numeric field.
    /// </summary>
    /// <param name="key">The field's key.</param>
    /// <returns>Its value, or zero when it is absent or not numeric.</returns>
    public double GetNumber(string key) =>
        _values.TryGetValue(key, out object? value) && value is int i ? i : 0.0;

    /// <summary>
    /// Reads a field as text, whatever its declared type.
    /// </summary>
    /// <param name="key">The field's key.</param>
    /// <returns>A stable string form, for comparison against a condition.</returns>
    public string GetText(string key)
    {
        if (!_values.TryGetValue(key, out object? value))
        {
            return string.Empty;
        }

        return value switch
        {
            bool b => b ? "true" : "false",
            int i => i.ToString(CultureInfo.InvariantCulture),
            string s => s,
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Sets a field from text, interpreted against its declared type.
    /// </summary>
    /// <param name="key">The field's key.</param>
    /// <param name="text">The value, as written in a rule.</param>
    /// <exception cref="WorldLoadException">The field is undeclared or the value does not fit its type.</exception>
    /// <remarks>
    /// Text rather than a typed argument because rules are data: `"to": "true"` in a JSON document has
    /// to become a bool in a bool field and the string "true" in a text field, and only the schema
    /// knows which.
    /// </remarks>
    public void Set(string key, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        StateField field = Field(key);

        _values[key] = field.Type switch
        {
            StateValueKinds.Bool when bool.TryParse(text, out bool b) => b,
            StateValueKinds.Bool => throw new WorldLoadException($"State '{key}' is a bool; '{text}' is not true or false."),
            StateValueKinds.Counter when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) => i,
            StateValueKinds.Counter => throw new WorldLoadException($"State '{key}' is an int; '{text}' is not a whole number."),
            _ => text,
        };
    }

    /// <summary>
    /// Adds to an integer field.
    /// </summary>
    /// <param name="key">The field's key.</param>
    /// <param name="by">How much to add.</param>
    /// <exception cref="WorldLoadException">The field is undeclared or is not an integer.</exception>
    public void Increment(string key, int by)
    {
        StateField field = Field(key);

        if (field.Type != StateValueKinds.Counter)
        {
            throw new WorldLoadException($"State '{key}' is a {field.Type}; only an int can be incremented.");
        }

        _values[key] = (int)GetNumber(key) + by;
    }

    /// <summary>Counts a turn.</summary>
    public void CountTurn() => Turns++;

    /// <summary>Marks the run as won.</summary>
    public void MarkWon() => Won = true;

    /// <summary>Marks the run as lost.</summary>
    public void MarkLost() => Lost = true;

    /// <summary>
    /// Builds the description sent to the model, from the world's template.
    /// </summary>
    /// <returns>The state as prose.</returns>
    /// <remarks>
    /// Each placeholder is replaced by the sentence the schema gives for the field's current value. An
    /// int or text field uses its "*" fallback sentence when no exact entry exists, so a counter that
    /// reaches an unforeseen value still reads as a sentence rather than as a bare number.
    /// </remarks>
    public string Describe()
    {
        string description = _world.StateTemplate;

        foreach (StateField field in _world.State)
        {
            description = Placeholders.Substitute(description, field.Key, DescribeField(field));
        }

        return description;
    }

    /// <summary>
    /// Returns a compact one-line summary of the state, for the console.
    /// </summary>
    /// <returns>Field keys and their current values, separated by spaces.</returns>
    /// <remarks>
    /// The console shows raw values while the model is shown prose, which is deliberate: the player is
    /// watching the world's mechanics, and the model is being asked to reason about a place.
    /// </remarks>
    public string Summarise()
    {
        List<string> parts = [];

        foreach (StateField field in _world.State)
        {
            if (field.Type == StateValueKinds.Bool && !GetBool(field.Key))
            {
                // False flags are listed rather than hidden, because "door: locked" is information and
                // omitting it would make the summary ambiguous.
                parts.Add($"{field.Key}: {DescribeShort(field)}");
                continue;
            }

            parts.Add($"{field.Key}: {DescribeShort(field)}");
        }

        return string.Join("   ", parts);
    }

    private string DescribeShort(StateField field)
    {
        string value = GetText(field.Key);
        string lookup = field.Type == StateValueKinds.Bool ? value : value;

        if (field.Describe.TryGetValue(lookup, out string? exact))
        {
            return exact;
        }

        return field.Describe.TryGetValue("*", out string? any) ? any : value;
    }

    private string DescribeField(StateField field)
    {
        string value = GetText(field.Key);

        if (field.Describe.TryGetValue(value, out string? exact))
        {
            return exact;
        }

        if (field.Describe.TryGetValue("*", out string? any))
        {
            return any;
        }

        // The validator guarantees a bool describes both values and anything else has at least one
        // entry, so this is only reachable for an int or text value with no matching entry and no "*".
        return $"{field.Key} is {value}";
    }

    private StateField Field(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        StateField? field = _world.State.FirstOrDefault(f => f.Key == key);

        return field ?? throw new WorldLoadException($"No state field named '{key}' is declared.");
    }

    private static object InitialValue(StateField field)
    {
        JsonElement initial = field.Initial;

        return field.Type switch
        {
            StateValueKinds.Bool => initial.ValueKind == JsonValueKind.True,
            StateValueKinds.Counter => initial.ValueKind == JsonValueKind.Number ? initial.GetInt32() : 0,
            _ => initial.ValueKind == JsonValueKind.String ? initial.GetString() ?? string.Empty : string.Empty,
        };
    }
}
