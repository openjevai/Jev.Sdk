// VendorAlignmentTests.cs
// Part of Jev.Sdk.Tests. Asserts that this client's throttling and restraint behaviour matches the
// vendor's documented guidance, because a caller running the Python or JavaScript SDK against the
// same account should not get different behaviour from the .NET one.
//
// Sources, all fetched from docs.typesafe.ai:
//   sdk/python/api/retries       RetryPolicy defaults
//   sdk/python/api/constants     environment variable names and defaults
//   sdk/javascript/api/interfaces/RetryPolicy
//   sdk/python/api/exceptions    the exception surface, including request ids
//
// Where this library deliberately differs, the difference is asserted here so it cannot drift
// silently.

using System.Net;
using Jev.Sdk;

namespace Jev.Sdk.Tests;

public class VendorAlignmentTests
{
    // ---------------------------------------------------------------- retry defaults

    [Fact]
    public void RetryDefaults_MatchTheVendorSdks()
    {
        // vendor: max_retries=2, backoff_initial=0.5, backoff_max=5.0, backoff_jitter=0.25,
        //         timeout=30.0 per attempt in the policy but 10.0 as the client default
        JevClientOptions options = new();

        Assert.Equal(2, options.MaxRetries);
        Assert.Equal(500, options.InitialRetryDelay.TotalMilliseconds);
        Assert.Equal(5.0, options.MaxRetryDelay.TotalSeconds);
        Assert.Equal(0.25, options.RetryJitterFraction);
        Assert.Equal(10.0, options.Timeout.TotalSeconds);
    }

    [Fact]
    public void MaxRetryAfter_MatchesTheVendorCeiling()
    {
        // vendor: maxRetryAfterMs default 60000, with longer server delays falling back to backoff.
        JevClientOptions options = new();

        Assert.Equal(60_000, options.MaxRetryAfter.TotalMilliseconds);
    }

    [Fact]
    public void RetryableStatuses_MatchTheVendorSet()
    {
        // vendor: 408, 429, and 500-599. The 5xx range covers TypeSafe's own 529.
        int[] retryable = [408, 429, 500, 501, 502, 503, 504, 529, 599];
        int[] notRetryable = [400, 401, 403, 404, 418, 422];

        foreach (int code in retryable)
        {
            Assert.True(
                HttpTypeSafeTransport.IsRetryableStatus((HttpStatusCode)code),
                $"{code} should be retryable");
        }

        foreach (int code in notRetryable)
        {
            Assert.False(
                HttpTypeSafeTransport.IsRetryableStatus((HttpStatusCode)code),
                $"{code} should not be retryable");
        }
    }

    [Fact]
    public void Jitter_IsSubtractiveNotFullJitter()
    {
        // The vendor subtracts up to a fraction of the delay. "Full jitter", which replaces the
        // delay with a uniform random value from zero, would shorten waits far more and is not what
        // the SDKs do. A delay of 1 second with jitter 0.25 must land in [0.75, 1.0], never below.
        JevClientOptions options = new()
        {
            InitialRetryDelay = TimeSpan.FromSeconds(1),
            RetryBackoffMultiplier = 2,
            MaxRetryDelay = TimeSpan.FromSeconds(5),
            RetryJitterFraction = 0.25,
        };

        for (int i = 0; i <= 10; i++)
        {
            double sample = i / 10.0;
            TimeSpan delay = HttpTypeSafeTransport.ComputeRetryDelay(0, null, options, () => sample);

            Assert.True(
                delay >= TimeSpan.FromMilliseconds(750) && delay <= TimeSpan.FromSeconds(1),
                $"sample {sample} produced {delay}, outside [0.75s, 1s]");
        }
    }

    [Fact]
    public void Backoff_DoublesUpToTheCeiling()
    {
        // vendor: backoff_initial doubled each attempt, capped at backoff_max.
        JevClientOptions options = new()
        {
            InitialRetryDelay = TimeSpan.FromMilliseconds(500),
            RetryBackoffMultiplier = 2,
            MaxRetryDelay = TimeSpan.FromSeconds(5),
        };

        Assert.Equal(500, HttpTypeSafeTransport.ComputeRetryDelay(0, null, options, null).TotalMilliseconds);
        Assert.Equal(1000, HttpTypeSafeTransport.ComputeRetryDelay(1, null, options, null).TotalMilliseconds);
        Assert.Equal(2000, HttpTypeSafeTransport.ComputeRetryDelay(2, null, options, null).TotalMilliseconds);
        Assert.Equal(4000, HttpTypeSafeTransport.ComputeRetryDelay(3, null, options, null).TotalMilliseconds);

        // 8000 would exceed the 5s ceiling, so it is capped.
        Assert.Equal(5000, HttpTypeSafeTransport.ComputeRetryDelay(4, null, options, null).TotalMilliseconds);
    }

    // ---------------------------------------------------------------- environment variables

