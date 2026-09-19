// JevClientOptions.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the client
// configuration surface. See requirements/requirements.md, R8, R10 and R15.
//
// Type: JevClientOptions
//
// Every default here is chosen to match the vendor's own SDK defaults, so that a caller moving
// between the Python, JavaScript, and .NET clients gets the same throttling behaviour. Where a
// value differs deliberately, the reason is in a comment.

using System.Text.Json;

namespace Jev.Sdk;

/// <summary>
/// Configuration for <see cref="JevClient"/>. Every member has a working default, so the client
/// functions correctly with no configuration at all.
/// </summary>
/// <remarks>
/// Instances are copied on construction of a client and the copy is frozen. Mutating an options
/// object after a client has been created has no effect on that client.
/// </remarks>
public sealed class JevClientOptions
{
    /// <summary>The default TypeSafe AI API base address.</summary>
    public static readonly Uri DefaultBaseAddress = new("https://api.typesafe.ai/v1/");

    /// <summary>The default model alias used when a request does not name one.</summary>
    public const string DefaultModelName = "jev-latest";

    /// <summary>
    /// The API key. When set, this is the highest-priority key source and no environment variable
    /// or configuration file is consulted. Its absence is normal, not an error.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Base address of the API, including the version segment. Defaults to
    /// <see cref="DefaultBaseAddress"/> (<c>https://api.typesafe.ai/v1/</c>). Overridable through
    /// the <c>TYPESAFE_BASE_URL</c> environment variable.
    /// </summary>
    public Uri BaseAddress { get; set; } = DefaultBaseAddress;

    /// <summary>
    /// Model used when a call does not specify one. Defaults to
    /// <see cref="DefaultModelName"/> (<c>jev-latest</c>). Overridable through the
    /// <c>TYPESAFE_DEFAULT_MODEL</c> environment variable.
    /// </summary>
    public string DefaultModel { get; set; } = DefaultModelName;

    /// <summary>
    /// Per-attempt timeout for a single HTTP operation. Each retry attempt receives its own full
    /// timeout; this is not a deadline for the whole call. Defaults to 10 seconds, matching the
    /// vendor's SDKs.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Number of retries after the first attempt for retryable failures. Zero disables retrying.
    /// Defaults to 2, matching the vendor's SDKs.
    /// </summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>
    /// Delay before the first retry. Defaults to 500 milliseconds, matching the vendor's SDKs.
    /// </summary>
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Multiplier applied to the delay after each attempt. Defaults to 2.0, giving exponential
    /// backoff.
    /// </summary>
    public double RetryBackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Upper bound on a *computed* backoff delay. Defaults to 5 seconds, matching the vendor's
    /// SDKs. This does not cap a server-supplied <c>Retry-After</c>; see
    /// <see cref="MaxRetryAfter"/>.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Upper bound on a *server-supplied* retry delay. A <c>Retry-After</c> longer than this is
    /// ignored and the computed backoff is used instead, because waiting longer than this inside a
    /// client call is not useful to a caller that can retry at its own level. Defaults to 60
    /// seconds, matching the vendor's SDKs.
    /// </summary>
    public TimeSpan MaxRetryAfter { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Fraction of each computed backoff delay to subtract at random, from 0 to 1. Defaults to
    /// 0.25, matching the vendor's SDKs: each delay is reduced by up to a quarter, which
    /// desynchronises callers that hit the same limit without shortening the wait much.
    /// </summary>
    /// <remarks>
    /// Note the shape. This is *subtractive* jitter — a delay of 1 second becomes somewhere
    /// between 0.75 and 1 second — not the "full jitter" used by some libraries, which would
    /// replace the delay with a uniform random value between zero and the computed maximum. The
    /// subtractive form keeps the intended backoff while still spreading retries out.
    /// </remarks>
    public double RetryJitterFraction { get; set; } = 0.25;

    /// <summary>
    /// When true, requests are validated locally before any network call is made. Defaults to
    /// true. Disabling this trades a fast, clear error for a slower, less specific one.
    /// </summary>
    public bool ValidateRequests { get; set; } = true;

    /// <summary>
    /// Additional HTTP headers to send with every request.
    /// </summary>
    /// <remarks>
    /// The vendor's SDKs both accept extra request headers, and without an equivalent a caller
    /// behind a proxy, gateway, or platform that requires its own header could not use this client
    /// at all. Headers are applied after the library's own, so a caller may override a default such
    /// as <c>Accept</c>. The <c>Authorization</c> header is always set by the library and cannot be
    /// replaced from here: a key belongs in <see cref="ApiKey"/>.
    /// <para>
    /// Values are sent verbatim. Do not put caller content or a credential in a header, because
    /// headers are visible in transit logs this library does not control.
    /// </para>
    /// </remarks>
    public Dictionary<string, string> Headers { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Optional hook to customise the client's JSON settings. It receives a private copy of the
    /// options, which is then frozen. Leave null to accept the defaults.
    /// </summary>
    /// <remarks>
    /// The hook may add converters and change property naming for the client's own types. It must
    /// not change the property naming policy for dictionary keys: question ids are dictionary
    /// keys, and rewriting them would return answers under keys the caller never supplied.
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
    /// Creates a copy for a client to own, validating the values it depends on.
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

        if (MaxRetryAfter < TimeSpan.Zero)
        {
            throw new JevConfigurationException("JevClientOptions.MaxRetryAfter cannot be negative.");
        }

        if (RetryJitterFraction is < 0 or > 1)
        {
            throw new JevConfigurationException("JevClientOptions.RetryJitterFraction must be between 0 and 1.");
        }

        if (string.IsNullOrWhiteSpace(DefaultModel))
        {
            throw new JevConfigurationException("JevClientOptions.DefaultModel cannot be empty.");
        }

        foreach (KeyValuePair<string, string> header in Headers)
        {
            if (string.IsNullOrWhiteSpace(header.Key))
            {
                throw new JevConfigurationException("JevClientOptions.Headers cannot contain a blank header name.");
            }

            if (string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
            {
                throw new JevConfigurationException(
                    "JevClientOptions.Headers cannot set the Authorization header. Supply the key through " +
                    "JevClientOptions.ApiKey, or an IApiKeyProvider, so it is never held as an ordinary string.");
            }
        }

        JevClientOptions copy = new()
        {
            ApiKey = ApiKey,
            BaseAddress = BaseAddress,
            DefaultModel = DefaultModel,
            Timeout = Timeout,
            MaxRetries = MaxRetries,
            InitialRetryDelay = InitialRetryDelay,
            RetryBackoffMultiplier = RetryBackoffMultiplier,
            MaxRetryDelay = MaxRetryDelay,
            MaxRetryAfter = MaxRetryAfter,
            RetryJitterFraction = RetryJitterFraction,
            ValidateRequests = ValidateRequests,
            ConfigureJson = ConfigureJson,
        };

        foreach (KeyValuePair<string, string> header in Headers)
        {
            copy.Headers[header.Key] = header.Value;
        }

        return copy;
    }
}
