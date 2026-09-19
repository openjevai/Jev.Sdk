// RetryTests.cs
// Part of Jev.Sdk.Tests. Retry behaviour is a function of the response and the options, so it is
// asserted directly rather than inferred from timing. Delays are zero and jitter is off, which
// means these tests never wait.
//
// The retryable status set and the backoff shape are asserted against the vendor's documented
// defaults, because a client that drifts from them throttles differently from the SDKs a user may
// already be running.

using System.Net;
using Jev.Sdk;

namespace Jev.Sdk.Tests;

public class RetryTests
{
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData((HttpStatusCode)529)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task RetryableStatus_IsRetriedThenSucceeds(HttpStatusCode retryable)
    {
        StubTransport transport = new StubTransport()
            .EnqueueJson("""{"error":"transient"}""", retryable)
            .EnqueueJson(TestClient.SuccessJson());

        using JevClient client = TestClient.Create(transport);

        SystemOneResponse response = await client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        Assert.Equal(2, transport.RequestCount);
        Assert.Equal("jev-latest", response.Model);
    }

    [Fact]
    public async Task RetryAfter_IsHonouredOverComputedBackoff()
    {
        TimeSpan serverDelay = TimeSpan.FromSeconds(7);

        StubTransport transport = new StubTransport()
            .EnqueueJson("""{"error":"slow down"}""", HttpStatusCode.TooManyRequests, retryAfter: serverDelay)
            .EnqueueJson(TestClient.SuccessJson());

        using JevClient client = TestClient.Create(transport);

        await client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        Assert.Equal(2, transport.RequestCount);
    }

    [Fact]
    public void ComputeRetryDelay_PrefersServerSuppliedValue()
    {
        JevClientOptions options = new()
        {
            InitialRetryDelay = TimeSpan.FromMilliseconds(100),
            RetryBackoffMultiplier = 2,
            MaxRetryDelay = TimeSpan.FromSeconds(5),
            MaxRetryAfter = TimeSpan.FromSeconds(60),
        };

        TimeSpan delay = HttpTypeSafeTransport.ComputeRetryDelay(
            attempt: 0, retryAfter: TimeSpan.FromSeconds(5), options, jitterSource: null);

        Assert.Equal(TimeSpan.FromSeconds(5), delay);
    }

    [Fact]
    public void ComputeRetryDelay_DiscardsAServerValueBeyondMaxRetryAfter()
    {
        // A server asking for longer than MaxRetryAfter causes the computed backoff to be used
        // instead, on the reasoning that a caller waiting minutes inside one call would rather
        // fail and retry at its own level. The vendor's ceiling is 60 seconds.
        JevClientOptions options = new()
        {
            InitialRetryDelay = TimeSpan.FromMilliseconds(500),
            RetryBackoffMultiplier = 2,
            MaxRetryDelay = TimeSpan.FromSeconds(5),
            MaxRetryAfter = TimeSpan.FromSeconds(60),
        };

        TimeSpan delay = HttpTypeSafeTransport.ComputeRetryDelay(
            attempt: 0, retryAfter: TimeSpan.FromMinutes(5), options, jitterSource: null);

        // Fell back to the computed backoff, not the 5-minute server instruction.
        Assert.Equal(TimeSpan.FromMilliseconds(500), delay);
    }

    [Fact]
    public void ComputeRetryDelay_GrowsExponentially()
    {
        JevClientOptions options = new()
        {
            InitialRetryDelay = TimeSpan.FromMilliseconds(100),
            RetryBackoffMultiplier = 2,
            MaxRetryDelay = TimeSpan.FromSeconds(5),
        };

        Assert.Equal(TimeSpan.FromMilliseconds(100), HttpTypeSafeTransport.ComputeRetryDelay(0, null, options, null));
        Assert.Equal(TimeSpan.FromMilliseconds(200), HttpTypeSafeTransport.ComputeRetryDelay(1, null, options, null));
        Assert.Equal(TimeSpan.FromMilliseconds(400), HttpTypeSafeTransport.ComputeRetryDelay(2, null, options, null));
    }