    [Fact]
    public void EnvironmentVariableNames_MatchTheVendorSdks()
    {
        // vendor: TYPESAFE_API_KEY, TYPESAFE_BASE_URL, TYPESAFE_DEFAULT_MODEL, TYPESAFE_LOG_LEVEL
        Assert.Equal("TYPESAFE_API_KEY", JevEnvironment.ApiKeyVariable);
        Assert.Equal("TYPESAFE_BASE_URL", JevEnvironment.BaseUrlVariable);
        Assert.Equal("TYPESAFE_DEFAULT_MODEL", JevEnvironment.DefaultModelVariable);
    }

    [Fact]
    public void BaseUrlVariable_OverridesTheDefaultBaseAddress()
    {
        JevClientOptions options = EnvironmentOptionsProvider.Apply(
            new JevClientOptions(),
            name => name == JevEnvironment.BaseUrlVariable ? "https://staging.example.test/v1/" : null);

        Assert.Equal(new Uri("https://staging.example.test/v1/"), options.BaseAddress);
    }

    [Fact]
    public void DefaultModelVariable_OverridesTheDefaultModel()
    {
        JevClientOptions options = EnvironmentOptionsProvider.Apply(
            new JevClientOptions(),
            name => name == JevEnvironment.DefaultModelVariable ? "jev-canary" : null);

        Assert.Equal("jev-canary", options.DefaultModel);
    }

    [Fact]
    public void EnvironmentDoesNotOverrideAnExplicitChoice()
    {
        // The environment supplies defaults. A caller who set a base address in code keeps it.
        JevClientOptions options = EnvironmentOptionsProvider.Apply(
            new JevClientOptions { BaseAddress = new Uri("https://explicit.example.test/v1/") },
            name => name == JevEnvironment.BaseUrlVariable ? "https://staging.example.test/v1/" : null);

        Assert.Equal(new Uri("https://explicit.example.test/v1/"), options.BaseAddress);
    }

    [Fact]
    public void AnUnparseableBaseUrlVariableIsIgnored()
    {
        // Construction-time option validation reports a bad value with a better message than this
        // method could, so an unparseable variable is skipped rather than thrown from here.
        JevClientOptions options = EnvironmentOptionsProvider.Apply(
            new JevClientOptions(),
            name => name == JevEnvironment.BaseUrlVariable ? "not-a-uri" : null);

        Assert.Equal(JevClientOptions.DefaultBaseAddress, options.BaseAddress);
    }

    // ---------------------------------------------------------------- request id

    [Fact]
    public async Task SuccessfulCall_ExposesTheServerRequestId()
    {
        // vendor: results carry request_id from the x-typesafe-request-id response header.
        StubHttpMessageHandler handler = new StubHttpMessageHandler().Enqueue(_ =>
        {
            HttpResponseMessage message = new(HttpStatusCode.OK)
            {
                Content = new StringContent(TestClient.SuccessJson(), System.Text.Encoding.UTF8, "application/json"),
            };

            message.Headers.TryAddWithoutValidation(JevRequestId.HeaderName, "req_success_123");
            return message;
        });

        System.Net.Http.HttpClient httpClient = new(handler, disposeHandler: false);

        using JevClient client = new(
            new JevClientOptions { ApiKey = TestClient.TestApiKey, InitialRetryDelay = TimeSpan.Zero, MaxRetryDelay = TimeSpan.Zero },
            new HttpTypeSafeTransport(httpClient, new StaticApiKeyProvider(TestClient.TestApiKey), timeout: null),
            new StaticApiKeyProvider(TestClient.TestApiKey),
            logger: null);

        SystemOneResponse response = await client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        Assert.Equal("req_success_123", response.RequestId);
    }

    [Fact]
    public async Task FailedCall_CarriesTheServerRequestIdOnTheException()
    {
        // vendor: errors carry request_id too, which is the handle support asks for.
        StubHttpMessageHandler handler = new StubHttpMessageHandler().Enqueue(_ =>
        {
            HttpResponseMessage message = new(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("""{"error":"bad key"}""", System.Text.Encoding.UTF8, "application/json"),
            };

            message.Headers.TryAddWithoutValidation(JevRequestId.HeaderName, "req_failure_456");
            return message;
        });

        System.Net.Http.HttpClient httpClient = new(handler, disposeHandler: false);

        using JevClient client = new(
            new JevClientOptions { ApiKey = TestClient.TestApiKey, InitialRetryDelay = TimeSpan.Zero, MaxRetryDelay = TimeSpan.Zero },
            new HttpTypeSafeTransport(httpClient, new StaticApiKeyProvider(TestClient.TestApiKey), timeout: null),
            new StaticApiKeyProvider(TestClient.TestApiKey),
            logger: null);

        JevAuthenticationException exception = await Assert.ThrowsAsync<JevAuthenticationException>(
            () => client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None));

