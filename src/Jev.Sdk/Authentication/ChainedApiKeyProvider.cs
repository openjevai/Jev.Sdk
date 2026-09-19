// ChainedApiKeyProvider.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the authentication
// seam. See requirements/requirements.md, R10 and R12.
//
// Type: ChainedApiKeyProvider

namespace Jev.Sdk;

/// <summary>
/// Consults a sequence of <see cref="IApiKeyProvider"/> implementations in order and returns
/// the first key found. This is the shape of the client's key-priority ladder.
/// </summary>
public sealed class ChainedApiKeyProvider : IApiKeyProvider
{
    private readonly IApiKeyProvider[] _providers;

    /// <summary>Initialises the chain.</summary>
    /// <param name="providers">Providers, highest priority first. Null entries are ignored.</param>
    public ChainedApiKeyProvider(params IApiKeyProvider?[] providers)
        : this((IEnumerable<IApiKeyProvider?>)providers)
    {
    }

    /// <summary>Initialises the chain from a sequence of providers.</summary>
    /// <param name="providers">Providers, highest priority first. Null entries are ignored.</param>
    public ChainedApiKeyProvider(IEnumerable<IApiKeyProvider?> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        _providers = [.. providers.Where(static p => p is not null).Select(static p => p!)];
    }

    /// <summary>The providers in this chain, highest priority first.</summary>
    public IReadOnlyList<IApiKeyProvider> Providers => _providers;

    /// <summary>True when the chain has no providers and can never yield a key.</summary>
    public bool IsEmpty => _providers.Length == 0;

    /// <inheritdoc />
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public async Task<string?> GetApiKeyAsync(CancellationToken cancellationToken)
    {
        foreach (IApiKeyProvider provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? key = await provider.GetApiKeyAsync(cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(key))
            {
                return key;
            }
        }

        return null;
    }
}
