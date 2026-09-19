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

        // The documented precedence, highest first:
        //
        //   1. an explicit key on JevClientOptions
        //   2. the TYPESAFE_API_KEY environment variable
        //   3. the settings files, machine-specific before generic
        //
        // The explicit key must be in this chain, not merely on the options object: the client is
        // constructed with this provider, which replaces its own ladder, so a key that is absent here
        // is a key the client cannot see. Omitting it made an explicit options key lose to the
        // environment variable, contradicting the documented order.
        return new ChainedApiKeyProvider(
            ExplicitKeyFromOptions(provider),
            new EnvironmentApiKeyProvider(),
            config is null ? null : new ConfigurationApiKeyProvider(config));
    }

    /// <summary>
    /// Wraps an explicitly configured key as a provider, so precedence holds when the client's own
    /// ladder is replaced by this chain.
    /// </summary>
    private static LazyApiKeyProvider ExplicitKeyFromOptions(IServiceProvider provider)
    {
        // Resolved lazily: the options singleton is built from the same container, and asking for it
        // here during provider construction would be circular. A provider defers the lookup instead.
        return new LazyApiKeyProvider(() =>
            provider.GetService<JevClientOptions>()?.ApiKey);
    }

    private static IConfiguration? ResolveConfiguration(IServiceProvider provider, IConfiguration? configuration) =>
        configuration ?? provider.GetService<IConfiguration>();
}

/// <summary>
/// Wraps a lazily resolved API key so a provider can be built before the value it reads exists.
/// </summary>
internal sealed class LazyApiKeyProvider : IApiKeyProvider
{
    private readonly Func<string?> _read;

    internal LazyApiKeyProvider(Func<string?> read) => _read = read;

    public Task<string?> GetApiKeyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string? key = _read();

        return Task.FromResult(string.IsNullOrWhiteSpace(key) ? null : key);
    }
}
