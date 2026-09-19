// Placeholder.cs
// Part of Jev.Sdk.Sample. Recognises the documented placeholder values that ship in
// appSettings.json and .env.example.
//
// The sample ships its example files with the literal value "REPLACE ME" so the expected shape is
// visible without inventing a fake key. That value must never be sent to the API, and it must not be
// treated as "configured" either — a caller who copies the example and forgets to edit it should get
// the prompt, not an authentication error or, worse, a base address of "REPLACE ME".
//
// This type is sample-only. The library has no opinion about placeholder text: by the time a value
// reaches JevOptionsBinding it is a real setting, and guessing that a real key might be a placeholder
// would be wrong more often than it would be helpful.

namespace Jev.Sdk.Sample;

/// <summary>
/// Recognises the placeholder values the sample's example files use.
/// </summary>
internal static class Placeholder
{
    /// <summary>The literal placeholder written into the shipped example files.</summary>
    private const string Marker = "REPLACE ME";

    /// <summary>
    /// Returns true when a configured value is absent, blank, or still the documented placeholder.
    /// </summary>
    /// <param name="value">The value read from configuration.</param>
    /// <returns>True when the value should be treated as not configured.</returns>
    /// <remarks>
    /// The comparison ignores surrounding whitespace and case, because placeholder text gets
    /// retyped by hand and "replace me" is the same non-answer as "REPLACE ME".
    /// </remarks>
    public static bool MeansUnset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return value.Trim().Equals(Marker, StringComparison.OrdinalIgnoreCase);
    }
}
