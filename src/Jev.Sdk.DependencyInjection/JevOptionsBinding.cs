// JevOptionsBinding.cs
// Part of Jev.Sdk.DependencyInjection. This file is one of the partial-class/file set for
// configuration. See requirements/requirements.md, R10 and R15.
//
// Type: JevOptionsBinding

using Microsoft.Extensions.Configuration;

namespace Jev.Sdk.DependencyInjection;

/// <summary>
/// Reads client options from configuration.
/// </summary>
/// <remarks>
/// Every value is optional. A settings file may contain nothing but an API key, or nothing at
/// all, and the client still works with its defaults.
/// </remarks>
public static class JevOptionsBinding
{
    /// <summary>
    /// Builds options from the <c>Jev</c> configuration section.
    /// </summary>
    /// <param name="configuration">The host's configuration.</param>
    /// <param name="apiKey">An explicit key, which takes precedence over the section.</param>
    /// <returns>Options carrying whatever the section specified, plus the defaults elsewhere.</returns>
    public static JevClientOptions FromConfiguration(IConfiguration configuration, string? apiKey = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection section = configuration.GetSection(JevEnvironment.ConfigurationSection);
        JevClientOptions options = new();

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            options.ApiKey = apiKey;
        }
        else if (!string.IsNullOrWhiteSpace(section[JevEnvironment.ApiKeyKey]))
        {
            options.ApiKey = section[JevEnvironment.ApiKeyKey];
        }

        string? baseAddress = section[JevEnvironment.BaseAddressKey];
        if (!string.IsNullOrWhiteSpace(baseAddress))
        {
            if (!Uri.TryCreate(baseAddress, UriKind.Absolute, out Uri? parsed))
            {
                throw new JevConfigurationException(
                    $"{JevEnvironment.ConfigurationSection}:{JevEnvironment.BaseAddressKey} must be an absolute URI; found '{baseAddress}'.");
            }

            options.BaseAddress = parsed;
        }

        string? defaultModel = section[JevEnvironment.DefaultModelKey];
        if (!string.IsNullOrWhiteSpace(defaultModel))
        {
            options.DefaultModel = defaultModel;
        }

        string? timeout = section[JevEnvironment.TimeoutKey];
        if (!string.IsNullOrWhiteSpace(timeout))
        {
            if (!TimeSpan.TryParse(timeout, System.Globalization.CultureInfo.InvariantCulture, out TimeSpan parsedTimeout)
                || parsedTimeout <= TimeSpan.Zero)
            {
                throw new JevConfigurationException(
                    $"{JevEnvironment.ConfigurationSection}:{JevEnvironment.TimeoutKey} must be a positive duration; found '{timeout}'.");
            }

            options.Timeout = parsedTimeout;
        }

        return options;
    }
}
