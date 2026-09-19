// VerifierRegressionTests.cs
// Part of Jev.Sdk.Tests. Regressions for the four defects an independent verification pass found by
// probing behaviour. Each was confirmed by execution before being fixed, and each has a test here so
// it cannot return silently.
//
//   1. Jitter was never applied in production. The public constructor hardcoded the injectable jitter
//      source to null, so RetryJitterFraction was inert and every retry used the exact computed
//      backoff — the synchronized-retry problem the setting exists to solve.
//   2. The dependency-injection key chain omitted the explicit options key, so an explicitly
//      configured key lost to TYPESAFE_API_KEY, contradicting the documented order.
//   3. A JSON null for the models member threw ArgumentNullException out of method contracts that
//      document no throw, and out of the JevException hierarchy entirely, so `catch (JevException)`
//      did not catch it.
//   4. Verified correct during the same pass: the timeout is per attempt and distinguishable from
//      caller cancellation, and custom headers reach the wire. Asserted here so they stay that way.

using System.Diagnostics;
using System.Net;
using Jev.Sdk;
using Jev.Sdk.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Jev.Sdk.Tests;

public class VerifierRegressionTests
{
    // ---------------------------------------------------------------- 1. production jitter

    [Fact]
    public async Task ProductionRetryDelays_AreActuallyJittered()
    {
        // The regression: jitter was configured but never applied, so every retry waited exactly the
        // computed backoff. Measured across several runs, the observed delay must vary.
        List<double> gaps = [];

        for (int run = 0; run < 12; run++)
        {
            List<DateTimeOffset> attempts = [];

            HttpMessageHandler handler = new DelegateHandler(_ =>
            {
                attempts.Add(DateTimeOffset.UtcNow);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
                });
            });

            // Built through the public path, so no injected jitter source. This is what production does.
            using JevClient client = new(
                new JevClientOptions
                {
                    ApiKey = TestClient.TestApiKey,
                    InitialRetryDelay = TimeSpan.FromMilliseconds(200),
                    RetryJitterFraction = 0.25,
                    MaxRetries = 1,
                    MaxRetryDelay = TimeSpan.FromMilliseconds(200),
                },
                new HttpTypeSafeTransport(new System.Net.Http.HttpClient(handler, disposeHandler: false), new StaticApiKeyProvider("k"), timeout: null),
                new StaticApiKeyProvider(TestClient.TestApiKey),
                logger: null);

            try
            {
                await client.GetModelsAsync(CancellationToken.None);
            }
            catch (JevException)
            {
            }

