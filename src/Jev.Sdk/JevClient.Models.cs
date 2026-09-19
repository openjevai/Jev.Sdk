// JevClient.Models.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the client.
// See requirements/requirements.md, R2, R13 and R23.
//
// Type: JevClient
//
// Model discovery, with a lazy per-client cache.
//
// The cache is lazy rather than eager, and that is deliberate. Fetching at construction would make
// `new JevClient()` perform network I/O, which the constructor must not do: it would make
// construction fail on a dead network, a bad key, or a rate limit, in a place a caller has no way
// to handle. And because `/v1/models` returns the models available to the *authenticated account*,
// the cache must be per client: a process-wide one would serve one account's model list to another.
//
// A host that wants the list warm anyway has an explicit opt-in: JevClient.WarmModelsAsync. That is
// a call the host makes at its own startup, where it can await and handle failure.

using System.Diagnostics.CodeAnalysis;

namespace Jev.Sdk;

/// <summary>
/// Model discovery entry points.
/// </summary>
public sealed partial class JevClient
{
    private const string ModelsPath = "models";

    private readonly SemaphoreSlim _modelsGate = new(1, 1);

    // Published as one immutable snapshot rather than as two independent fields. Two separate fields
    // let a reader observe a new list with a stale timestamp, which reads as a value that is either
    // already expired or valid for longer than configured, depending on the interleaving.
    private CacheEntry? _cache;

    /// <summary>
    /// Lists the models and aliases available to the authenticated account. This always performs a
    /// request; use <see cref="GetAvailableModelsAsync"/> when a cached result is acceptable.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call, including any retry delay.</param>
    /// <returns>The available models, fresh from the API.</returns>
    /// <exception cref="JevConfigurationException">No API key is available.</exception>
    /// <exception cref="JevAuthenticationException">The API rejected the credentials.</exception>
    /// <exception cref="JevRateLimitException">The rate limit is still in force after retries.</exception>
    /// <exception cref="JevOverloadedException">The service is still overloaded after retries.</exception>
    /// <exception cref="JevServerException">A server-side condition persisted after retries.</exception>
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

        // A server that omits or nulls the member would otherwise produce an ArgumentNullException from
        // the collection expression, which is neither a JevException nor what any documented method
        // promises. An empty list is the honest reading: the account has no models to report.
        IReadOnlyList<ModelMetadata> models = response.ModelsOrEmpty;

        // A successful fetch refreshes the cache, so a caller mixing this with the cached accessor
        // does not immediately pay for a second call. The entry is published as one reference write,
        // which is atomic, so a concurrent reader sees either the old snapshot or the new one and never
        // a mixture of the two.
        Volatile.Write(ref _cache, new CacheEntry(models, _timeProvider.GetUtcNow()));

