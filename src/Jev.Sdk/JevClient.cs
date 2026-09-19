// JevClient.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the client.
// See requirements/requirements.md, R10, R12, R13 and R18.
//
// Type: JevClient
//
// The client is split across the following partials:
//   JevClient.cs                    construction, options, seams, disposal
//   JevClient.SystemOne.cs          evaluation entry points
//   JevClient.Models.cs             model listing
//   JevClient.Validation.cs         local request validation
//   JevClient.Pipeline.cs           the shared send, retry, and error-mapping pipeline

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jev.Sdk;

/// <summary>
/// A client for the TypeSafe AI System One API.
/// </summary>
/// <remarks>
/// The client works with no configuration beyond an API key in the environment. Its
/// collaborators are interfaces with built-in implementations, so a host may replace the
/// transport, the key provider, or the logger, and a test may do the same to run the whole
/// client without a network.
/// </remarks>
public sealed partial class JevClient : IDisposable
{
    private readonly JevClientOptions _options;
    private readonly ITypeSafeTransport _transport;
    private readonly IApiKeyProvider _apiKeyProvider;
    private readonly ILogger _logger;
    private readonly JevJsonContext _jsonContext;
    private readonly TimeProvider _timeProvider;
    private readonly Func<double>? _jitterSource;

    /// <summary>
    /// Creates a client with no configuration beyond what the environment provides.
    /// </summary>
    /// <remarks>
    /// The API key is read from the <c>TYPESAFE_API_KEY</c> environment variable when a call
    /// is made, so setting the variable after construction still works. When no key is
    /// available, the first call raises <see cref="JevConfigurationException"/> naming every
    /// source that was consulted.
    /// </remarks>
    public JevClient()
        : this(options: null, transport: null, apiKeyProvider: null, logger: null)
    {
    }

    /// <summary>Creates a client that authenticates with the supplied key.</summary>
    /// <param name="apiKey">The API key. Its absence is normal, not an error.</param>
    public JevClient(string? apiKey)
        : this(new JevClientOptions { ApiKey = apiKey }, transport: null, apiKeyProvider: null, logger: null)
    {
    }

    /// <summary>Creates a client from options.</summary>
    /// <param name="options">The options. Null gives the defaults.</param>
    public JevClient(JevClientOptions? options)
        : this(options, transport: null, apiKeyProvider: null, logger: null)
    {
    }

    /// <summary>
    /// Creates a client with explicit collaborators. This is the constructor a host or a test
    /// uses to substitute any seam.
    /// </summary>
    /// <param name="options">The options. Null gives the defaults.</param>
    /// <param name="transport">
    /// The transport to send with. Null builds the built-in HTTP transport.
    /// </param>
    /// <param name="apiKeyProvider">
    /// Supplies the API key. Null builds the default ladder: the explicit key, then the
    /// environment variable. The dependency-injection package extends the ladder with the
    /// machine-specific and generic settings files.
    /// </param>
    /// <param name="logger">Receives the client's diagnostics. Null disables logging.</param>
    /// <exception cref="JevConfigurationException">A supplied option value is invalid.</exception>
    public JevClient(
        JevClientOptions? options,
        ITypeSafeTransport? transport,
        IApiKeyProvider? apiKeyProvider,
        ILogger? logger)
        : this(options, transport, apiKeyProvider, logger, TimeProvider.System, jitterSource: null)
    {
    }

    // Internal so that tests can pin the clock and the jitter source, making retry timing
    // deterministic without waiting. There is deliberately no public counterpart: a caller has
    // no reason to supply either, and a public parameter would be a promise to keep.
    internal JevClient(
        JevClientOptions? options,
        ITypeSafeTransport? transport,
        IApiKeyProvider? apiKeyProvider,
        ILogger? logger,
        TimeProvider timeProvider,
        Func<double>? jitterSource)
    {
        // When the caller supplies no options at all, the environment fills in the base address and
        // default model, matching what the vendor's SDKs read. An explicitly supplied options object
        // is left alone: the environment supplies defaults, it does not override a decision made in
        // code.
        _options = (options ?? EnvironmentOptionsProvider.Apply(new JevClientOptions())).Snapshot();

        // The serialization context is built once, here, and never mutated afterwards.
        _jsonContext = JevJson.CreateContext(_options.ConfigureJson);

        _apiKeyProvider = apiKeyProvider ?? BuildDefaultKeyProvider(_options);

        _transport = transport ?? new HttpTypeSafeTransport(
            httpClient: null,
            apiKeyProvider: _apiKeyProvider,
            timeout: _options.Timeout);

        _logger = logger ?? NullLogger.Instance;
        _timeProvider = timeProvider;

        // A null here means "no injected source", not "no jitter". Production jitter must actually
        // happen: without it every client that hits the same rate limit retries in lockstep, which is
        // the problem the jitter fraction exists to solve. A test injects a constant source to make the
        // delay deterministic.
        _jitterSource = jitterSource ?? Random.Shared.NextDouble;
    }

    /// <summary>The model used by calls that do not name one.</summary>
    public string DefaultModel => _options.DefaultModel;

    /// <summary>The base address requests are sent to.</summary>
    public Uri BaseAddress => _options.BaseAddress;

    /// <summary>
    /// The maximum number of retries performed after a retryable failure. Zero disables
    /// retrying.
    /// </summary>
    public int MaxRetries => _options.MaxRetries;

    /// <summary>
    /// Releases the transport, when it holds unmanaged resources. Safe to call more than once.
    /// </summary>
    /// <remarks>
    /// A caller-supplied transport that the caller also owns is disposed as well, because the
    /// client is the last thing to use it. Supply a transport that tolerates this, or let the
    /// client build its own.
    /// </remarks>
    public void Dispose() => (_transport as IDisposable)?.Dispose();

    private static ChainedApiKeyProvider BuildDefaultKeyProvider(JevClientOptions options)
    {
        // Priority order, highest first: an explicit key, then the environment variable. The
        // settings files participate through the dependency-injection package, which loads them
        // at host startup; the core library performs no file I/O.
        return new ChainedApiKeyProvider(
            options.ApiKey is { Length: > 0 } key ? new StaticApiKeyProvider(key) : null,
            new EnvironmentApiKeyProvider());
    }

    private async Task<string> ResolveApiKeyAsync(CancellationToken cancellationToken)
    {
        string? apiKey = await _apiKeyProvider.GetApiKeyAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new JevConfigurationException(
                "No API key was found. Supply one by passing it to the JevClient constructor, by setting " +
                $"the {JevEnvironment.ApiKeyVariable} environment variable, or by setting " +
                $"{JevEnvironment.ConfigurationSection}:{JevEnvironment.ApiKeyKey} in " +
                $"{JevEnvironment.GenericSettingsFile} or appSettings.<MACHINE_NAME>.json " +
                "(the settings files are read by the Jev.Sdk.DependencyInjection package).");
        }

        return apiKey;
    }
}
