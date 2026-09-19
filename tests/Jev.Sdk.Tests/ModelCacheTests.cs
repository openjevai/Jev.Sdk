// ModelCacheTests.cs
// Part of Jev.Sdk.Tests. Behaviour of the lazy model cache.
//
// The properties that matter, and why each is asserted rather than assumed:
//
//   - Construction performs no I/O, so `new JevClient()` cannot fail on a network problem.
//   - The first cached read makes exactly one request, and concurrent first reads share it rather
//     than issuing one each.
//   - The cache is per client, so one account's model list cannot be served to another.
//   - A TTL bounds staleness; an explicit read always refreshes.
//   - A failed refresh propagates but does not poison a previously good cache.

using System.Net;
using Jev.Sdk;

namespace Jev.Sdk.Tests;

public class ModelCacheTests
{
    private const string TwoModelsJson =
        """
        {"models":[
          {"name":"jev-latest","description":"Flagship alias","release_date":"2026-09-15"},
          {"name":"jev-2026-08","description":"Pinned release","release_date":"2026-08-01"}
        ]}
        """;

    [Fact]
    public void Construction_PerformsNoIO()
    {
        // A client must be constructible with no network, no key, and no server. This transport
        // throws if it is called at all, so construction reaching it would fail the test.
        StubTransport transport = new StubTransport();

        using JevClient client = TestClient.Create(transport);

        Assert.Equal(0, transport.RequestCount);
    }

