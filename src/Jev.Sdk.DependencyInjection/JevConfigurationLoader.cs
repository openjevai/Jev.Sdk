// JevConfigurationLoader.cs
// Part of Jev.Sdk.DependencyInjection. This file is one of the partial-class/file set for
// configuration. See requirements/requirements.md, R10, R13 and R15.
//
// Type: JevConfigurationLoader
//
// File-based configuration is loaded here, at host startup, and never inside the client. The
// core library performs no file I/O, so reading a settings file in a client constructor would
// break that rule and would also require a synchronous file read in a constructor, which
// cannot await.
//
// Precedence, highest first:
//   1. an API key supplied to the client
//   2. the TYPESAFE_API_KEY environment variable
//   3. appSettings.{MACHINE_NAME}.json
//   4. appSettings.json
//
// Environment beats every file, and a machine-specific file overrides the generic one. The
// machine token comes from the MACHINE_NAME environment variable when set, falling back to
// Environment.MachineName; a container's machine name is a random id that changes when the
// container is recreated, so the override is what makes the file useful in a container.

using System.Text;
using Microsoft.Extensions.Configuration;

namespace Jev.Sdk.DependencyInjection;

/// <summary>
/// Builds the configuration sources the client reads.
/// </summary>
public static class JevConfigurationLoader
{
    /// <summary>
    /// The machine-specific file name, pre-parsed so that repeated calls do not re-parse the
    /// composite format string.
    /// </summary>
    private static readonly CompositeFormat s_machineSettingsFileName =
        CompositeFormat.Parse(JevEnvironment.MachineSettingsFileFormat);

    /// <summary>
    /// Builds a configuration root holding the two settings files and the environment, in the
    /// documented precedence order.
    /// </summary>
    /// <param name="basePath">
    /// Directory searched for the settings files. Defaults to the current directory.
    /// </param>
    /// <param name="machineName">
    /// Machine token used to select the machine-specific file. Defaults to the
    /// <c>MACHINE_NAME</c> environment variable, then to <see cref="Environment.MachineName"/>.
    /// </param>
    /// <returns>A configuration root. Files that are absent are simply not added.</returns>
    /// <remarks>
    /// The files are read once. They are deliberately not watched for changes: a client holds a
    /// resolved key and options, and reloading underneath a live client would make its
    /// behaviour depend on when a file last changed.
    /// </remarks>
    public static IConfigurationRoot Build(string? basePath = null, string? machineName = null)
    {
        string resolvedBasePath = basePath ?? Directory.GetCurrentDirectory();

        ConfigurationBuilder builder = new();

        // Lowest precedence first, because each source added later overrides the ones before it.
        AddIfPresent(builder, resolvedBasePath, JevEnvironment.GenericSettingsFile);
        AddIfPresent(builder, resolvedBasePath, MachineSettingsFileName(machineName));

        builder.AddEnvironmentVariables();

        return builder.Build();
    }

    /// <summary>
    /// Resolves the machine token used to build the machine-specific file name.
    /// </summary>
    /// <param name="explicitMachineName">An explicit token, which wins when supplied.</param>
    /// <returns>The token to substitute into the file name.</returns>
    public static string ResolveMachineName(string? explicitMachineName = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitMachineName))
        {
            return explicitMachineName;
        }

        string? fromEnvironment = Environment.GetEnvironmentVariable(JevEnvironment.MachineNameVariable);

        return string.IsNullOrWhiteSpace(fromEnvironment) ? Environment.MachineName : fromEnvironment;
    }

    /// <summary>
    /// Returns the machine-specific file name for a token.
    /// </summary>
    /// <param name="machineName">Machine token. Null resolves it as <see cref="ResolveMachineName"/> does.</param>
    /// <returns>The file name, for example <c>appSettings.BUILD01.json</c>.</returns>
    public static string MachineSettingsFileName(string? machineName = null) =>
        string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            s_machineSettingsFileName,
            ResolveMachineName(machineName));

    /// <summary>
    /// Returns the file names this loader looks for, in precedence order, lowest first.
    /// </summary>
    /// <param name="machineName">Machine token. Null resolves it as <see cref="ResolveMachineName"/> does.</param>
    /// <returns>The candidate file names.</returns>
    public static IReadOnlyList<string> CandidateFileNames(string? machineName = null) =>
        [JevEnvironment.GenericSettingsFile, MachineSettingsFileName(machineName)];

    private static void AddIfPresent(ConfigurationBuilder builder, string basePath, string fileName)
    {
        string path = Path.Combine(basePath, fileName);

        if (File.Exists(path))
        {
            builder.AddJsonFile(path, optional: true, reloadOnChange: false);
        }
    }
}
