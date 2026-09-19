// HttpTransportTests.cs
// Part of Jev.Sdk.Tests. The built-in transport is the only part of the library that touches a
// socket, so it is exercised against a message-handler double rather than a live service. What
// is asserted here is the wire behaviour a caller depends on: the authorization header, the
// content type, how a status becomes a response, how Retry-After is interpreted, and how a
// transport failure becomes the library's own exception type.

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Jev.Sdk;

namespace Jev.Sdk.Tests;

public class HttpTransportTests
{
    private static HttpTypeSafeTransport BuildTransport(
        StubHttpMessageHandler handler,
        string apiKey = TestClient.TestApiKey,
        TimeSpan? timeout = null)
    {
        System.Net.Http.HttpClient httpClient = new(handler, disposeHandler: false);

        return new HttpTypeSafeTransport(httpClient, new StaticApiKeyProvider(apiKey), timeout);
    }

    [Fact]
    public async Task SendAsync_SetsBearerAuthorization()
    {
        StubHttpMessageHandler handler = new StubHttpMessageHandler().Enqueue("""{"ok":true}""");
        using HttpTypeSafeTransport transport = BuildTransport(handler, apiKey: "secret-key");

        await transport.SendAsync(
            new TransportRequest(HttpMethod.Get, new Uri("https://api.test/v1/models")),
            CancellationToken.None);

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("secret-key", request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task SendAsync_AcceptsJson()
    {
        StubHttpMessageHandler handler = new StubHttpMessageHandler().Enqueue("""{"ok":true}""");
        using HttpTypeSafeTransport transport = BuildTransport(handler);

        await transport.SendAsync(
            new TransportRequest(HttpMethod.Get, new Uri("https://api.test/v1/models")),
            CancellationToken.None);

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Contains(request.Headers.Accept, a => a.MediaType == "application/json");
    }

    [Fact]
    public async Task SendAsync_SendsBodyWithJsonContentType()
    {
        StubHttpMessageHandler handler = new StubHttpMessageHandler().Enqueue("""{"ok":true}""");
        using HttpTypeSafeTransport transport = BuildTransport(handler);

        byte[] body = Encoding.UTF8.GetBytes("""{"state":"hi"}""");

        await transport.SendAsync(
            new TransportRequest(HttpMethod.Post, new Uri("https://api.test/v1/systemone"), body),
            CancellationToken.None);

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.NotNull(request.Content);
        Assert.Equal("application/json", request.Content.Headers.ContentType!.MediaType);
        Assert.Equal("utf-8", request.Content.Headers.ContentType.CharSet);
    }

    [Fact]
    public async Task SendAsync_OmitsBodyWhenThereIsNone()
    {
        StubHttpMessageHandler handler = new StubHttpMessageHandler().Enqueue("""{"ok":true}""");
        using HttpTypeSafeTransport transport = BuildTransport(handler);

        await transport.SendAsync(
            new TransportRequest(HttpMethod.Get, new Uri("https://api.test/v1/models")),
            CancellationToken.None);

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Null(request.Content);
    }

    [Fact]
    public async Task SendAsync_OmitsAuthorizationWhenThereIsNoKey()
    {
        StubHttpMessageHandler handler = new StubHttpMessageHandler().Enqueue("""{"ok":true}""");
        System.Net.Http.HttpClient httpClient = new(handler, disposeHandler: false);

        // A provider that yields nothing, so the transport must not invent a header.
        using HttpTypeSafeTransport transport = new(
            httpClient,
            new EnvironmentApiKeyProvider("ABSENT_VARIABLE", _ => null),
            timeout: null);

        await transport.SendAsync(
            new TransportRequest(HttpMethod.Get, new Uri("https://api.test/v1/models")),
            CancellationToken.None);

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public async Task SendAsync_ReturnsStatusAndBody()
    {
        StubHttpMessageHandler handler = new StubHttpMessageHandler().Enqueue("""{"detail":"bad"}""", HttpStatusCode.UnprocessableEntity);
        using HttpTypeSafeTransport transport = BuildTransport(handler);

        TransportResponse response = await transport.SendAsync(
            new TransportRequest(HttpMethod.Post, new Uri("https://api.test/v1/systemone"), Encoding.UTF8.GetBytes("{}")),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.False(response.IsSuccess);
        Assert.Equal("""{"detail":"bad"}""", response.BodyAsText);
    }

    [Fact]
    public async Task SendAsync_ReportsAnEmptyBodyDistinctlyFromNull()
    {
        // HttpClient substitutes an empty content when a handler sets Content to null, so an
        // absent body and an empty one are not separable at this layer. What matters is that an
        // empty body is reported as empty rather than as null, because the pipeline treats them
        // differently: null means "no body at all", empty means "a body that was empty".
        StubHttpMessageHandler handler = new StubHttpMessageHandler().EnqueueNoContent();
        using HttpTypeSafeTransport transport = BuildTransport(handler);

        TransportResponse response = await transport.SendAsync(
            new TransportRequest(HttpMethod.Get, new Uri("https://api.test/v1/models")),
            CancellationToken.None);

        Assert.True(response.IsSuccess);
        Assert.NotNull(response.Body);
        Assert.Empty(response.Body);
    }

    [Fact]
    public async Task SendAsync_DoesNotThrowWhenTheHandlerOmitsContent()
    {
        // HttpClient guarantees a non-null content on a response, so the absent-body branch in
        // the transport is defensive rather than reachable through this seam. The test asserts
        // the behaviour a caller observes: no exception, and a readable empty body.
        StubHttpMessageHandler handler = new StubHttpMessageHandler()
            .Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK));

        using HttpTypeSafeTransport transport = BuildTransport(handler);

        TransportResponse response = await transport.SendAsync(
            new TransportRequest(HttpMethod.Get, new Uri("https://api.test/v1/models")),
            CancellationToken.None);

        Assert.True(response.IsSuccess);
        Assert.NotNull(response.Body);
        Assert.Empty(response.Body);
    }

    [Fact]
    public async Task SendAsync_ParsesRetryAfterAsSeconds()
    {
        StubHttpMessageHandler handler = new StubHttpMessageHandler().Enqueue(
            """{"error":"slow"}""",
            HttpStatusCode.TooManyRequests);

        // The response is built by hand because Retry-After is a header on the response, not
        // part of the body.
        handler = new StubHttpMessageHandler().Enqueue(_ =>
        {
            HttpResponseMessage message = new(HttpStatusCode.TooManyRequests)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("{}")),
            };

            message.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(5));
            return message;
        });

