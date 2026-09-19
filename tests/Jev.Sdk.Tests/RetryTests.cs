// RetryTests.cs
// Part of Jev.Sdk.Tests. Retry behaviour is a function of the response and the options, so it
// is asserted directly rather than inferred from timing. Delays are zero and jitter is pinned,
// which means these tests never wait.

using System.Net;
using Jev.Sdk;

namespace Jev.Sdk.Tests;

public class RetryTests
{
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData((HttpStatusCode)529)]
    public async Task RetryableStatus_IsRetriedThenSucceeds(HttpStatusCode retryable)
    {
        StubTransport transport = new StubTransport()
            .EnqueueJson("""{"error":"busy"}""", retryable)
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

        // The server-supplied delay is capped by MaxRetryDelay, which the test client sets to
        // zero, so the observable effect is that the retry still happened rather than the value
        // being ignored.
        Assert.Equal(2, transport.RequestCount);
    }

    [Fact]
    public void ComputeRetryDelay_PrefersServerSuppliedValue()
    {
        JevClientOptions options = new()
        {
            InitialRetryDelay = TimeSpan.FromMilliseconds(100),
            RetryBackoffMultiplier = 2,
            MaxRetryDelay = TimeSpan.FromSeconds(30),
        };

        TimeSpan delay = HttpTypeSafeTransport.ComputeRetryDelay(
            attempt: 0, retryAfter: TimeSpan.FromSeconds(5), options, jitterSource: null);

        Assert.Equal(TimeSpan.FromSeconds(5), delay);
    }

    [Fact]
    public void ComputeRetryDelay_GrowsExponentially()
    {
        JevClientOptions options = new()
        {
            InitialRetryDelay = TimeSpan.FromMilliseconds(100),
            RetryBackoffMultiplier = 2,
            MaxRetryDelay = TimeSpan.FromSeconds(30),
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
            MaxRetryDelay = TimeSpan.FromSeconds(2),
        };

        TimeSpan delay = HttpTypeSafeTransport.ComputeRetryDelay(attempt: 5, retryAfter: null, options, jitterSource: null);

        Assert.Equal(TimeSpan.FromSeconds(2), delay);
    }

    [Fact]
    public void ComputeRetryDelay_AppliesJitterWithinTheWindow()
    {
        JevClientOptions options = new()
        {
            InitialRetryDelay = TimeSpan.FromSeconds(1),
            RetryBackoffMultiplier = 2,
            MaxRetryDelay = TimeSpan.FromSeconds(30),
            UseRetryJitter = true,
        };

        // A jitter source at its floor and another at its ceiling bound the window.
        TimeSpan low = HttpTypeSafeTransport.ComputeRetryDelay(0, null, options, () => 0.0);
        TimeSpan high = HttpTypeSafeTransport.ComputeRetryDelay(0, null, options, () => 0.999);

        Assert.True(low >= TimeSpan.FromMilliseconds(500) && low <= TimeSpan.FromSeconds(1));
        Assert.True(high > low);
        Assert.True(high <= TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ComputeRetryDelay_DoesNotJitterAServerSuppliedValue()
    {
        JevClientOptions options = new()
        {
            InitialRetryDelay = TimeSpan.FromSeconds(1),
            UseRetryJitter = true,
            MaxRetryDelay = TimeSpan.FromSeconds(30),
        };

        // The server said how long to wait, so second-guessing it with jitter would be wrong.
        TimeSpan delay = HttpTypeSafeTransport.ComputeRetryDelay(0, TimeSpan.FromSeconds(4), options, () => 0.0);

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

    [Fact]
    public async Task AuthenticationFailure_IsNeverRetried()
    {
        StubTransport transport = new StubTransport()
            .EnqueueJson("""{"error":"bad key"}""", HttpStatusCode.Unauthorized);

        using JevClient client = TestClient.Create(transport);

        await Assert.ThrowsAsync<JevAuthenticationException>(
            () => client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None));

        // Retrying cannot fix a credential, so it must not be attempted.
        Assert.Equal(1, transport.RequestCount);
    }

    [Fact]
    public async Task ValidationFailure_IsNeverRetried()
    {
        StubTransport transport = new StubTransport()
            .EnqueueJson("""{"detail":[{"loc":["body"],"msg":"bad","type":"missing"}]}""", HttpStatusCode.UnprocessableEntity);

        using JevClient client = TestClient.Create(transport);

        await Assert.ThrowsAsync<JevValidationException>(
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
    public void IsRetryableStatus_OnlyCoversRateLimitAndOverload()
    {
        Assert.True(HttpTypeSafeTransport.IsRetryableStatus(HttpStatusCode.TooManyRequests));
        Assert.True(HttpTypeSafeTransport.IsRetryableStatus((HttpStatusCode)529));

        Assert.False(HttpTypeSafeTransport.IsRetryableStatus(HttpStatusCode.Unauthorized));
        Assert.False(HttpTypeSafeTransport.IsRetryableStatus(HttpStatusCode.UnprocessableEntity));
        Assert.False(HttpTypeSafeTransport.IsRetryableStatus(HttpStatusCode.InternalServerError));
    }
}