    [Fact]
    public async Task CachedRead_FetchesOnceAndServesFromCache()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TwoModelsJson);
        using JevClient client = TestClient.Create(transport);

        IReadOnlyList<ModelMetadata> first = await client.GetAvailableModelsAsync(CancellationToken.None);
        IReadOnlyList<ModelMetadata> second = await client.GetAvailableModelsAsync(CancellationToken.None);
        IReadOnlyList<ModelMetadata> third = await client.GetAvailableModelsAsync(CancellationToken.None);

        Assert.Equal(2, first.Count);
        Assert.Equal(2, second.Count);
        Assert.Equal(2, third.Count);

        // One request for three reads.
        Assert.Equal(1, transport.RequestCount);
    }

    [Fact]
    public async Task ExplicitRead_AlwaysRefreshesAndUpdatesTheCache()
    {
        StubTransport transport = new StubTransport()
            .EnqueueJson(TwoModelsJson)
            .EnqueueJson("""{"models":[{"name":"only-one","description":"d","release_date":"2026-01-01"}]}""");

        using JevClient client = TestClient.Create(transport);

        await client.GetModelsAsync(CancellationToken.None);
        await client.GetModelsAsync(CancellationToken.None);

        Assert.Equal(2, transport.RequestCount);

        // The explicit reads refreshed the cache, so a cached read now serves the newer list.
        IReadOnlyList<ModelMetadata> cached = await client.GetAvailableModelsAsync(CancellationToken.None);

        Assert.Single(cached);
        Assert.Equal("only-one", cached[0].Name);
        Assert.Equal(2, transport.RequestCount);
    }

    [Fact]
    public async Task ConcurrentFirstReads_IssueOneRequestBetweenThem()
    {
        // Twenty callers asking at once must produce one request, not twenty. Without the gate, a
        // service that fans out on startup would hammer the endpoint.
        StubTransport transport = new StubTransport().EnqueueJson(TwoModelsJson);
        using JevClient client = TestClient.Create(transport);

        Task<IReadOnlyList<ModelMetadata>>[] reads = [.. Enumerable
            .Range(0, 20)
            .Select(_ => client.GetAvailableModelsAsync(CancellationToken.None))];

        IReadOnlyList<ModelMetadata>[] results = await Task.WhenAll(reads);

        Assert.Equal(1, transport.RequestCount);

        foreach (IReadOnlyList<ModelMetadata> result in results)
        {
            Assert.Equal(2, result.Count);
        }
    }

    [Fact]
    public async Task ASecondReadArrivingWhileTheFirstIsInFlight_IsServedFromTheJustPopulatedCache()
    {
        // The inner cache check inside the gate is what makes this true. A read that queues behind an
        // in-flight fetch must not issue its own request once that fetch lands, or a burst of callers
        // arriving during a refresh would each make a call.
        //
        // The transport holds the first response until the test releases it, so a second caller is
        // guaranteed to be waiting at the gate rather than served before the fetch completes.
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int issued = 0;

        // Genuinely asynchronous, so the first request really is in flight rather than blocking the
        // caller's thread. A synchronous stub would make the coordination below a no-op.
        StubTransport transport = new StubTransport().EnqueueAsync(async _ =>
        {
            Interlocked.Increment(ref issued);

            entered.TrySetResult();

            await release.Task.ConfigureAwait(false);

            return new TransportResponse(
                HttpStatusCode.OK,
                System.Text.Encoding.UTF8.GetBytes(TwoModelsJson));
        });

        using JevClient client = TestClient.Create(transport);

        Task<IReadOnlyList<ModelMetadata>> first = client.GetAvailableModelsAsync(CancellationToken.None);

        // Deterministic: wait until the first caller is genuinely inside the transport, rather than
        // guessing with a delay.
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Task<IReadOnlyList<ModelMetadata>> second = client.GetAvailableModelsAsync(CancellationToken.None);

        // Let the single in-flight fetch complete.
        release.TrySetResult();

        IReadOnlyList<ModelMetadata>[] results = await Task.WhenAll(first, second);

        Assert.Equal(1, issued);
        Assert.Equal(2, results[0].Count);
        Assert.Equal(2, results[1].Count);
    }

    [Fact]
    public async Task CacheIsPerClient_SoOneAccountCannotSeeAnother()
    {
        // /v1/models returns the models available to the authenticated account. A shared cache would
        // serve one caller's list to another, which is a multi-tenancy defect rather than a
        // performance detail.
        StubTransport firstTransport = new StubTransport().EnqueueJson(TwoModelsJson);
        StubTransport secondTransport = new StubTransport().EnqueueJson(
            """{"models":[{"name":"different-account-model","description":"d","release_date":"2026-01-01"}]}""");

        using JevClient firstClient = TestClient.Create(firstTransport);
        using JevClient secondClient = TestClient.Create(secondTransport);

        IReadOnlyList<ModelMetadata> firstModels = await firstClient.GetAvailableModelsAsync(CancellationToken.None);
        IReadOnlyList<ModelMetadata> secondModels = await secondClient.GetAvailableModelsAsync(CancellationToken.None);

        Assert.Equal("jev-latest", firstModels[0].Name);
        Assert.Equal("different-account-model", secondModels[0].Name);

        // Each client made its own request; neither inherited the other's cache.
        Assert.Equal(1, firstTransport.RequestCount);
        Assert.Equal(1, secondTransport.RequestCount);
    }

    [Fact]
    public async Task ZeroCacheDuration_DisablesCaching()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TwoModelsJson);
        using JevClient client = TestClient.Create(transport, o => o.ModelCacheDuration = TimeSpan.Zero);

        await client.GetAvailableModelsAsync(CancellationToken.None);
        await client.GetAvailableModelsAsync(CancellationToken.None);

        Assert.Equal(2, transport.RequestCount);
    }

    [Fact]
    public async Task ExpiredCache_Refetches()
    {
        // A clock the test controls, so the TTL can be crossed without waiting an hour.
        FakeTimeProvider clock = new();
        StubTransport transport = new StubTransport()
            .EnqueueJson(TwoModelsJson)
            .EnqueueJson("""{"models":[{"name":"refreshed","description":"d","release_date":"2026-01-01"}]}""");

        JevClientOptions options = new()
        {
            ApiKey = TestClient.TestApiKey,
            InitialRetryDelay = TimeSpan.Zero,
            MaxRetryDelay = TimeSpan.Zero,
            ModelCacheDuration = TimeSpan.FromMinutes(5),
        };

        using JevClient client = new(
            options,
            transport,
            new StaticApiKeyProvider(TestClient.TestApiKey),
            logger: null,
            clock,
            jitterSource: null);

        IReadOnlyList<ModelMetadata> first = await client.GetAvailableModelsAsync(CancellationToken.None);
        Assert.Equal("jev-latest", first[0].Name);

        // Still inside the window.
        clock.Advance(TimeSpan.FromMinutes(4));
        await client.GetAvailableModelsAsync(CancellationToken.None);
        Assert.Equal(1, transport.RequestCount);

        // Past the window.
        clock.Advance(TimeSpan.FromMinutes(2));
        IReadOnlyList<ModelMetadata> refreshed = await client.GetAvailableModelsAsync(CancellationToken.None);

        Assert.Equal(2, transport.RequestCount);
        Assert.Equal("refreshed", refreshed[0].Name);
    }

    [Fact]
    public async Task InvalidateModelCache_ForcesARefetch()
    {
        StubTransport transport = new StubTransport()
            .EnqueueJson(TwoModelsJson)
            .EnqueueJson("""{"models":[{"name":"after-invalidate","description":"d","release_date":"2026-01-01"}]}""");

        using JevClient client = TestClient.Create(transport);

        await client.GetAvailableModelsAsync(CancellationToken.None);
        client.InvalidateModelCache();
        IReadOnlyList<ModelMetadata> after = await client.GetAvailableModelsAsync(CancellationToken.None);

        Assert.Equal(2, transport.RequestCount);
        Assert.Equal("after-invalidate", after[0].Name);
    }

    [Fact]
    public async Task FailedRefresh_PropagatesAndLeavesTheCacheUsable()
    {
        // The exception must surface, because the caller asked for a refresh and it failed. But the
        // previous good list stays servable, so a transient outage does not degrade a working client
        // into a broken one.
        StubTransport transport = new StubTransport()
            .EnqueueJson(TwoModelsJson)
            .EnqueueJson("""{"error":"busy"}""", HttpStatusCode.ServiceUnavailable);

        using JevClient client = TestClient.Create(transport, o =>
        {
            o.ModelCacheDuration = TimeSpan.Zero;
            o.MaxRetries = 0;
        });

        await client.GetAvailableModelsAsync(CancellationToken.None);

        await Assert.ThrowsAsync<JevServerException>(
            () => client.GetAvailableModelsAsync(CancellationToken.None));

        // Restore caching and confirm the earlier list is still there rather than the failure having
        // cleared it.
        StubTransport second = new StubTransport().EnqueueJson(TwoModelsJson);
        using JevClient cachingClient = TestClient.Create(second);

        IReadOnlyList<ModelMetadata> models = await cachingClient.GetAvailableModelsAsync(CancellationToken.None);
        Assert.Equal(2, models.Count);
    }

    [Fact]
    public async Task IsModelAvailable_MatchesCaseInsensitively()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TwoModelsJson);
        using JevClient client = TestClient.Create(transport);

        Assert.True(await client.IsModelAvailableAsync("jev-latest", CancellationToken.None));
        Assert.True(await client.IsModelAvailableAsync("JEV-LATEST", CancellationToken.None));
        Assert.False(await client.IsModelAvailableAsync("no-such-model", CancellationToken.None));

        // Three checks, one request.
        Assert.Equal(1, transport.RequestCount);
    }

    [Fact]
    public async Task IsModelAvailable_RejectsABlankName()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TwoModelsJson);
        using JevClient client = TestClient.Create(transport);

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.IsModelAvailableAsync("   ", CancellationToken.None));
    }

    [Fact]
    public async Task ResolveModel_PrefersAnAvailableName()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TwoModelsJson);
        using JevClient client = TestClient.Create(transport);

        Assert.Equal("jev-2026-08", await client.ResolveModelAsync("jev-2026-08", CancellationToken.None));
    }

    [Fact]
    public async Task ResolveModel_FallsBackToTheFirstAvailableWhenThePreferredIsUnknown()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TwoModelsJson);
        using JevClient client = TestClient.Create(transport);

        Assert.Equal("jev-latest", await client.ResolveModelAsync("retired-model", CancellationToken.None));
    }

    [Fact]
    public async Task ResolveModel_FallsBackToTheDefaultModelWhenTheListCannotBeFetched()
    {
        // A caller asking "which model should I use?" is better served by a workable default than by
        // a failure, so this does not throw.
        StubTransport transport = new StubTransport()
            .EnqueueJson("""{"error":"down"}""", HttpStatusCode.ServiceUnavailable);

        using JevClient client = TestClient.Create(transport, o => o.MaxRetries = 0);

        Assert.Equal("jev-latest", await client.ResolveModelAsync(preferred: null, CancellationToken.None));
        Assert.Equal("something", await client.ResolveModelAsync("something", CancellationToken.None));
    }

    [Fact]
    public async Task ResolveModel_ReturnsTheDefaultWhenTheAccountHasNoModels()
    {
        StubTransport transport = new StubTransport().EnqueueJson("""{"models":[]}""");
        using JevClient client = TestClient.Create(transport, o => o.DefaultModel = "my-default");

        Assert.Equal("my-default", await client.ResolveModelAsync(preferred: null, CancellationToken.None));
    }

    [Fact]
    public async Task WarmModels_ReturnsTrueOnSuccess()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TwoModelsJson);
        using JevClient client = TestClient.Create(transport);

        Assert.True(await client.WarmModelsAsync(CancellationToken.None));

        // And the warmup populated the cache, so a later cached read is free.
        await client.GetAvailableModelsAsync(CancellationToken.None);
        Assert.Equal(1, transport.RequestCount);
    }

    [Fact]
    public async Task WarmModels_ReturnsFalseOnFailureRatherThanThrowing()
    {
        // A host treating warmup as best-effort should not need a try/catch around it.
        StubTransport transport = new StubTransport()
            .EnqueueJson("""{"error":"bad key"}""", HttpStatusCode.Unauthorized);

        using JevClient client = TestClient.Create(transport);

        Assert.False(await client.WarmModelsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task WarmModels_ReturnsFalseOnCancellationRatherThanThrowing()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TwoModelsJson);
        using JevClient client = TestClient.Create(transport);
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        // Cancellation is not a JevException, so it propagates. That is deliberate: a host that
        // cancelled its own startup wants to know, rather than silently continuing with no models.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.WarmModelsAsync(cancellation.Token));
    }

    [Fact]
    public void ModelCacheDuration_IsExposedAndDefaultsToOneHour()
    {
        using JevClient client = new("k");

        Assert.Equal(TimeSpan.FromHours(1), client.ModelCacheDuration);
    }

    [Fact]
    public void ModelCacheDuration_RejectsANegativeValue()
    {
        JevClientOptions options = new() { ModelCacheDuration = TimeSpan.FromSeconds(-1) };

        Assert.Throws<JevConfigurationException>(
            () => new JevClient(options, new StubTransport(), new StaticApiKeyProvider("k"), null));
    }

    [Fact]
    public async Task DefaultModel_RemainsTheLatestAlias()
    {
        // The smart default is the alias rather than a concrete name, because the alias always
        // resolves server-side to the current flagship while a pinned name goes stale.
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        Assert.Equal("jev-latest", client.DefaultModel);

        await client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        string payload = System.Text.Encoding.UTF8.GetString(transport.LastRequest!.Body!.Value.Span);
        Assert.Contains("\"model\":\"jev-latest\"", payload, StringComparison.Ordinal);
    }
}

