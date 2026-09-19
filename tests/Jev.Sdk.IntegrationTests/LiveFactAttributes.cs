// LiveFactAttributes.cs
// Part of Jev.Sdk.IntegrationTests. Makes the live tests report honestly when no key is configured.
//
// These tests call the real API, so they cannot run without a key. Two dishonest options were
// available and both are rejected:
//
//   - Let them fail. A clone with no key would show a red suite, which trains people to ignore
//     failures and says nothing about the library.
//   - Let them pass. A green tick for a call that never happened is worse: it reports coverage of
//     behaviour that was never exercised.
//
// A skip is the honest third option. It appears as a skip in the test report, it names the two places
// a key can come from, and it cannot be mistaken for a passing assertion.
//
// xunit v2 has no built-in "skip when" attribute, so this derives from FactAttribute and decides at
// discovery time. Skip is set after the derived constructor body runs, which is why the attribute
// takes no constructor argument for it.

using System.Globalization;

namespace Jev.Sdk.IntegrationTests;

/// <summary>
/// A test that calls the live API. Skips itself, with an explanation, when no key is configured.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class LiveFactAttribute : FactAttribute
{
    /// <summary>Creates the attribute, skipping the test when no API key is available.</summary>
    public LiveFactAttribute()
    {
        if (!LiveSettings.IsConfigured)
        {
            Skip = $"No live API key configured. {LiveSettings.WhereWeLooked()}";
        }
    }
}

/// <summary>
/// A live test that runs once per supplied data row. Skips when no key is configured.
/// </summary>
/// <param name="args">The data row, passed through to the test method.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class LiveTheoryAttribute(params object[] args) : TheoryAttribute
{
    /// <summary>The data rows this theory feeds to the test.</summary>
    public object[] Args { get; } = args;

    /// <summary>Returns the row, formatted for the test report.</summary>
    /// <returns>A readable representation of the row.</returns>
    public override string ToString()
    {
        // DisplayName is only computed for TheoryAttribute subclasses when this method is overridden;
        // without it a theory's rows collapse into one indistinguishable name in the report.
        string rows = string.Join(
            ", ",
            Args.Select(a => Convert.ToString(a, CultureInfo.InvariantCulture) ?? "(null)"));

        return $"{GetType().Name}({rows})";
    }
}