            if (attempts.Count >= 2)
            {
                gaps.Add((attempts[1] - attempts[0]).TotalMilliseconds);
            }
        }

        Assert.True(gaps.Count >= 8, $"expected several measured gaps, got {gaps.Count}");

        // 0.25 subtractive jitter on a 200 ms delay lands in [150, 200] ms. With the defect every gap
        // was 200 ms, so the spread is what proves jitter is live.
        Assert.All(gaps, gap => Assert.InRange(gap, 100, 260));

        double spread = gaps.Max() - gaps.Min();
        Assert.True(spread > 5, $"retry delays did not vary (spread {spread:F1} ms): jitter is not being applied");
    }

    [Fact]
    public void ComputeRetryDelay_WithARealRandomSource_StaysWithinTheJitterWindow()
    {
        // The production jitter source is Random.Shared.NextDouble. Whatever it returns, the delay must
        // sit inside the documented window.
        JevClientOptions options = new()
        {
            InitialRetryDelay = TimeSpan.FromSeconds(1),
            RetryBackoffMultiplier = 2,
            MaxRetryDelay = TimeSpan.FromSeconds(5),
            RetryJitterFraction = 0.25,
        };

        for (int i = 0; i < 200; i++)
        {
            TimeSpan delay = HttpTypeSafeTransport.ComputeRetryDelay(
                0, retryAfter: null, options, Random.Shared.NextDouble);

            Assert.InRange(delay.TotalMilliseconds, 750, 1000);
        }
    }

    // ---------------------------------------------------------------- 2. DI key precedence

    [Fact]
    public async Task DependencyInjection_ExplicitKeyBeatsTheEnvironmentVariable()
    {
        // The regression: the chain passed to the client omitted the explicit key, and because the
        // client's own ladder is replaced by that chain, an explicit key silently lost to the
        // environment.
        const string variableName = "TYPESAFE_API_KEY";
        string? previous = Environment.GetEnvironmentVariable(variableName);

        try
        {
            Environment.SetEnvironmentVariable(variableName, "ENV-KEY");

            ServiceCollection services = new();
            services.AddLogging();
            JevServiceCollectionExtensions.AddJevClient(
                services,
                configuration: null,
                configure: options => options.ApiKey = "EXPLICIT-KEY");

            using ServiceProvider provider = services.BuildServiceProvider();

            IApiKeyProvider keyProvider = provider.GetRequiredService<IApiKeyProvider>();
            string? used = await keyProvider.GetApiKeyAsync(CancellationToken.None);

            Assert.Equal("EXPLICIT-KEY", used);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, previous);
        }
    }

    [Fact]
    public async Task DependencyInjection_FallsBackToTheEnvironmentWhenNoExplicitKeyIsSet()
    {
        const string variableName = "TYPESAFE_API_KEY";
        string? previous = Environment.GetEnvironmentVariable(variableName);

        try
        {
            Environment.SetEnvironmentVariable(variableName, "ENV-KEY");

            ServiceCollection services = new();
            services.AddLogging();
            JevServiceCollectionExtensions.AddJevClient(services);

            using ServiceProvider provider = services.BuildServiceProvider();

            IApiKeyProvider keyProvider = provider.GetRequiredService<IApiKeyProvider>();

            Assert.Equal("ENV-KEY", await keyProvider.GetApiKeyAsync(CancellationToken.None));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, previous);
        }
    }

    [Fact]
    public async Task DependencyInjection_ExplicitKeyBeatsASettingsFile()
    {
        ServiceCollection services = new();
        services.AddLogging();

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Jev:ApiKey"] = "FILE-KEY",
            })
            .Build();

        JevServiceCollectionExtensions.AddJevClient(
            services,
            configuration,
            configure: options => options.ApiKey = "EXPLICIT-KEY");

        using ServiceProvider provider = services.BuildServiceProvider();

        IApiKeyProvider keyProvider = provider.GetRequiredService<IApiKeyProvider>();

        Assert.Equal("EXPLICIT-KEY", await keyProvider.GetApiKeyAsync(CancellationToken.None));
    }

    // ---------------------------------------------------------------- 3. null models member

    [Theory]
    [InlineData("""{"models":null}""")]
    [InlineData("""{}""")]
    public async Task ANullOrAbsentModelsMember_DoesNotThrow(string body)
    {
        // The regression: `[.. response.Models]` threw ArgumentNullException, which is not a JevException
        // and is not documented by any of these methods.
        StubTransport transport = new StubTransport().EnqueueJson(body);
        using JevClient client = TestClient.Create(transport);

        IReadOnlyList<ModelMetadata> models = await client.GetModelsAsync(CancellationToken.None);
        Assert.Empty(models);

        Assert.Equal(TimeSpan.FromHours(1), client.ModelCacheDuration);
    }

    [Fact]
    public async Task ANullModelsMember_SurvivesEveryModelMethod()
    {
        // Each of these documents a benign outcome for an unavailable list, so none may throw.
        StubTransport transport = new StubTransport().EnqueueJson("""{"models":null}""");
        using JevClient client = TestClient.Create(transport);

        Assert.Empty(await client.GetAvailableModelsAsync(CancellationToken.None));
        Assert.False(await client.IsModelAvailableAsync("anything", CancellationToken.None));
        Assert.Equal("jev-latest", await client.ResolveModelAsync(preferred: null, CancellationToken.None));
        Assert.True(await client.WarmModelsAsync(CancellationToken.None));
    }

    [Fact]
    public void ModelListResponse_ExposesANeverNullAccessor()
    {
        ModelListResponse response = new() { Models = null };

        Assert.Empty(response.ModelsOrEmpty);

        // And it is not serialized, so it never appears in a payload the API does not define.
        string written = System.Text.Json.JsonSerializer.Serialize(response, JevJsonContext.Default.Options);
        Assert.DoesNotContain("models_or_empty", written, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- 4. properties verified correct

    [Fact]
    public async Task CustomHeaders_ReachTheWireOnEveryAttempt()
    {
        List<string?> seen = [];

        HttpMessageHandler handler = new HeaderCapturingHandler(seen);

        JevClientOptions options = new()
        {
            ApiKey = TestClient.TestApiKey,
            InitialRetryDelay = TimeSpan.Zero,
        };

        options.Headers["X-Tenant"] = "acme";

        using JevClient client = new(
            options,
            new HttpTypeSafeTransport(new System.Net.Http.HttpClient(handler, disposeHandler: false), new StaticApiKeyProvider("k"), timeout: null),
            new StaticApiKeyProvider(TestClient.TestApiKey),
            logger: null);

        await client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        Assert.All(seen, value => Assert.Equal("acme", value));
    }

    /// <summary>Captures a chosen header from every request it sees.</summary>
    private sealed class HeaderCapturingHandler : HttpMessageHandler
    {
        private readonly List<string?> _seen;

        internal HeaderCapturingHandler(List<string?> seen) => _seen = seen;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _seen.Add(request.Headers.TryGetValues("X-Tenant", out IEnumerable<string>? values) ? values.FirstOrDefault() : null);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(TestClient.SuccessJson(), System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    [Fact]
    public async Task Timeout_FiresPerAttemptAndCallerCancellationStaysDistinct()
    {
        // Both properties in one place: a first attempt that times out is retried with a fresh deadline,
        // and a caller-initiated cancel is not relabelled a timeout.
        int attempt = 0;

        HttpMessageHandler handler = new DelegateHandler(async cancellationToken =>
        {
            attempt++;

            if (attempt == 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(TestClient.SuccessJson(), System.Text.Encoding.UTF8, "application/json"),
            };
        });

        using (JevClient client = new(
            new JevClientOptions
            {
                ApiKey = TestClient.TestApiKey,
                InitialRetryDelay = TimeSpan.Zero,
                MaxRetryDelay = TimeSpan.Zero,
                MaxRetries = 1,
            },
            new HttpTypeSafeTransport(new System.Net.Http.HttpClient(handler, disposeHandler: false), new StaticApiKeyProvider("k"), TimeSpan.FromMilliseconds(300)),
            new StaticApiKeyProvider(TestClient.TestApiKey),
            logger: null))
        {
            SystemOneResponse response = await client.SystemOneAsync(
                "text", TestClient.ThreeQuestions(), model: null, CancellationToken.None);

            Assert.Equal("jev-latest", response.Model);
            Assert.Equal(2, attempt);
        }

        // Cancellation, separately.
        HttpMessageHandler slow = new DelegateHandler(async cancellationToken =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using JevClient cancelling = new(
            new JevClientOptions { ApiKey = TestClient.TestApiKey, InitialRetryDelay = TimeSpan.Zero },
            new HttpTypeSafeTransport(new System.Net.Http.HttpClient(slow, disposeHandler: false), new StaticApiKeyProvider("k"), TimeSpan.FromSeconds(30)),
            new StaticApiKeyProvider(TestClient.TestApiKey),
            logger: null);

        using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(150));

        Stopwatch clock = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cancelling.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, cancellation.Token));

        clock.Stop();
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"took {clock.Elapsed}, so the timeout was misreported as a cancel");
    }
}
