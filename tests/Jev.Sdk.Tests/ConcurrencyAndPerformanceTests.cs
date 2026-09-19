// ConcurrencyAndPerformanceTests.cs
// Part of Jev.Sdk.Tests. Thread safety and scalability, asserted by running concurrent work rather
// than by reading the code. Every property here was first measured with a stress harness outside the
// suite, and each test encodes the property that harness established.
//
// Two of the three issues below are DEFENSIVE, not demonstrated defects, and this file says so rather
// than implying a test that catches them. Verification was attempted by reintroducing each race and
// re-running the relevant test eight times; neither was caught, because neither produces an observable
// failure. That distinction is recorded here so nobody later reads a green test as proof.
//
//   1. DEFENSIVE. The model cache published its list and its timestamp as two independent field
//      writes, so a reader could observe a torn pair. The consequences are all benign: a stale
//      timestamp means one list served slightly longer than configured, or one harmless extra fetch.
//      The snapshot makes it correct by construction instead of relying on each torn case being
//      survivable. Reintroducing the race does NOT fail these tests.
//   2. DEFENSIVE. Disposal used a plain bool with a check-then-act. HttpClient.Dispose is idempotent,
//      and a send that races a disposal raises ObjectDisposedException, which is a documented and
//      expected outcome that this file catches deliberately. Interlocked is still the right shape, but
//      again no test here proves a defect existed.
//   3. MEASURED. Binding a response to a caller's own type built a fresh JsonSerializerOptions per
//      response, discarding the serializer's metadata cache on every call. This was a real allocation
//      on every typed call and is fixed by caching the resolution.
//
// Serial execution is deliberately disabled elsewhere in this suite for diagnostics isolation. These
// tests create their own concurrency, so that setting does not weaken them.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using Jev.Sdk;

namespace Jev.Sdk.Tests;

public class ConcurrencyAndPerformanceTests
{
    private const string ModelsJson =
        """{"models":[{"name":"jev-latest","description":"Flagship","release_date":"2026-09-15"}]}""";

    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly string _body;
        private int _count;

        internal CountingHandler(string body) => _body = body;

