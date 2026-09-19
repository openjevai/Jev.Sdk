// TimeoutTests.cs
// Part of Jev.Sdk.Tests. Timeout behaviour, asserted against wall-clock observations rather than
// against configuration, because configuration being set does not mean the deadline is enforced.
//
// Four properties matter, and each has its own trap:
//
//   1. The timeout actually fires. Setting a value that nothing reads is the classic version of
//      this bug.
//   2. It is per attempt, not a whole-call budget. Three retries of two seconds each is a six-second
//      call, not a two-second one.
//   3. The caller's cancellation stays distinguishable from the timeout. A caller that cancelled
//      must not be told it hit a network timeout.
//   4. A caller-supplied HttpClient that has already served a request must still work. HttpClient
//      forbids setting Timeout once it has started a request, and a pooled or singleton client is a
//      normal thing to pass in.

using System.Diagnostics;
using System.Net;
using Jev.Sdk;

namespace Jev.Sdk.Tests;

public class TimeoutTests
{
    /// <summary>A handler that delays for a fixed period before answering.</summary>
    private sealed class DelayHandler : HttpMessageHandler
    {
        private readonly TimeSpan _delay;

        internal DelayHandler(TimeSpan delay) => _delay = delay;

        internal int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;

            await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"models":[]}""", System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }

    /// <summary>
    /// A handler that answers headers immediately then stalls the body.
    /// </summary>
    /// <remarks>
    /// The stall is delivered through a stream that honours the cancellation token on read, which is
    /// how a real network stream behaves. A content whose delay ignored the token would be
    /// uninterruptible, and the test would prove nothing about the timeout.
    /// </remarks>
    private sealed class StallingBodyHandler : HttpMessageHandler
    {
        private readonly TimeSpan _stall;

        internal StallingBodyHandler(TimeSpan stall) => _stall = stall;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new StallingStream(_stall)),
            });

        private sealed class StallingStream : Stream
        {
            private readonly TimeSpan _stall;

            internal StallingStream(TimeSpan stall) => _stall = stall;

            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override async ValueTask<int> ReadAsync(
                Memory<byte> buffer,
                CancellationToken cancellationToken = default)
            {
                // A network read waits and is interruptible, which is the property under test.
                await Task.Delay(_stall, cancellationToken).ConfigureAwait(false);
                return 0;
            }

            public override int Read(byte[] buffer, int offset, int count) => 0;

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }

    [Fact]
    public async Task Timeout_Fires()
    {
        DelayHandler handler = new(TimeSpan.FromSeconds(30));
        using HttpTypeSafeTransport transport = new(
            new HttpClient(handler, disposeHandler: false),
            new StaticApiKeyProvider("k"),
            TimeSpan.FromMilliseconds(300));

        Stopwatch clock = Stopwatch.StartNew();

        JevConnectionException exception = await Assert.ThrowsAsync<JevConnectionException>(
            () => transport.SendAsync(
                new TransportRequest(HttpMethod.Get, new Uri("https://api.test/v1/models")),
                CancellationToken.None));

        clock.Stop();

        Assert.Contains("timed out", exception.Message, StringComparison.Ordinal);
        Assert.False(exception.IsProtocolError);

        // The deadline actually stopped the call, rather than the 30-second handler running to
        // completion while the timeout was merely configured.
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"took {clock.Elapsed}");
    }

    [Fact]
    public async Task Timeout_IsPerAttemptNotAWholeCallBudget()
    {
        // Three sequential calls, each taking longer than half the timeout. All three must pass,
        // which is only true if each gets its own full deadline.
        DelayHandler handler = new(TimeSpan.FromMilliseconds(400));
        using HttpTypeSafeTransport transport = new(
            new HttpClient(handler, disposeHandler: false),
            new StaticApiKeyProvider("k"),
            TimeSpan.FromSeconds(1));

        for (int i = 0; i < 3; i++)
        {
            TransportResponse response = await transport.SendAsync(
                new TransportRequest(HttpMethod.Get, new Uri("https://api.test/v1/models")),
                CancellationToken.None);

            Assert.True(response.IsSuccess);
        }

        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task Timeout_AppliesToEachRetryAttemptSeparately()
    {
        // Through the client: the first attempt times out, the retry gets its own deadline and
        // succeeds. A whole-call budget would fail the second attempt too.
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

        using JevClient client = new(
            new JevClientOptions
            {
                ApiKey = TestClient.TestApiKey,
                InitialRetryDelay = TimeSpan.Zero,
                MaxRetryDelay = TimeSpan.Zero,
                MaxRetries = 2,
            },
            new HttpTypeSafeTransport(new HttpClient(handler, disposeHandler: false), new StaticApiKeyProvider("k"), TimeSpan.FromMilliseconds(400)),
            new StaticApiKeyProvider(TestClient.TestApiKey),
            logger: null);

        SystemOneResponse response = await client.SystemOneAsync(
            "text", TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        Assert.Equal("jev-latest", response.Model);
        Assert.Equal(2, attempt);
    }

    [Fact]
    public async Task CallerCancellation_IsNotReportedAsATimeout()
    {
        // Same exception type from the framework, opposite meaning. A caller that cancelled must see
        // a cancellation, or it will believe its own deliberate stop was a network failure.
        DelayHandler handler = new(TimeSpan.FromSeconds(30));
        using HttpTypeSafeTransport transport = new(
            new HttpClient(handler, disposeHandler: false),
            new StaticApiKeyProvider("k"),
            TimeSpan.FromSeconds(60));

        using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => transport.SendAsync(
                new TransportRequest(HttpMethod.Get, new Uri("https://api.test/v1/models")),
                cancellation.Token));
    }

    [Fact]
    public async Task CallerCancellation_WinsWhenShorterThanTheTimeout()
    {
        // Both deadlines are live; the caller's is shorter, so the caller's cancellation is what
        // surfaces.
        DelayHandler handler = new(TimeSpan.FromSeconds(30));
        using HttpTypeSafeTransport transport = new(
            new HttpClient(handler, disposeHandler: false),
            new StaticApiKeyProvider("k"),
            TimeSpan.FromSeconds(30));

        using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(150));

        Stopwatch clock = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => transport.SendAsync(
                new TransportRequest(HttpMethod.Get, new Uri("https://api.test/v1/models")),
                cancellation.Token));
        clock.Stop();

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"took {clock.Elapsed}");
    }

    [Fact]
    public async Task Timeout_WinsWhenShorterThanTheCallerToken()
    {
        // The mirror of the previous test: the transport's deadline is shorter, so a
        // JevConnectionException is what surfaces.
        DelayHandler handler = new(TimeSpan.FromSeconds(30));
        using HttpTypeSafeTransport transport = new(
            new HttpClient(handler, disposeHandler: false),
            new StaticApiKeyProvider("k"),
            TimeSpan.FromMilliseconds(200));

        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(30));

        await Assert.ThrowsAsync<JevConnectionException>(
            () => transport.SendAsync(
                new TransportRequest(HttpMethod.Get, new Uri("https://api.test/v1/models")),
                cancellation.Token));
    }

    [Fact]
    public async Task AnAlreadyUsedHttpClient_IsAccepted()
    {
        // HttpClient forbids setting Timeout once it has started a request. A pooled or singleton
        // client is a normal thing for a caller to pass, and what IHttpClientFactory hands back, so
        // the constructor must not touch that property.
        HttpClient reused = new(new DelayHandler(TimeSpan.Zero), disposeHandler: false);

        // Use it first, which is what makes the property immutable.
        using (HttpResponseMessage warmup = await reused.GetAsync(
            new Uri("https://api.test/warmup"),
            CancellationToken.None))
        {
            Assert.Equal(HttpStatusCode.OK, warmup.StatusCode);
        }

        // The constructor must succeed rather than throwing InvalidOperationException.
        using HttpTypeSafeTransport transport = new(
            reused,
            new StaticApiKeyProvider("k"),
            TimeSpan.FromSeconds(5));

        Assert.Equal(TimeSpan.FromSeconds(5), transport.Timeout);

        TransportResponse response = await transport.SendAsync(
            new TransportRequest(HttpMethod.Get, new Uri("https://api.test/v1/models")),
            CancellationToken.None);

        Assert.True(response.IsSuccess);
    }

    [Fact]
    public async Task CallerSuppliedHttpClientTimeout_IsLeftAlone()
    {
        // The transport must not mutate a client the caller owns.
        HttpClient supplied = new(new DelayHandler(TimeSpan.Zero), disposeHandler: false)
        {
            Timeout = TimeSpan.FromSeconds(37),
        };

        using HttpTypeSafeTransport transport = new(supplied, new StaticApiKeyProvider("k"), TimeSpan.FromSeconds(5));

        Assert.Equal(TimeSpan.FromSeconds(37), supplied.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(5), transport.Timeout);

        // And it still works, because both deadlines are live and ours is the shorter one.
        await transport.SendAsync(
            new TransportRequest(HttpMethod.Get, new Uri("https://api.test/v1/models")),
            CancellationToken.None);
    }

    [Fact]
    public async Task AStallingBody_IsBoundedByTheTimeout()
    {
        // With ResponseHeadersRead the send returns as soon as headers arrive, so a server that sends
        // headers promptly and then stalls the body would hold the call open indefinitely unless the
        // read is bounded too.
        StallingBodyHandler handler = new(TimeSpan.FromSeconds(30));
        using HttpTypeSafeTransport transport = new(
            new HttpClient(handler, disposeHandler: false),
            new StaticApiKeyProvider("k"),
            TimeSpan.FromMilliseconds(400));

        Stopwatch clock = Stopwatch.StartNew();

        await Assert.ThrowsAsync<JevConnectionException>(
            () => transport.SendAsync(
                new TransportRequest(HttpMethod.Get, new Uri("https://api.test/v1/models")),
                CancellationToken.None));

        clock.Stop();
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"took {clock.Elapsed}");
    }

    [Fact]
    public async Task ZeroTimeout_LeavesTheClientTimeoutInEffect()
    {
        // A caller who zeroes the per-attempt timeout gets the client's own default rather than a
        // token that cancels immediately, which would fail every call.
        HttpClient supplied = new(new DelayHandler(TimeSpan.Zero), disposeHandler: false)
        {
            Timeout = TimeSpan.FromSeconds(30),
        };

        using HttpTypeSafeTransport transport = new(supplied, new StaticApiKeyProvider("k"), TimeSpan.Zero);

        Assert.Null(transport.Timeout);

        TransportResponse response = await transport.SendAsync(
            new TransportRequest(HttpMethod.Get, new Uri("https://api.test/v1/models")),
            CancellationToken.None);

        Assert.True(response.IsSuccess);
    }

    [Fact]
    public async Task ANegativeTimeout_IsTreatedAsNoTimeout()
    {
        using HttpTypeSafeTransport transport = new(
            new HttpClient(new DelayHandler(TimeSpan.Zero), disposeHandler: false),
            new StaticApiKeyProvider("k"),
            TimeSpan.FromSeconds(-5));

        Assert.Null(transport.Timeout);

        TransportResponse response = await transport.SendAsync(
            new TransportRequest(HttpMethod.Get, new Uri("https://api.test/v1/models")),
            CancellationToken.None);

        Assert.True(response.IsSuccess);
    }

    [Fact]
    public void HttpClient_IsExposedForDiagnostics()
    {
        // A caller may want to inspect or configure the client it supplied. The property exists so
        // that is possible without reaching into the transport's internals.
        HttpClient supplied = new(new DelayHandler(TimeSpan.Zero), disposeHandler: false);
        using HttpTypeSafeTransport transport = new(supplied, new StaticApiKeyProvider("k"), timeout: null);

        Assert.Same(supplied, transport.HttpClient);
    }

    [Fact]
    public void DefaultTimeout_MatchesTheVendorSdk()
    {
        // The vendor documents a 10-second per-HTTP-operation default, and it is per attempt.
        JevClientOptions options = new();

        Assert.Equal(TimeSpan.FromSeconds(10), options.Timeout);
    }

    [Fact]
    public async Task ClientAppliesItsConfiguredTimeoutToTheTransportItBuilds()
    {
        // A client that builds its own transport must pass its timeout through, or the option would
        // be inert.
        using JevClient client = new(new JevClientOptions
        {
            ApiKey = TestClient.TestApiKey,
            Timeout = TimeSpan.FromSeconds(17),
        });

        HttpTypeSafeTransport transport = Assert.IsType<HttpTypeSafeTransport>(
            typeof(JevClient)
                .GetField("_transport", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(client));

        Assert.Equal(TimeSpan.FromSeconds(17), transport.Timeout);
    }
}

/// <summary>A handler whose behaviour is supplied per call, for tests that need a sequence.</summary>
internal sealed class DelegateHandler : HttpMessageHandler
{
    private readonly Func<CancellationToken, Task<HttpResponseMessage>> _handler;

    internal DelegateHandler(Func<CancellationToken, Task<HttpResponseMessage>> handler) => _handler = handler;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        _handler(cancellationToken);
}
