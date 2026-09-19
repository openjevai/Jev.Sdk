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
// Both attributes set Skip in their own constructor, which runs after the base constructor has done
// its work — the supported way to make a skip decision at discovery time in xunit v2. Verified by
// reflection against xunit.core 2.9.3: FactAttribute is open with a settable Skip, TheoryAttribute is
// open and inherits it, and InlineDataAttribute is sealed — which is why rows are supplied with the
// framework's own InlineData rather than a derived attribute.

namespace Jev.Sdk.IntegrationTests;

/// <summary>Reasons a live test skips itself.</summary>
internal static class SkipReasons
{
    /// <summary>The message used when no API key could be resolved.</summary>
    public static string NoApiKey => $"No live API key configured. {LiveSettings.WhereWeLooked()}";
}

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
            Skip = SkipReasons.NoApiKey;
        }
    }
}

/// <summary>
/// A live test that runs once per data row. Skips every row when no key is configured.
/// </summary>
/// <remarks>
/// Apply this once and supply rows with the framework's own <c>[InlineData(...)]</c>, exactly as a
/// normal theory works. Deriving a data attribute is not possible here: InlineDataAttribute is sealed.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class LiveTheoryAttribute : TheoryAttribute
{
    /// <summary>Creates the attribute, skipping the theory when no API key is available.</summary>
    public LiveTheoryAttribute()
    {
        if (!LiveSettings.IsConfigured)
        {
            Skip = SkipReasons.NoApiKey;
        }
    }
}