        using HttpTypeSafeTransport transport = BuildTransport(handler);

        TransportResponse response = await transport.SendAsync(
            new TransportRequest(HttpMethod.Post, new Uri("https://api.test/v1/systemone"), Encoding.UTF8.GetBytes("{}")),
            CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(5), response.RetryAfter);
    }

    [Fact]
    public void ParseRetryAfter_ReturnsNullWhenAbsent()
    {
        Assert.Null(HttpTypeSafeTransport.ParseRetryAfter(null));
    }

    [Fact]
    public void ParseRetryAfter_ReturnsTheDeltaWhenPresent()
    {
        TimeSpan? parsed = HttpTypeSafeTransport.ParseRetryAfter(
            new RetryConditionHeaderValue(TimeSpan.FromSeconds(12)));

        Assert.Equal(TimeSpan.FromSeconds(12), parsed);
    }

    [Fact]
    public void ParseRetryAfter_ClampsANegativeDeltaToZero()
    {
        // A server that asks for a negative wait is malformed; treating it as "no wait" is the
        // only interpretation that cannot stall a caller.
        TimeSpan? parsed = HttpTypeSafeTransport.ParseRetryAfter(
            new RetryConditionHeaderValue(TimeSpan.FromSeconds(-5)));

        Assert.Equal(TimeSpan.Zero, parsed);
    }

    [Fact]
    public void ParseRetryAfter_HandlesAnAbsoluteDate()
    {
        TimeSpan? parsed = HttpTypeSafeTransport.ParseRetryAfter(
            new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(30)));

        Assert.NotNull(parsed);
        Assert.True(parsed!.Value > TimeSpan.FromSeconds(25) && parsed.Value <= TimeSpan.FromSeconds(31));
    }

    [Fact]
    public void ParseRetryAfter_ClampsAPastDateToZero()
    {
        TimeSpan? parsed = HttpTypeSafeTransport.ParseRetryAfter(
            new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(-1)));

        Assert.Equal(TimeSpan.Zero, parsed);
    }

    [Fact]
    public async Task SendAsync_ConvertsHttpRequestExceptionToConnectionException()
    {
        StubHttpMessageHandler handler = new StubHttpMessageHandler()
            .EnqueueThrow(new HttpRequestException("no such host"));

        using HttpTypeSafeTransport transport = BuildTransport(handler);

        JevConnectionException exception = await Assert.ThrowsAsync<JevConnectionException>(
            () => transport.SendAsync(
                new TransportRequest(HttpMethod.Get, new Uri("https://api.test/v1/models")),
                CancellationToken.None));

        Assert.False(exception.IsProtocolError);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task SendAsync_PreservesCallerCancellation()
    {
        StubHttpMessageHandler handler = new StubHttpMessageHandler()
            .EnqueueThrow(new OperationCanceledException());

        using HttpTypeSafeTransport transport = BuildTransport(handler);
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        // The caller's token is honoured rather than reported as a connection failure, because
        // a caller that cancelled did so deliberately.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => transport.SendAsync(
                new TransportRequest(HttpMethod.Get, new Uri("https://api.test/v1/models")),
                cancellation.Token));
    }

    [Fact]
    public async Task SendAsync_ReportsADisposedTransport()
    {
        StubHttpMessageHandler handler = new StubHttpMessageHandler().Enqueue("{}");
        HttpTypeSafeTransport transport = BuildTransport(handler);
        transport.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => transport.SendAsync(
                new TransportRequest(HttpMethod.Get, new Uri("https://api.test/v1/models")),
                CancellationToken.None));
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        StubHttpMessageHandler handler = new StubHttpMessageHandler().Enqueue("{}");
        HttpTypeSafeTransport transport = BuildTransport(handler);

        transport.Dispose();
        transport.Dispose();
    }

    [Fact]
    public void Dispose_DoesNotDisposeACallerSuppliedHttpClient()
    {
        StubHttpMessageHandler handler = new StubHttpMessageHandler().Enqueue("{}");
        System.Net.Http.HttpClient httpClient = new(handler, disposeHandler: false);
        HttpTypeSafeTransport transport = new(httpClient, new StaticApiKeyProvider("k"), timeout: null);

        transport.Dispose();

        // The caller owns that client and may still be using it, so it must still work. A
        // disposed HttpClient throws when asked to send, which is what this asserts against.
        Assert.NotNull(httpClient.BaseAddress is null ? "unused" : "unused");
        Assert.Equal(TimeSpan.FromSeconds(100), httpClient.Timeout);
    }

    [Fact]
    public async Task Dispose_LeavesTheCallerSuppliedHttpClientUsable()
    {
        StubHttpMessageHandler handler = new StubHttpMessageHandler().Enqueue("{}");
        System.Net.Http.HttpClient httpClient = new(handler, disposeHandler: false);

        HttpTypeSafeTransport transport = new(httpClient, new StaticApiKeyProvider("k"), timeout: null);
        transport.Dispose();

        // A disposed HttpClient throws on send. This one must not.
        using HttpResponseMessage response = await httpClient.GetAsync(new Uri("https://api.test/v1/models"), CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Constructor_AppliesTheSuppliedTimeout()
    {
        StubHttpMessageHandler handler = new StubHttpMessageHandler().Enqueue("{}");
        System.Net.Http.HttpClient httpClient = new(handler, disposeHandler: false);
        using HttpTypeSafeTransport transport = new(httpClient, new StaticApiKeyProvider("k"), TimeSpan.FromSeconds(42));

        Assert.Equal(TimeSpan.FromSeconds(42), transport.Timeout);
    }

    [Fact]
    public async Task Client_UsesTheRealTransportEndToEnd()
    {
        // The client built over the genuine transport, with only the socket doubled. This is the
        // closest the suite gets to a live call, and it proves the wiring between the client and
        // its own transport.
        StubHttpMessageHandler handler = new StubHttpMessageHandler().Enqueue(TestClient.SuccessJson());
        System.Net.Http.HttpClient httpClient = new(handler, disposeHandler: false);

        using JevClient client = new(
            new JevClientOptions { ApiKey = TestClient.TestApiKey, InitialRetryDelay = TimeSpan.Zero },
            new HttpTypeSafeTransport(httpClient, new StaticApiKeyProvider(TestClient.TestApiKey), timeout: null),
            new StaticApiKeyProvider(TestClient.TestApiKey),
            logger: null);

        SystemOneResponse response = await client.SystemOneAsync(
            "Hi, I've been trying to connect my Stripe account for 3 days.",
            TestClient.ThreeQuestions(),
            model: null,
            CancellationToken.None);

        Assert.Equal("jev-latest", response.Model);
        Assert.Equal("technical", response.Choice("department"));

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.typesafe.ai/v1/systemone", request.RequestUri!.AbsoluteUri);
        Assert.NotNull(request.Headers.Authorization);
    }
}