    [Fact]
    public void ComputeRetryDelay_IsBoundedByMaxRetryDelay()
    {
        JevClientOptions options = new()
        {
            InitialRetryDelay = TimeSpan.FromMilliseconds(100),
            RetryBackoffMultiplier = 10,
            MaxRetryDelay = TimeSpan.FromSeconds(5),
        };

        TimeSpan delay = HttpTypeSafeTransport.ComputeRetryDelay(attempt: 5, retryAfter: null, options, jitterSource: null);

        Assert.Equal(TimeSpan.FromSeconds(5), delay);
    }

    [Fact]
    public void ComputeRetryDelay_JitterIsSubtractive()
    {
        // The vendor's jitter subtracts up to a fraction of the computed delay. It does not replace
        // the delay with a uniform random value, so the result always sits below the backoff and
        // above the floor set by the fraction.
        JevClientOptions options = new()
        {
            InitialRetryDelay = TimeSpan.FromSeconds(1),
            RetryBackoffMultiplier = 2,
            MaxRetryDelay = TimeSpan.FromSeconds(5),
            RetryJitterFraction = 0.25,
        };

        // A jitter source at its maximum subtracts the full jitter fraction, leaving 75%.
        TimeSpan mostJitter = HttpTypeSafeTransport.ComputeRetryDelay(0, null, options, () => 1.0);

        // A jitter source at zero subtracts nothing, leaving the full delay.
        TimeSpan noJitter = HttpTypeSafeTransport.ComputeRetryDelay(0, null, options, () => 0.0);

        Assert.Equal(TimeSpan.FromSeconds(1), noJitter);
        Assert.Equal(TimeSpan.FromMilliseconds(750), mostJitter);
    }

    [Fact]
    public void ComputeRetryDelay_DoesNotJitterAServerSuppliedValue()
    {
        JevClientOptions options = new()
        {
            InitialRetryDelay = TimeSpan.FromSeconds(1),
            RetryJitterFraction = 0.25,
            MaxRetryDelay = TimeSpan.FromSeconds(5),
            MaxRetryAfter = TimeSpan.FromSeconds(60),
        };

        // The server said how long to wait, so shortening it would risk another rejection.
        TimeSpan delay = HttpTypeSafeTransport.ComputeRetryDelay(0, TimeSpan.FromSeconds(4), options, () => 1.0);

        Assert.Equal(TimeSpan.FromSeconds(4), delay);
    }