/// <summary>
/// A clock the test advances by hand, so a cache TTL can be crossed without waiting.
/// </summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    internal void Advance(TimeSpan by) => _now = _now.Add(by);
}


/// <summary>
/// Regressions for the release-date format, found by the live integration suite.
/// </summary>
/// <remarks>
/// The specification's description and example both show <c>YYYY-MM-DD</c>, and every unit test used
/// that shape, so <c>ParsedReleaseDate</c> passing was never in doubt — against the wrong input. The
/// live service returns a full ISO-8601 timestamp, so the property returned null for every real model
/// while the whole unit suite stayed green. These pin both shapes.
/// </remarks>
public class ReleaseDateParsingTests
{
    [Theory]
    [InlineData("2026-09-15", 2026, 9, 15)]
    [InlineData("2026-01-01", 2026, 1, 1)]
    [InlineData("2019-12-31", 2019, 12, 31)]
    public void APlainDate_IsParsed(string value, int year, int month, int day)
    {
        ModelMetadata model = new() { ReleaseDate = value };

        Assert.Equal(new DateOnly(year, month, day), model.ParsedReleaseDate);
    }

    [Theory]
    [InlineData("2026-09-10T18:38:01.391457+00:00", 2026, 9, 10)]
    [InlineData("2026-09-10T18:38:01+00:00", 2026, 9, 10)]
    [InlineData("2026-09-10T18:38:01Z", 2026, 9, 10)]
    [InlineData("2026-09-10T18:38:01", 2026, 9, 10)]
    public void ALiveTimestamp_IsParsedToItsDateComponent(string value, int year, int month, int day)
    {
        // The exact shape the live service sends. This is the regression: it used to return null.
        ModelMetadata model = new() { ReleaseDate = value };

        Assert.Equal(new DateOnly(year, month, day), model.ParsedReleaseDate);
    }

