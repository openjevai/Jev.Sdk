// IApiKeyProvider.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the authentication
// seam. See requirements/requirements.md, R10 and R12.
//
// Type: IApiKeyProvider

namespace Jev.Sdk;

/// <summary>
/// Supplies the API key used to authenticate requests.
/// </summary>
/// <remarks>
/// This member is asynchronous even where an implementation has nothing to await, so that a
/// caller can supply a key from a secret store (a vault, a keychain, a managed identity)
/// without a breaking change to this interface later.
/// </remarks>
public interface IApiKeyProvider
{
    /// <summary>
    /// Returns the API key, or <see langword="null"/> when this provider has no key to offer.
    /// </summary>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>
    /// The API key, or <see langword="null"/>. Returning null is not an error; it means the
    /// next provider in the chain should be consulted.
    /// </returns>
    Task<string?> GetApiKeyAsync(CancellationToken cancellationToken);
}
