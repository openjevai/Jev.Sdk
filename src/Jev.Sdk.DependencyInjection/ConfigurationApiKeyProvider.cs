// ConfigurationApiKeyProvider.cs
// Part of Jev.Sdk.DependencyInjection. This file is one of the partial-class/file set for
// configuration. See requirements/requirements.md, R10 and R12.
//
// Type: ConfigurationApiKeyProvider

using Microsoft.Extensions.Configuration;

namespace Jev.Sdk.DependencyInjection;

/// <summary>
/// Supplies an API key read from an <see cref="IConfiguration"/>, which is where the
/// settings files land once the host has loaded them.
/// </summary>
/// <remarks>
/// Reading is synchronous, from the in-memory configuration the host already built. No file is
/// touched here; the file was read once, at startup, by <see cref="JevConfigurationLoader"/>.
/// </remarks>
public sealed class ConfigurationApiKeyProvider : IApiKeyProvider
{
    private readonly IConfiguration _configuration;
    private readonly string _keyPath;

    /// <summary>Initialises a provider reading the documented settings path.</summary>
    /// <param name="configuration">The host's configuration.</param>
    public ConfigurationApiKeyProvider(IConfiguration configuration)
        : this(configuration, $"{JevEnvironment.ConfigurationSection}:{JevEnvironment.ApiKeyKey}")
    {
    }

    /// <summary>Initialises a provider reading a specific configuration path.</summary>
    /// <param name="configuration">The host's configuration.</param>
    /// <param name="keyPath">The configuration key holding the API key.</param>
    /// <exception cref="ArgumentException">The path is null, empty, or whitespace.</exception>
    public ConfigurationApiKeyProvider(IConfiguration configuration, string keyPath)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);

        _configuration = configuration;
        _keyPath = keyPath;
    }

    /// <summary>The configuration path this provider reads.</summary>
    public string KeyPath => _keyPath;

    /// <inheritdoc />
    public Task<string?> GetApiKeyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string? value = _configuration[_keyPath];

        return Task.FromResult(string.IsNullOrWhiteSpace(value) ? null : value);
    }
}
