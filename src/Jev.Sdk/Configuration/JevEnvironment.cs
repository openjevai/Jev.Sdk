// JevEnvironment.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the configuration surface.
// See requirements/requirements.md, R10.
//
// Type: JevEnvironment
//
// Variable names match the vendor's own SDKs, so a machine configured for the Python or
// JavaScript client needs no extra setup for this one.

namespace Jev.Sdk;

/// <summary>
/// Names the environment variables and configuration keys this library reads.
/// </summary>
/// <remarks>
/// The API key variable is named after the service, not after this library, because the credential
/// authenticates against TypeSafe AI and is the same variable the vendor's own SDKs read. One
/// machine, one variable, all the SDKs.
/// </remarks>
public static class JevEnvironment
{
    /// <summary>
    /// Environment variable holding the API key. This is the variable the vendor's own SDKs read, so
    /// setting it configures every TypeSafe client on the machine.
    /// </summary>
    public const string ApiKeyVariable = "TYPESAFE_API_KEY";

    /// <summary>
    /// Environment variable overriding the API base URL. The vendor's SDKs read this name.
    /// </summary>
    public const string BaseUrlVariable = "TYPESAFE_BASE_URL";

    /// <summary>
    /// Environment variable overriding the default model. The vendor's SDKs read this name.
    /// </summary>
    public const string DefaultModelVariable = "TYPESAFE_DEFAULT_MODEL";

    /// <summary>
    /// Configuration file holding the client's settings, read once at startup and never watched
    /// for changes.
    /// </summary>
    public const string GenericSettingsFile = "appSettings.json";

    /// <summary>
    /// Machine-specific settings file, which overrides the generic file. The machine token is
    /// substituted into the name, giving e.g. <c>appSettings.BUILD01.json</c>.
    /// </summary>
    public const string MachineSettingsFileFormat = "appSettings.{0}.json";

    /// <summary>
    /// Environment variable that overrides the machine token used to select the machine-specific
    /// settings file.
    /// </summary>
    /// <remarks>
    /// <see cref="Environment.MachineName"/> is stable on a workstation but is a random container id
    /// inside Docker, so a container needs this override to use the feature.
    /// </remarks>
    public const string MachineNameVariable = "MACHINE_NAME";

    /// <summary>Configuration section holding the client's settings.</summary>
    public const string ConfigurationSection = "Jev";

    /// <summary>Configuration key, under the section, holding the API key.</summary>
    public const string ApiKeyKey = "ApiKey";

    /// <summary>Configuration key, under the section, holding the base address.</summary>
    public const string BaseAddressKey = "BaseAddress";

    /// <summary>Configuration key, under the section, holding the default model.</summary>
    public const string DefaultModelKey = "DefaultModel";

    /// <summary>Configuration key, under the section, holding the per-attempt timeout.</summary>
    public const string TimeoutKey = "Timeout";
}
