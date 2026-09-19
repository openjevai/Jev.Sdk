// LiveSettings.cs
// Part of Jev.Sdk.IntegrationTests. Resolves the API key for the live tests, and decides whether the
// live tests should run at all.
//
// The key comes from the library's own configuration path, never from a literal in this file. Two
// sources are read, in the order the library documents:
//
//   1. the TYPESAFE_API_KEY environment variable
//   2. appSettings.Local.json, beside the test assembly
//
// "appSettings.Local.json" deserves an explanation, because the library does not read a file by that
// name on its own. JevConfigurationLoader reads exactly two files: appSettings.json, and
// appSettings.{MACHINE_NAME}.json. Passing "Local" as the machine name — which MACHINE_NAME also
// accepts — makes the second candidate resolve to appSettings.Local.json. So the familiar name works
// through the library's documented machine-override mechanism rather than through a special case.
//
// Nothing here is required. With neither source present the live tests skip rather than fail, so a
// clone with no key still has a green suite and an honest report.

using Jev.Sdk;
using Jev.Sdk.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Jev.Sdk.IntegrationTests;

/// <summary>
/// Resolves the live-test configuration, and reports whether it is usable.
/// </summary>
internal static class LiveSettings
{
    /// <summary>The machine name that selects <c>appSettings.Local.json</c>.</summary>
    /// <remarks>
    /// Passed as the machine token so the library's own machine-override path produces the file name.
    /// Kept as one constant so the file name and the token cannot drift apart.
    /// </remarks>
    private const string LocalMachineToken = "Local";

    /// <summary>The file name the loader looks for, derived from the library rather than typed twice.</summary>
    public static string LocalFileName => JevConfigurationLoader.MachineSettingsFileName(LocalMachineToken);

    private static readonly Lazy<string?> s_apiKey = new(ResolveApiKey);

    /// <summary>
    /// True when a usable API key was found. False skips the live tests instead of failing them.
    /// </summary>
    public static bool IsConfigured => !string.IsNullOrWhiteSpace(s_apiKey.Value);

    /// <summary>
    /// The API key, or null when none is configured. Never a placeholder: see
    /// <see cref="IsPlaceholder"/>.
    /// </summary>
    public static string? ApiKey => s_apiKey.Value;

    /// <summary>
    /// Explains where a key was looked for, for the message a skipped test prints.
    /// </summary>
    /// <returns>A sentence naming both sources and the file this run looked for.</returns>
    public static string WhereWeLooked()
    {
        string path = Path.Combine(AppContext.BaseDirectory, LocalFileName);

        return $"Set {JevEnvironment.ApiKeyVariable}, or put a key in {path}.";
    }

    private static string? ResolveApiKey()
    {
        // The environment first, matching the library's documented precedence.
        string? fromEnvironment = Environment.GetEnvironmentVariable(JevEnvironment.ApiKeyVariable);

        if (IsUsable(fromEnvironment))
        {
            return fromEnvironment!.Trim();
        }

        try
        {
            // Built with the library's own loader, so the file name, the machine override and the
            // precedence are the library's rather than a second implementation of the same rules.
            IConfigurationRoot configuration = JevConfigurationLoader.Build(
                AppContext.BaseDirectory,
                LocalMachineToken);

            string? fromFile = JevOptionsBinding.FromConfiguration(configuration).ApiKey;

            return IsUsable(fromFile) ? fromFile!.Trim() : null;
        }
        catch (JevConfigurationException)
        {
            // A malformed settings file should not turn a skipped suite into a failing one. The key
            // simply was not resolvable; WhereWeLooked() names the file to fix.
            return null;
        }
    }

    /// <summary>
    /// True when a value is a real key rather than absent or an unedited placeholder.
    /// </summary>
    /// <remarks>
    /// The placeholder check exists because the repository ships example files containing
    /// "REPLACE ME". Without it, a copied-but-unedited file would make the live tests run and fail
    /// with a confusing authentication error instead of skipping with a useful message.
    /// </remarks>
    private static bool IsUsable(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.Trim().Equals("REPLACE ME", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns true when the value is the documented placeholder rather than a key.
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns>True when the value is placeholder text.</returns>
    public static bool IsPlaceholder(string? value) =>
        value is not null && value.Trim().Equals("REPLACE ME", StringComparison.OrdinalIgnoreCase);
}
