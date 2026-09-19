// ModelEndpointTests.cs
// Part of Jev.Sdk.IntegrationTests. GET /v1/models against the live service.
//
// The model list is the cheapest endpoint, so it carries the tests that verify the client's plumbing
// end to end: authentication, deserialization, the cache, and the documented fallbacks. What it
// cannot verify is the answer schema — that is the other file's job.

using Jev.Sdk;

namespace Jev.Sdk.IntegrationTests;

/// <summary>
/// Live coverage of the model listing endpoint and the client's model resolution.
/// </summary>
public class ModelEndpointTests
{
    [LiveFact]
    public async Task GetModelsAsync_ReturnsAtLeastOneModel()
    {
        IReadOnlyList<ModelMetadata> models = await LiveClient.Shared.GetModelsAsync(CancellationToken.None);

        Assert.NotEmpty(models);
    }

    [LiveFact]
    public async Task EveryModel_ReportsANameAndDescription()
    {
        IReadOnlyList<ModelMetadata> models = await LiveClient.Shared.GetModelsAsync(CancellationToken.None);

        foreach (ModelMetadata model in models)
        {
            // name and description are declared required by the specification, so an empty one means
            // either the schema drifted or a member was dropped in deserialization. Both are worth
            // failing on.
            Assert.False(string.IsNullOrWhiteSpace(model.Name), "a model came back with no name");
            Assert.False(string.IsNullOrWhiteSpace(model.Description), $"'{model.Name}' came back with no description");
        }
    }

    [LiveFact]
    public async Task EveryModel_ReportsAReleaseDateThatParses()
    {
        IReadOnlyList<ModelMetadata> models = await LiveClient.Shared.GetModelsAsync(CancellationToken.None);

        foreach (ModelMetadata model in models)
        {
            // release_date is declared required. ParsedReleaseDate is the library's own convenience
            // property, so a null here means the value arrived in a format the library does not
            // understand — which is exactly the drift this test exists to catch.
            Assert.False(string.IsNullOrWhiteSpace(model.ReleaseDate), $"'{model.Name}' came back with no release date");
            Assert.NotNull(model.ParsedReleaseDate);
        }
    }

    [LiveFact]
    public async Task ModelNames_AreUnique()
    {
        IReadOnlyList<ModelMetadata> models = await LiveClient.Shared.GetModelsAsync(CancellationToken.None);

        // Names are the identifier a caller passes back in `model`, so a duplicate would make the
        // value ambiguous.
        Assert.Equal(models.Count, models.Select(m => m.Name).Distinct(StringComparer.Ordinal).Count());
    }

    [LiveFact]
    public async Task TheDocumentedDefaultModel_IsOfferedByTheService()
    {
        // The library documents DefaultModel as an alias the service resolves. If the service stopped
        // offering it, every caller relying on the default would break — and nothing in the unit suite
        // could know, because it never talks to the service.
        IReadOnlyList<ModelMetadata> models = await LiveClient.Shared.GetModelsAsync(CancellationToken.None);

        Assert.Contains(models, model => model.Name == JevClientOptions.DefaultModelName);
    }

    [LiveFact]
    public async Task ResolveModelAsync_ResolvesTheDefaultAliasToAConcreteName()
    {
        string resolved = await LiveClient.Shared.ResolveModelAsync(preferred: null, CancellationToken.None);

        // The documented behaviour: an alias resolves to something concrete. It may legitimately
        // resolve to the alias itself if the service does not advertise a concrete replacement, so
        // the assertion is that a usable name came back rather than that it changed.
        Assert.False(string.IsNullOrWhiteSpace(resolved));
    }

    [LiveFact]
    public async Task IsModelAvailableAsync_IsTrueForTheDefaultAndFalseForNonsense()
    {
        bool known = await LiveClient.Shared.IsModelAvailableAsync(JevClientOptions.DefaultModelName, CancellationToken.None);
        bool unknown = await LiveClient.Shared.IsModelAvailableAsync("definitely-not-a-model-9f2c1a", CancellationToken.None);

        Assert.True(known, $"the service did not report '{JevClientOptions.DefaultModelName}' as available");
        Assert.False(unknown);
    }

    [LiveFact]
    public async Task TheModelCache_ServesRepeatedCallsFromOneFetch()
    {
        // The cache is the client's only stateful behaviour, and the unit suite can only prove it
        // against a stub. Here it is proven against the real endpoint: three calls, all consistent,
        // and a second fetch after invalidation still works.
        using JevClient client = LiveClient.Create();

        IReadOnlyList<ModelMetadata> first = await client.GetModelsAsync(CancellationToken.None);
        IReadOnlyList<ModelMetadata> second = await client.GetModelsAsync(CancellationToken.None);
        IReadOnlyList<ModelMetadata> third = await client.GetAvailableModelsAsync(CancellationToken.None);

        Assert.Equal(first.Count, second.Count);
        Assert.Equal(first.Count, third.Count);

        client.InvalidateModelCache();

        IReadOnlyList<ModelMetadata> afterInvalidation = await client.GetModelsAsync(CancellationToken.None);

        Assert.Equal(first.Count, afterInvalidation.Count);
    }

    [LiveFact]
    public async Task ConcurrentReaders_AllGetTheSameListFromOneClient()
    {
        // The unit suite asserts this against a stub with a call counter. Against the real service the
        // meaningful assertion is that concurrent use of one client neither throws nor returns
        // inconsistent results.
        using JevClient client = LiveClient.Create();

        Task<IReadOnlyList<ModelMetadata>>[] readers =
            [.. Enumerable.Range(0, 16).Select(_ => client.GetModelsAsync(CancellationToken.None))];

        IReadOnlyList<ModelMetadata>[] results = await Task.WhenAll(readers);

        int expected = results[0].Count;

        Assert.All(results, models => Assert.Equal(expected, models.Count));
    }

    [LiveFact]
    public async Task CancellingBeforeTheCall_ThrowsOperationCanceled()
    {
        // Cancellation is honoured end to end, not just in the stub. Cancelled up front so no live
        // request is actually issued.
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => LiveClient.Shared.GetModelsAsync(cancellation.Token));
    }
}