    [Fact]
    public async Task ExhaustedRetries_SurfacesTheStatusSpecificException()
    {
        StubTransport transport = new StubTransport()
            .EnqueueJson("""{"error":"busy"}""", HttpStatusCode.TooManyRequests);

        using JevClient client = TestClient.Create(transport, o => o.MaxRetries = 2);

        JevRateLimitException exception = await Assert.ThrowsAsync<JevRateLimitException>(
            () => client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None));

        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
        Assert.Equal(3, transport.RequestCount);
    }

    [Fact]
    public async Task ZeroRetries_MeansNoRetrying()
    {
        StubTransport transport = new StubTransport()
            .EnqueueJson("""{"error":"busy"}""", HttpStatusCode.TooManyRequests);

        using JevClient client = TestClient.Create(transport, o => o.MaxRetries = 0);

        await Assert.ThrowsAsync<JevRateLimitException>(
            () => client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None));

        Assert.Equal(1, transport.RequestCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task ClientError_IsNeverRetried(HttpStatusCode status)
    {
        // Retrying cannot fix a malformed request, a bad credential, a permissions problem, or a
        // missing resource, so none of them is attempted twice.
        StubTransport transport = new StubTransport()
            .EnqueueJson("""{"error":"client side"}""", status);

        using JevClient client = TestClient.Create(transport);

        await Assert.ThrowsAnyAsync<JevApiException>(
            () => client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None));

        Assert.Equal(1, transport.RequestCount);
    }

    [Fact]
    public async Task TransportFailure_IsRetried()
    {
        StubTransport transport = new StubTransport()
            .EnqueueFailure()
            .EnqueueJson(TestClient.SuccessJson());

        using JevClient client = TestClient.Create(transport);

        SystemOneResponse response = await client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        Assert.Equal(2, transport.RequestCount);
        Assert.Equal("jev-latest", response.Model);
    }

    [Fact]
    public async Task TransportFailure_Exhausted_ThrowsConnectionException()
    {
        StubTransport transport = new StubTransport().EnqueueFailure("host unreachable");

        using JevClient client = TestClient.Create(transport, o => o.MaxRetries = 1);

        JevConnectionException exception = await Assert.ThrowsAsync<JevConnectionException>(
            () => client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None));

        Assert.False(exception.IsProtocolError);
        Assert.Equal(2, transport.RequestCount);
    }

    [Fact]
    public void DefaultRetryPolicy_MatchesTheVendorSdks()
    {
        // These values are the vendor's documented defaults, and matching them is the point: a
        // caller running the Python SDK and this one against the same limit should throttle the
        // same way.
        JevClientOptions options = new();

        Assert.Equal(2, options.MaxRetries);
        Assert.Equal(TimeSpan.FromMilliseconds(500), options.InitialRetryDelay);
        Assert.Equal(TimeSpan.FromSeconds(5), options.MaxRetryDelay);
        Assert.Equal(TimeSpan.FromSeconds(60), options.MaxRetryAfter);
        Assert.Equal(0.25, options.RetryJitterFraction);
        Assert.Equal(TimeSpan.FromSeconds(10), options.Timeout);
    }

    [Fact]
    public void IsRetryableStatus_CoversTheVendorSet()
    {
        // 408 and every 5xx, plus 429. 529 is inside the 5xx range rather than named separately,
        // which is why it does not need its own entry.
        Assert.True(HttpTypeSafeTransport.IsRetryableStatus(HttpStatusCode.RequestTimeout));
        Assert.True(HttpTypeSafeTransport.IsRetryableStatus(HttpStatusCode.TooManyRequests));
        Assert.True(HttpTypeSafeTransport.IsRetryableStatus((HttpStatusCode)529));
        Assert.True(HttpTypeSafeTransport.IsRetryableStatus(HttpStatusCode.InternalServerError));
        Assert.True(HttpTypeSafeTransport.IsRetryableStatus(HttpStatusCode.BadGateway));
        Assert.True(HttpTypeSafeTransport.IsRetryableStatus(HttpStatusCode.ServiceUnavailable));
        Assert.True(HttpTypeSafeTransport.IsRetryableStatus(HttpStatusCode.GatewayTimeout));

        Assert.False(HttpTypeSafeTransport.IsRetryableStatus(HttpStatusCode.BadRequest));
        Assert.False(HttpTypeSafeTransport.IsRetryableStatus(HttpStatusCode.Unauthorized));
        Assert.False(HttpTypeSafeTransport.IsRetryableStatus(HttpStatusCode.Forbidden));
        Assert.False(HttpTypeSafeTransport.IsRetryableStatus(HttpStatusCode.NotFound));
        Assert.False(HttpTypeSafeTransport.IsRetryableStatus(HttpStatusCode.UnprocessableEntity));
    }

    [Fact]
    public void RetryAfterMs_IsPreferredOverRetryAfter()
    {
        // The millisecond form is more precise than Retry-After's whole seconds, so when both are
        // present the millisecond value wins.
        TimeSpan? parsed = HttpTypeSafeTransport.ParseRetryHeaders(
            new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(9)),
            "1500");

        Assert.Equal(TimeSpan.FromMilliseconds(1500), parsed);
    }

    [Fact]
    public void RetryAfterMs_FallsBackToRetryAfterWhenUnparseable()
    {
        TimeSpan? parsed = HttpTypeSafeTransport.ParseRetryHeaders(
            new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(9)),
            "not-a-number");

        Assert.Equal(TimeSpan.FromSeconds(9), parsed);
    }

    [Fact]
    public void RetryAfterMs_IsNullWhenNeitherHeaderIsPresent()
    {
        Assert.Null(HttpTypeSafeTransport.ParseRetryHeaders(null, null));
        Assert.Null(HttpTypeSafeTransport.ParseRetryHeaders(null, "   "));
    }
}
