// JevServiceCollectionExtensions.cs
// Part of Jev.Sdk.DependencyInjection. This file is one of the partial-class/file set for
// dependency injection. See requirements/requirements.md, R10 and R12.
//
// Type: JevServiceCollectionExtensions
//
// Registration is additive and overridable. A host that wants the defaults adds the client in
// one line; a host that wants to replace one seam registers its own implementation first and
// this extension leaves it alone.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Jev.Sdk.DependencyInjection;

/// <summary>
/// Registers <see cref="JevClient"/> and its collaborators.
/// </summary>
public static class JevServiceCollectionExtensions
{
    /// <summary>
    /// Adds a <see cref="JevClient"/> using the supplied configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">
    /// Configuration holding the client's settings. Null falls back to the registered
    /// <see cref="IConfiguration"/>, and then to the environment alone.
    /// </param>
    /// <param name="configure">Optional further customisation, applied last.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddJevClient(
        this IServiceCollection services,
        IConfiguration? configuration = null,
        Action<JevClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IApiKeyProvider>(provider => BuildApiKeyProvider(provider, configuration));

        services.TryAddSingleton(provider =>
        {
            JevClientOptions options = BuildOptions(provider, configuration);
            configure?.Invoke(options);

            return options;
        });

        services.TryAddSingleton(provider => new JevClient(
            provider.GetRequiredService<JevClientOptions>(),
            transport: provider.GetService<ITypeSafeTransport>(),
            apiKeyProvider: provider.GetRequiredService<IApiKeyProvider>(),
            logger: provider.GetService<Microsoft.Extensions.Logging.ILogger<JevClient>>()));

        return services;
    }

    private static JevClientOptions BuildOptions(IServiceProvider provider, IConfiguration? configuration)
    {
        IConfiguration? config = ResolveConfiguration(provider, configuration);

        return config is null
            ? EnvironmentOptionsProvider.Apply(new JevClientOptions())
            : JevOptionsBinding.FromConfiguration(config);
    }

    private static ChainedApiKeyProvider BuildApiKeyProvider(IServiceProvider provider, IConfiguration? configuration)
    {
        IConfiguration? config = ResolveConfiguration(provider, configuration);

        // Precedence: the environment variable, then the machine-specific settings file, then
        // the generic settings file. An explicit key supplied to the client is handled by the
        // client itself, above this chain. The settings files were read once by the host, using
        // JevConfigurationLoader: the core library performs no file I/O.
        return new ChainedApiKeyProvider(
            new EnvironmentApiKeyProvider(),
            config is null ? null : new ConfigurationApiKeyProvider(config));
    }

    private static IConfiguration? ResolveConfiguration(IServiceProvider provider, IConfiguration? configuration) =>
        configuration ?? provider.GetService<IConfiguration>();
}
