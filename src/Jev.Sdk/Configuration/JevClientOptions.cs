// JevClientOptions.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the client
// configuration surface. See requirements/requirements.md, R10 and R15.
//
// Type: JevClientOptions

using System.Text.Json;

namespace Jev.Sdk;

/// <summary>
/// Configuration for <see cref="JevClient"/>. Every member has a working default, so the
/// client functions correctly with no configuration at all.
/// </summary>
/// <remarks>
/// Instances are copied on construction of a client and the copy is frozen. Mutating an
/// options object after a client has been created has no effect on that client.
/// </remarks>
public sealed class JevClientOptions
{
    /// <summary>The default TypeSafe AI API base address.</summary>
    public static readonly Uri DefaultBaseAddress = new("https://api.typesafe.ai/v1/");

    /// <summary>The default model alias used when a request does not name one.</summary>
    public const string DefaultModelName = "jev-latest";

    /// <summary>
    /// The API key. When set, this is the highest-priority key source and no environment
    /// variable or configuration file is consulted. Its absence is normal, not an error.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Base address of the API, including the version segment. Defaults to
    /// <see cref="DefaultBaseAddress"/> (<c>https://api.typesafe.ai/v1/</c>).
    /// </summary>
    public Uri BaseAddress { get; set; } = DefaultBaseAddress;

    /// <summary>
    /// Model used when a call does not specify one. Defaults to
    /// <see cref="DefaultModelName"/> (<c>jev-latest</c>).
    /// </summary>
    public string DefaultModel { get; set; } = DefaultModelName;

    /// <summary>
    /// Per-attempt timeout. Each retry attempt receives its own full timeout; this is not a
    /// deadline for the whole call. Defaults to 100 seconds.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);

    /// <summary>
    /// Number of retries after the first attempt for retryable failures (HTTP 429 and 529,
    /// and transport-level connection failures). Defaults to 3.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Delay before the first retry. Defaults to 500 milliseconds.</summary>
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Multiplier applied to the delay after each attempt. Defaults to 2.0, giving
    /// exponential backoff.
    /// </summary>
    public double RetryBackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Upper bound on any computed or server-supplied retry delay. Defaults to 30 seconds.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// When true, adds random jitter to computed retry delays to avoid synchronized retries
    /// across callers. Defaults to true.
    /// </summary>
    public bool UseRetryJitter { get; set; } = true;

    /// <summary>
    /// When true, requests are validated locally before any network call is made. Defaults
    /// to true. Disabling this trades a fast, clear error for a slower, less specific one.
    /// </summary>
    public bool ValidateRequests { get; set; } = true;

    /// <summary>
    /// Optional hook to customise the client's JSON settings. It receives a private copy of
    /// the options, which is then frozen. Leave null to accept the defaults.
    /// </summary>
    /// <remarks>
    /// The hook may add converters and change property naming for the client's own types.
    /// It must not change the property naming policy for dictionary keys: question ids are
    /// dictionary keys, and rewriting them would return answers under keys the caller never
    /// supplied.
    /// </remarks>
    public Action<JsonSerializerOptions>? ConfigureJson { get; set; }

    /// <summary>
    /// Returns the full request URI for a relative path such as <c>systemone</c>.
    /// </summary>
    internal Uri BuildUri(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        Uri baseAddress = BaseAddress;
        string baseText = baseAddress.AbsoluteUri;

        if (!baseText.EndsWith('/'))
        {
            baseText += "/";
        }

        return new Uri(new Uri(baseText, UriKind.Absolute), relativePath);
    }

    /// <summary>
    /// Creates a deep-enough copy for a client to own, validating the values it depends on.
    /// </summary>
    internal JevClientOptions Snapshot()
    {
        if (BaseAddress is null)
        {
            throw new JevConfigurationException("JevClientOptions.BaseAddress must be set.");
        }

        if (!BaseAddress.IsAbsoluteUri)
        {
            throw new JevConfigurationException("JevClientOptions.BaseAddress must be an absolute URI.");
        }

        if (BaseAddress.Scheme != Uri.UriSchemeHttps && BaseAddress.Scheme != Uri.UriSchemeHttp)
        {
            throw new JevConfigurationException(
                $"JevClientOptions.BaseAddress must use http or https; found '{BaseAddress.Scheme}'.");
        }

        if (Timeout <= TimeSpan.Zero)
        {
            throw new JevConfigurationException("JevClientOptions.Timeout must be greater than zero.");
        }

        if (MaxRetries < 0)
        {
            throw new JevConfigurationException("JevClientOptions.MaxRetries cannot be negative.");
        }

        if (InitialRetryDelay < TimeSpan.Zero)
        {
            throw new JevConfigurationException("JevClientOptions.InitialRetryDelay cannot be negative.");
        }

        if (RetryBackoffMultiplier < 1.0)
        {
            throw new JevConfigurationException("JevClientOptions.RetryBackoffMultiplier must be at least 1.0.");
        }

        if (MaxRetryDelay < TimeSpan.Zero)
        {
            throw new JevConfigurationException("JevClientOptions.MaxRetryDelay cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(DefaultModel))
        {
            throw new JevConfigurationException("JevClientOptions.DefaultModel cannot be empty.");
        }

        return new JevClientOptions
        {
            ApiKey = ApiKey,
            BaseAddress = BaseAddress,
            DefaultModel = DefaultModel,
            Timeout = Timeout,
            MaxRetries = MaxRetries,
            InitialRetryDelay = InitialRetryDelay,
            RetryBackoffMultiplier = RetryBackoffMultiplier,
            MaxRetryDelay = MaxRetryDelay,
            UseRetryJitter = UseRetryJitter,
            ValidateRequests = ValidateRequests,
            ConfigureJson = ConfigureJson,
        };
    }
}
