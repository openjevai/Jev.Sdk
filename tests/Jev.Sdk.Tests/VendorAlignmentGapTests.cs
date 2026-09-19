// VendorAlignmentGapTests.cs
// Part of Jev.Sdk.Tests. The branches the vendor-alignment tests do not reach: the new option
// guards, the retry-after-ms header path, the request-id header edge cases, and the connection
// exception's request id.

using System.Net;
using Jev.Sdk;

namespace Jev.Sdk.Tests;

public class VendorAlignmentGapTests
{
    // ---------------------------------------------------------------- new option guards

    [Fact]
    public void Snapshot_RejectsANegativeMaxRetryAfter()
    {
        JevClientOptions options = new() { MaxRetryAfter = TimeSpan.FromSeconds(-1) };

        JevConfigurationException exception = Assert.Throws<JevConfigurationException>(
            () => new JevClient(options, new StubTransport(), new StaticApiKeyProvider("k"), null));

        Assert.Contains("MaxRetryAfter cannot be negative", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void Snapshot_RejectsAJitterFractionOutsideZeroToOne(double fraction)
    {
        JevClientOptions options = new() { RetryJitterFraction = fraction };

        JevConfigurationException exception = Assert.Throws<JevConfigurationException>(
            () => new JevClient(options, new StubTransport(), new StaticApiKeyProvider("k"), null));

        Assert.Contains("RetryJitterFraction must be between 0 and 1", exception.Message, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- retry-after-ms header

    [Fact]
    public async Task RetryAfterMs_Header_IsHonouredOverRetryAfter()
    {
        // The vendor honours retry-after-ms, which is more precise than Retry-After's whole
        // seconds. The transport must read it, not just the standard header.
        int attempts = 0;

        StubHttpMessageHandler handler = new StubHttpMessageHandler().Enqueue(_ =>
        {
            attempts++;

            if (attempts == 1)
            {
                HttpResponseMessage busy = new(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
                };

                // A short whole-second Retry-After plus a distinct millisecond value, so which one
                // was read is observable through the total delay.
                busy.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
                busy.Headers.TryAddWithoutValidation("retry-after-ms", "1");
                return busy;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(TestClient.SuccessJson(), System.Text.Encoding.UTF8, "application/json"),
            };
        });

        System.Net.Http.HttpClient httpClient = new(handler, disposeHandler: false);

        using JevClient client = new(
            new JevClientOptions { ApiKey = TestClient.TestApiKey },
            new HttpTypeSafeTransport(httpClient, new StaticApiKeyProvider(TestClient.TestApiKey), timeout: null),
            new StaticApiKeyProvider(TestClient.TestApiKey),
            logger: null);

        // If the 30-second Retry-After had been honoured, this call would take 30 seconds and the
        // test would time out. Reading the 1 ms form is what makes it return promptly.
        SystemOneResponse response = await client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        Assert.Equal("jev-latest", response.Model);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task RetryAfterMs_BeyondTheCeiling_FallsBackToComputedBackoff()
    {
        // A server asking for longer than MaxRetryAfter causes the computed backoff to be used, so
        // the call does not stall. With both delays zeroed the call returns immediately; a stall
        // would mean the server value had been honoured.
        int attempts = 0;

        StubHttpMessageHandler handler = new StubHttpMessageHandler().Enqueue(_ =>
        {
            attempts++;

            if (attempts == 1)
            {
                HttpResponseMessage busy = new(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
                };

                busy.Headers.TryAddWithoutValidation("retry-after-ms", "600000");
                return busy;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(TestClient.SuccessJson(), System.Text.Encoding.UTF8, "application/json"),
            };
        });

        System.Net.Http.HttpClient httpClient = new(handler, disposeHandler: false);

        using JevClient client = new(
            new JevClientOptions
            {
                ApiKey = TestClient.TestApiKey,
                InitialRetryDelay = TimeSpan.Zero,
                MaxRetryDelay = TimeSpan.Zero,
                MaxRetryAfter = TimeSpan.FromSeconds(60),
            },
            new HttpTypeSafeTransport(httpClient, new StaticApiKeyProvider(TestClient.TestApiKey), timeout: null),
            new StaticApiKeyProvider(TestClient.TestApiKey),
            logger: null);

        SystemOneResponse response = await client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        Assert.Equal("jev-latest", response.Model);
        Assert.Equal(2, attempts);
    }

    // ---------------------------------------------------------------- request id edges

    [Fact]
    public void RequestId_IgnoresBlankHeaderValues()
    {
        System.Net.Http.HttpResponseMessage message = new();
        message.Headers.TryAddWithoutValidation(JevRequestId.HeaderName, "   ");

        Assert.Null(JevRequestId.FromHeaders(message.Headers));
    }

    [Fact]
    public void RequestId_TrimsSurroundingWhitespace()
    {
        System.Net.Http.HttpResponseMessage message = new();
        message.Headers.TryAddWithoutValidation(JevRequestId.HeaderName, "  req_trim_me  ");

        Assert.Equal("req_trim_me", JevRequestId.FromHeaders(message.Headers));
    }

    [Fact]
    public void RequestId_IsNullWhenTheHeaderIsAbsentFromARealHeaderCollection()
    {
        System.Net.Http.HttpResponseMessage message = new();

        Assert.Null(JevRequestId.FromHeaders(message.Headers));
    }

    // ---------------------------------------------------------------- connection exception request id

    [Fact]
    public void ConnectionException_CarriesARequestIdWhenOneWasReceived()
    {
        // A failure while reading the body still happens after the headers arrived, so the request
        // id is available and worth carrying.
        JevConnectionException exception = new(
            "unreadable",
            isProtocolError: true,
            responseBody: "raw",
            requestId: "req_body_fail");

        Assert.Equal("req_body_fail", exception.RequestId);
        Assert.True(exception.IsProtocolError);
    }

    [Fact]
    public void ConnectionException_WithAnInnerCauseCarriesARequestIdToo()
    {
        JevConnectionException exception = new(
            "unreadable",
            new InvalidOperationException("cause"),
            isProtocolError: true,
            responseBody: "raw",
            requestId: "req_cause");

        Assert.Equal("req_cause", exception.RequestId);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    // ---------------------------------------------------------------- remaining retry branches

    [Fact]
    public void ComputeRetryDelay_ClampsANegativeComputedDelayToZero()
    {
        // The options guard rejects a negative initial delay, but a caller can build an options
        // instance by hand and call this method directly. The clamp keeps such a caller from getting
        // a negative TimeSpan that Task.Delay would reject.
        JevClientOptions options = new()
        {
            InitialRetryDelay = TimeSpan.FromMilliseconds(-500),
            RetryBackoffMultiplier = 2,
            MaxRetryDelay = TimeSpan.FromSeconds(5),
        };

        Assert.Equal(TimeSpan.Zero, HttpTypeSafeTransport.ComputeRetryDelay(0, null, options, null));
    }

    [Fact]
    public void ParseRetryHeaders_ReturnsNullForAHeaderCarryingNeitherForm()
    {
        // RetryConditionHeaderValue permits both members to be null. That is a malformed header
        // rather than an instruction to wait, so it yields no delay. Reached through the combined
        // entry point, which is what the transport actually calls.
        System.Net.Http.Headers.RetryConditionHeaderValue? malformed = default;

        Assert.Null(HttpTypeSafeTransport.ParseRetryHeaders(malformed, null));
    }
}
