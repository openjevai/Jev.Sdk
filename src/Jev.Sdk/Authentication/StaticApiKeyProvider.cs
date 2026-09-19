// StaticApiKeyProvider.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the authentication
// seam. See requirements/requirements.md, R10 and R12.
//
// Type: StaticApiKeyProvider

namespace Jev.Sdk;

/// <summary>
/// Supplies a fixed API key. Used when the key is passed to the client directly.
/// </summary>
public sealed class StaticApiKeyProvider : IApiKeyProvider
{
    private readonly string _apiKey;

    /// <summary>Initialises a provider that always returns <paramref name="apiKey"/>.</summary>
    /// <param name="apiKey">The API key.</param>
    /// <exception cref="ArgumentException">The key is null, empty, or whitespace.</exception>
    public StaticApiKeyProvider(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        _apiKey = apiKey;
    }

    /// <inheritdoc />
    public Task<string?> GetApiKeyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<string?>(_apiKey);
    }
}