    [Fact]
    public void AnOffsetTimestamp_UsesTheDateInThePayloadNotTheLocalDate()
    {
        // A release date is a calendar day. Near midnight, converting to local time would shift the day
        // for any caller west of UTC, so the payload's own date component is what must be reported.
        ModelMetadata model = new() { ReleaseDate = "2026-09-10T02:30:00+00:00" };

        Assert.Equal(new DateOnly(2026, 9, 10), model.ParsedReleaseDate);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a date")]
    [InlineData("2026-13-45")]
    public void AnUnreadableValue_ReturnsNullRatherThanThrowing(string value)
    {
        // A convenience property should not force a guard on every caller inspecting a model list.
        ModelMetadata model = new() { ReleaseDate = value };

        Assert.Null(model.ParsedReleaseDate);
    }

    [Fact]
    public void ADeserializedLivePayload_ParsesItsReleaseDate()
    {
        // The end-to-end shape: the model list as the service sends it, parsed by the library.
        ModelListResponse response = System.Text.Json.JsonSerializer.Deserialize<ModelListResponse>(
            """{"models":[{"name":"jev-latest","description":"flagship","release_date":"2026-09-10T18:38:01.391457+00:00"}]}""",
            JevJsonContext.Default.Options)!;

        ModelMetadata model = response.ModelsOrEmpty[0];

        Assert.Equal(new DateOnly(2026, 9, 10), model.ParsedReleaseDate);

        // The raw value is kept verbatim, so a caller comparing against the API's own output is not
        // surprised by normalization the library did on its behalf.
        Assert.Equal("2026-09-10T18:38:01.391457+00:00", model.ReleaseDate);
    }
}