        Assert.Equal("req_failure_456", exception.RequestId);
        Assert.Equal("POST systemone", exception.Endpoint);
    }

    [Fact]
    public void RequestId_IsNullWhenTheHeaderIsAbsent()
    {
        Assert.Null(JevRequestId.FromHeaders(null));
    }

    // ---------------------------------------------------------------- exception surface

    [Fact]
    public void ExceptionTypes_CoverTheVendorSurface()
    {
        // vendor: 400, 401, 403, 404, 422, 429, 5xx, plus connection and timeout. Each maps to a
        // distinct catchable type so a caller's recovery does not depend on parsing a message.
        Assert.IsAssignableFrom<JevException>(new JevBadRequestException("b"));
        Assert.IsAssignableFrom<JevException>(new JevAuthenticationException("a"));
        Assert.IsAssignableFrom<JevException>(new JevPermissionDeniedException("p"));
        Assert.IsAssignableFrom<JevException>(new JevNotFoundException("n"));
        Assert.IsAssignableFrom<JevException>(new JevValidationException("v", []));
        Assert.IsAssignableFrom<JevException>(new JevRateLimitException("r"));
        Assert.IsAssignableFrom<JevException>(new JevServerException(HttpStatusCode.BadGateway, "s"));
        Assert.IsAssignableFrom<JevException>(new JevOverloadedException("o"));
        Assert.IsAssignableFrom<JevException>(new JevConnectionException("c"));
    }

    [Fact]
    public void OverloadedException_IsAServerException()
    {
        // A caller catching every server-side condition gets the 529 as well, while a caller who
        // cares about the distinction has the narrower type.
        JevOverloadedException overloaded = new("busy");

        Assert.IsAssignableFrom<JevServerException>(overloaded);
        Assert.Equal(529, (int)overloaded.StatusCode);
    }

    [Fact]
    public void RateLimitException_IsNotAServerException()
    {
        // A rate limit is the server declining to serve, not failing. Collapsing the two would make
        // a caller report an outage when it should slow down.
        JevRateLimitException rateLimited = new("slow down");

        Assert.IsNotAssignableFrom<JevServerException>(rateLimited);
    }

    [Fact]
    public async Task ServerError_OtherThanOverload_BecomesAServerException()
    {
        StubTransport transport = new StubTransport()
            .EnqueueJson("""{"error":"gateway"}""", HttpStatusCode.BadGateway);

        using JevClient client = TestClient.Create(transport, o => o.MaxRetries = 0);

        JevServerException exception = await Assert.ThrowsAsync<JevServerException>(
            () => client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
        Assert.Null(exception.RequestId);
    }

    [Fact]
    public async Task ClientErrorStatuses_MapToTheirOwnTypes()
    {
        (HttpStatusCode Status, Type Expected)[] cases =
        [
            (HttpStatusCode.BadRequest, typeof(JevBadRequestException)),
            (HttpStatusCode.Unauthorized, typeof(JevAuthenticationException)),
            (HttpStatusCode.Forbidden, typeof(JevPermissionDeniedException)),
            (HttpStatusCode.NotFound, typeof(JevNotFoundException)),
            (HttpStatusCode.UnprocessableEntity, typeof(JevValidationException)),
        ];

        foreach ((HttpStatusCode status, Type expected) in cases)
        {
            StubTransport transport = new StubTransport()
                .EnqueueJson("""{"error":"client side"}""", status);

            using JevClient client = TestClient.Create(transport);

            JevApiException exception = await Assert.ThrowsAnyAsync<JevApiException>(
                () => client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None));

            Assert.IsType(expected, exception);
            Assert.Equal(status, exception.StatusCode);
        }
    }

    [Fact]
    public async Task RequestId_IsAttachedToTheSpanAndSafeToRecord()
    {
        using ActivityCollector collector = new();

        StubHttpMessageHandler handler = new StubHttpMessageHandler().Enqueue(_ =>
        {
            HttpResponseMessage message = new(HttpStatusCode.OK)
            {
                Content = new StringContent(TestClient.SuccessJson(), System.Text.Encoding.UTF8, "application/json"),
            };

            message.Headers.TryAddWithoutValidation(JevRequestId.HeaderName, "req_span_789");
            return message;
        });

        System.Net.Http.HttpClient httpClient = new(handler, disposeHandler: false);

        using JevClient client = new(
            new JevClientOptions { ApiKey = TestClient.TestApiKey, InitialRetryDelay = TimeSpan.Zero },
            new HttpTypeSafeTransport(httpClient, new StaticApiKeyProvider(TestClient.TestApiKey), timeout: null),
            new StaticApiKeyProvider(TestClient.TestApiKey),
            logger: null);

        await client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        System.Diagnostics.Activity span = Assert.Single(
            collector.Activities,
            a => a.OperationName == JevTelemetry.SystemOneActivityName);

        Assert.Equal("req_span_789", span.GetTagItem(JevTelemetry.RequestIdTag)?.ToString());
    }
}