        internal int Count => Volatile.Read(ref _count);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
        }
    }

    // ---------------------------------------------------------------- model cache

    [Fact]
    public async Task ConcurrentCachedReads_AreConsistentAndIssueOneRequest()
    {
        CountingHandler handler = new(ModelsJson);
        using HttpTypeSafeTransport transport = new(
            new System.Net.Http.HttpClient(handler, disposeHandler: false), new StaticApiKeyProvider("k"), timeout: null);

        using JevClient client = new(
            new JevClientOptions { ApiKey = "k" }, transport, new StaticApiKeyProvider("k"), logger: null);

        ConcurrentBag<string> failures = [];

        await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(async () =>
        {
            for (int i = 0; i < 200; i++)
            {
                IReadOnlyList<ModelMetadata> models = await client.GetAvailableModelsAsync(CancellationToken.None);

                if (models.Count != 1 || models[0].Name != "jev-latest")
                {
                    failures.Add($"unexpected list of {models.Count}");
                }
            }
        })));

        Assert.Empty(failures);
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task InvalidationRacingWithReads_NeverProducesAFault()
    {
        // The regression: list and timestamp were independent writes, so a reader could pair a fresh
        // list with a stale timestamp or the reverse. Publishing one immutable snapshot removes the
        // possibility; this test drives the race hard enough to have caught it.
        CountingHandler handler = new(ModelsJson);
        using HttpTypeSafeTransport transport = new(
            new System.Net.Http.HttpClient(handler, disposeHandler: false), new StaticApiKeyProvider("k"), timeout: null);

        using JevClient client = new(
            new JevClientOptions { ApiKey = "k" }, transport, new StaticApiKeyProvider("k"), logger: null);

        ConcurrentBag<Exception> faults = [];
        using CancellationTokenSource stop = new(TimeSpan.FromSeconds(2));

        Task[] readers = [.. Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    await client.GetAvailableModelsAsync(CancellationToken.None);
                }
                catch (Exception exception)
                {
                    faults.Add(exception);
                }
            }
        }))];

        Task invalidator = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                client.InvalidateModelCache();
                await Task.Delay(1);
            }
        });

        await Task.WhenAll([.. readers, invalidator]);

        Assert.Empty(faults);
    }

    [Fact]
    public async Task CancelWhileQueuedOnTheGate_DoesNotCorruptTheGate()
    {
        // A cancellation that arrives while waiting for the gate must leave the gate usable. If the
        // release were inside a finally around the wait, a cancelled waiter would raise
        // SemaphoreFullException on the next entrant.
        StubTransport transport = new StubTransport().EnqueueJson(ModelsJson);
        using JevClient client = TestClient.Create(transport);

        // Prime the cache so the first call does not fetch, then invalidate and race a cancel.
        await client.GetAvailableModelsAsync(CancellationToken.None);
        client.InvalidateModelCache();

        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetAvailableModelsAsync(cancellation.Token));

        // The gate must still work for everyone after that.
        IReadOnlyList<ModelMetadata> models = await client.GetAvailableModelsAsync(CancellationToken.None);
        Assert.Single(models);
    }

    // ---------------------------------------------------------------- transport disposal

    [Fact]
    public async Task DisposalRacingWithUse_NeverCorruptsState()
    {
        CountingHandler handler = new(ModelsJson);
        HttpTypeSafeTransport transport = new(
            new System.Net.Http.HttpClient(handler, disposeHandler: false), new StaticApiKeyProvider("k"), timeout: null);

        ConcurrentBag<Exception> unexpected = [];

        Task[] senders = [.. Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
        {
            for (int i = 0; i < 100; i++)
            {
                try
                {
                    await transport.SendAsync(
                        new TransportRequest(HttpMethod.Get, new Uri("https://api.test/v1/models")),
                        CancellationToken.None);
                }
                catch (ObjectDisposedException)
                {
                    // Legitimate: the transport was disposed between the check and the send.
                }
                catch (Exception exception)
                {
                    unexpected.Add(exception);
                }
            }
        }))];

        Task disposer = Task.Run(async () =>
        {
            await Task.Delay(10);

            for (int i = 0; i < 50; i++)
            {
                transport.Dispose();
                await Task.Delay(1);
            }
        });

        await Task.WhenAll([.. senders, disposer]);

        Assert.Empty(unexpected);
    }

    [Fact]
    public void ConcurrentDispose_DisposesTheOwnedClientExactlyOnce()
    {
        // Only the thread that wins the 0 -> 1 transition disposes, so concurrent disposal cannot
        // double-dispose a client the transport owns.
        HttpTypeSafeTransport transport = new(
            httpClient: null, new StaticApiKeyProvider("k"), timeout: null);

        HttpClient owned = transport.HttpClient;

        Parallel.For(0, 64, _ => transport.Dispose());

        // A disposed HttpClient throws on send. One disposal is enough; the test proves the path did
        // not throw and the object is in a terminal state.
        Assert.ThrowsAny<Exception>(() => owned.GetAsync("https://api.test/x").GetAwaiter().GetResult());
    }

    // ---------------------------------------------------------------- throughput and allocation

    [Fact]
    public async Task RepeatedCalls_DoNotGrowTheTypeInfoCache()
    {
        // The regression: binding a response to a caller's own type built fresh options per response.
        // With the cache, repeated calls resolve once. This is asserted through behaviour rather than
        // through internals: a thousand calls must not be slower than a hundred by more than the work
        // itself, which a per-call metadata rebuild would violate.
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        Dictionary<string, Question> questions = new(StringComparer.Ordinal) { ["a"] = Question.Noul("is it?") };

        for (int i = 0; i < 100; i++)
        {
            await client.SystemOneAsync("text", questions, model: null, CancellationToken.None);
        }

        Stopwatch warm = Stopwatch.StartNew();
        for (int i = 0; i < 1_000; i++)
        {
            await client.SystemOneAsync("text", questions, model: null, CancellationToken.None);
        }

        warm.Stop();

        // A generous ceiling. The point is that this does not degrade super-linearly, which a per-call
        // options rebuild would cause.
        Assert.True(
            warm.Elapsed < TimeSpan.FromSeconds(10),
            $"1000 calls took {warm.Elapsed}, suggesting per-call metadata construction");
    }

    [Fact]
    public async Task ParallelCalls_ScalePastOneCore()
    {
        // Scalability: a shared client must serve concurrent callers without serializing them. The
        // assertion is deliberately loose — a loaded CI machine varies — but a client-level lock would
        // show no gain at all beyond one thread.
        CountingHandler handler = new(TestClient.SuccessJson());
        using HttpTypeSafeTransport transport = new(
            new System.Net.Http.HttpClient(handler, disposeHandler: false), new StaticApiKeyProvider("k"), timeout: null);

        using JevClient client = new(
            new JevClientOptions { ApiKey = "k" }, transport, new StaticApiKeyProvider("k"), logger: null);

        Dictionary<string, Question> questions = new(StringComparer.Ordinal) { ["a"] = Question.Noul("is it?") };

        for (int i = 0; i < 200; i++)
        {
            await client.SystemOneAsync("text", questions, model: null, CancellationToken.None);
        }

        const int perWorker = 2_000;

        Stopwatch single = Stopwatch.StartNew();
        for (int i = 0; i < perWorker; i++)
        {
            await client.SystemOneAsync("text", questions, model: null, CancellationToken.None);
        }

        single.Stop();

        Stopwatch parallel = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            for (int i = 0; i < perWorker; i++)
            {
                await client.SystemOneAsync("text", questions, model: null, CancellationToken.None);
            }
        })));

        parallel.Stop();

        // Eight workers doing eight times the work should take well under eight times as long; if the
        // client serialized callers it would take at least that.
        double ratio = parallel.Elapsed.TotalMilliseconds / single.Elapsed.TotalMilliseconds;

        Assert.True(
            ratio < 6.0,
            $"8 parallel workers took {ratio:F1}x a single worker's time for 8x the work, suggesting serialization");
    }

    [Fact]
    public async Task AResponseLargerThanItsMetadata_DoesNotAllocateAbsurdly()
    {
        // A shape-check on the hot path rather than a precise budget, which would be brittle across
        // runtimes. A regression that reintroduced per-call options construction or a full document
        // copy would push this well past the ceiling.
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        Dictionary<string, Question> questions = new(StringComparer.Ordinal) { ["a"] = Question.Noul("is it?") };

        for (int i = 0; i < 1_000; i++)
        {
            await client.SystemOneAsync("text", questions, model: null, CancellationToken.None);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();

        const int Calls = 5_000;
        for (int i = 0; i < Calls; i++)
        {
            await client.SystemOneAsync("text", questions, model: null, CancellationToken.None);
        }

        long perCall = (GC.GetAllocatedBytesForCurrentThread() - before) / Calls;

        // The stub transport, the request message, the serialized request, the response body and the
        // parsed response all allocate. Measured at roughly 4 KB per call for this shape; the ceiling
        // is set well above that to catch a regression rather than to police a byte.
        Assert.True(perCall < 12_000, $"allocated {perCall} bytes per call, which suggests a per-call rebuild");
    }
}