        return models;
    }

    /// <summary>
    /// Returns the available models, using the client's cache when it is still fresh.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call, including any retry delay.</param>
    /// <returns>The available models.</returns>
    /// <remarks>
    /// This is what a caller wants when asking "is model X available?" or "pick the best model for
    /// this job": the answer rarely needs to be live, and a cached list avoids a request per
    /// question. Concurrent first-uses share a single request rather than each issuing their own.
    /// <para>
    /// The cache is per client and therefore scoped to one account, which matters because the API
    /// returns the models available to the authenticated account rather than a global list.
    /// </para>
    /// <para>
    /// A failed refresh does not poison the cache: the previous list stays servable, and the
    /// exception propagates so the caller knows the refresh was attempted and failed.
    /// </para>
    /// </remarks>
    /// <exception cref="JevConfigurationException">No API key is available.</exception>
    /// <exception cref="JevAuthenticationException">The API rejected the credentials.</exception>
    /// <exception cref="JevRateLimitException">The rate limit is still in force after retries.</exception>
    /// <exception cref="JevOverloadedException">The service is still overloaded after retries.</exception>
    /// <exception cref="JevServerException">A server-side condition persisted after retries.</exception>
    /// <exception cref="JevConnectionException">The exchanged failed, or the response was unreadable.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public async Task<IReadOnlyList<ModelMetadata>> GetAvailableModelsAsync(CancellationToken cancellationToken)
    {
        if (TryGetCachedModels(out IReadOnlyList<ModelMetadata>? cached))
        {
            return cached;
        }

        // WaitAsync outside a try: if it throws, the gate was never taken, and releasing it in a
        // finally would raise SemaphoreFullException. Only the acquired path may release.
        await _modelsGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // A second check inside the gate: a request that queued behind another has no reason to
            // issue its own, which is what keeps N concurrent first-uses down to one request.
            if (TryGetCachedModels(out cached))
            {
                return cached;
            }

            IReadOnlyList<ModelMetadata> models = await GetModelsAsync(cancellationToken).ConfigureAwait(false);

            return models;
        }
        finally
        {
            _modelsGate.Release();
        }
    }

    /// <summary>
    /// Returns true when <paramref name="modelName"/> matches a model or alias the account can use.
    /// </summary>
    /// <param name="modelName">The model name or alias to check.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>True when the account has that model available.</returns>
    /// <remarks>
    /// Comparison is ordinal and case-insensitive, matching how the API treats names.
    /// </remarks>
    public async Task<bool> IsModelAvailableAsync(string modelName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);

        IReadOnlyList<ModelMetadata> models = await GetAvailableModelsAsync(cancellationToken).ConfigureAwait(false);

        return models.Any(model =>
            string.Equals(model.Name, modelName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns the name of the first available model, preferring <paramref name="preferred"/> when
    /// the account has it, and falling back to <see cref="DefaultModel"/>.
    /// </summary>
    /// <param name="preferred">
    /// A model name to prefer. Null or unavailable falls through to the client's default model.
    /// </param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>A model name the account can use.</returns>
    /// <remarks>
    /// Useful when a caller wants the best available concrete model rather than an alias, for
    /// logging or for pinning behaviour.
    /// <para>
    /// If the listing fails with a <see cref="JevException"/> — a network problem, a bad key, a rate
    /// limit — this returns <paramref name="preferred"/> or <see cref="DefaultModel"/> rather than
    /// throwing, because a caller asking "which model should I use?" is better served by a workable
    /// default than by a failure. Cancellation is not caught: a caller that cancelled wants to know.
    /// A programming error such as an <see cref="ObjectDisposedException"/> also propagates, since
    /// hiding it would turn a bug into a silently degraded result.
    /// </para>
    /// </remarks>
    public async Task<string> ResolveModelAsync(string? preferred, CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<ModelMetadata> models = await GetAvailableModelsAsync(cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(preferred) &&
                models.Any(model => string.Equals(model.Name, preferred, StringComparison.OrdinalIgnoreCase)))
            {
                return preferred;
            }

            ModelMetadata? first = models.Count > 0 ? models[0] : null;

            if (first is not null)
            {
                return first.Name;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Falling back is the better outcome here: the caller asked which model to use, not whether
            // the listing endpoint is healthy.
            //
            // The catch is deliberately wider than JevException. The transport is a documented
            // substitution point, so a host-supplied implementation can throw its own exception type,
            // and the whole point of this method is that a caller cannot be made to handle a failure of
            // a listing it did not ask for. Cancellation is excluded because a caller that cancelled
            // wants to know rather than silently receiving a default.
        }

        return string.IsNullOrWhiteSpace(preferred) ? _options.DefaultModel : preferred;
    }

    /// <summary>
    /// Fetches the model list once, so that later cached reads are served without a request.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>True when the list was fetched, false when the fetch failed.</returns>
    /// <remarks>
    /// This is the explicit opt-in for a host that wants the models warm at its own startup, where
    /// it can await and handle failure. The client never does this itself, because a constructor
    /// cannot await and must not perform I/O.
    /// <para>
    /// Failure is reported as <see langword="false"/> rather than an exception, so a host can treat
    /// model warmup as a best-effort step without wrapping it in a try/catch.
    /// </para>
    /// </remarks>
    public async Task<bool> WarmModelsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await GetModelsAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Same reasoning as ResolveModelAsync: a substituted transport may throw its own type, and
            // a host treating warmup as best-effort was told it would not need a try/catch. Cancellation
            // still propagates, because a host that cancelled its own startup wants to know.
            return false;
        }
    }

    /// <summary>
    /// Drops the cached model list, so the next cached read refetches.
    /// </summary>
    /// <remarks>
    /// Not usually needed, because the cache expires on its own. It exists for a caller that knows
    /// the server-side list changed, such as after provisioning an account.
    /// </remarks>
    public void InvalidateModelCache() => Volatile.Write(ref _cache, null);

    /// <summary>
    /// How long a fetched model list is served before the next cached read refetches. Defaults to
    /// one hour. Zero disables caching, making every cached read a fresh request.
    /// </summary>
    public TimeSpan ModelCacheDuration => _options.ModelCacheDuration;

    private bool TryGetCachedModels([NotNullWhen(true)] out IReadOnlyList<ModelMetadata>? models)
    {
        models = null;

        if (_options.ModelCacheDuration <= TimeSpan.Zero)
        {
            return false;
        }

        // One atomic read of the whole snapshot, so the list and its timestamp cannot disagree.
        CacheEntry? entry = Volatile.Read(ref _cache);

        if (entry is null)
        {
            return false;
        }

        if (_timeProvider.GetUtcNow() - entry.FetchedAt >= _options.ModelCacheDuration)
        {
            return false;
        }

        models = entry.Models;
        return true;
    }

    /// <summary>An immutable model-list snapshot with the instant it was fetched.</summary>
    private sealed record CacheEntry(IReadOnlyList<ModelMetadata> Models, DateTimeOffset FetchedAt);
}
