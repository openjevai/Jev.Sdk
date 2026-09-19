// JevClient.Models.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the client.
// See requirements/requirements.md, R2 and R13.
//
// Type: JevClient
//
// Model discovery. This endpoint is documented only in the OpenAPI specification, not in the
// prose API reference, but it is the only way to learn which model names and aliases an
// account can use, so the client implements it.

namespace Jev.Sdk;

/// <summary>
/// Model discovery entry points.
/// </summary>
public sealed partial class JevClient
{
    private const string ModelsPath = "models";

    /// <summary>
    /// Lists the models and aliases available to the authenticated account.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call, including any retry delay.</param>
    /// <returns>The available models.</returns>
    /// <exception cref="JevConfigurationException">No API key is available.</exception>
    /// <exception cref="JevAuthenticationException">The API rejected the credentials.</exception>
    /// <exception cref="JevRateLimitException">The rate limit is still in force after retries.</exception>
    /// <exception cref="JevOverloadedException">The service is still overloaded after retries.</exception>
    /// <exception cref="JevConnectionException">The exchange failed, or the response was unreadable.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public async Task<IReadOnlyList<ModelMetadata>> GetModelsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await ResolveApiKeyAsync(cancellationToken).ConfigureAwait(false);

        ModelListResponse response = await SendAsync(
            HttpMethod.Get,
            ModelsPath,
            payload: null,
            _jsonContext.ModelListResponse,
            model: string.Empty,
            questionCount: 0,
            isSystemOne: false,
            cancellationToken).ConfigureAwait(false);

        return [.. response.Models];
    }
}
