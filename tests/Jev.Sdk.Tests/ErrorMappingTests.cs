// ErrorMappingTests.cs
// Part of Jev.Sdk.Tests. Every documented error status must map to a specific, catchable type,
// because a caller's recovery depends on distinguishing "your key is wrong" from "slow down"
// from "you sent something the server rejected".

using System.Net;
using Jev.Sdk;

namespace Jev.Sdk.Tests;

public class ErrorMappingTests
{
    [Fact]
    public async Task Unauthorized_ThrowsAuthenticationException()
    {
        StubTransport transport = new StubTransport()
            .EnqueueJson("""{"detail":"invalid api key"}""", HttpStatusCode.Unauthorized);

        using JevClient client = TestClient.Create(transport);

        JevAuthenticationException exception = await Assert.ThrowsAsync<JevAuthenticationException>(
            () => client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.IsAssignableFrom<JevApiException>(exception);
        Assert.IsAssignableFrom<JevException>(exception);
    }

    [Fact]
    public async Task UnprocessableEntity_ThrowsValidationExceptionWithDetails()
    {
        StubTransport transport = new StubTransport().EnqueueJson(
            """
            {
              "detail": [
                {
                  "loc": ["body", "questions", "frustration", "score", "criteria"],
                  "msg": "Field required",
                  "type": "missing"
                }
              ]
            }
            """,
            HttpStatusCode.UnprocessableEntity);

        using JevClient client = TestClient.Create(transport);

        JevValidationException exception = await Assert.ThrowsAsync<JevValidationException>(
            () => client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None));

        ErrorDetails detail = Assert.Single(exception.Details);
        Assert.Equal("Field required", detail.Message);
        Assert.Equal("missing", detail.ErrorType);
        Assert.Equal("body.questions.frustration.score.criteria", detail.LocationPath);
    }

    [Fact]
    public async Task UnprocessableEntity_WithUnparseableBody_StillThrowsValidationException()
    {
        // A validation failure whose body does not match the documented shape is still a
        // validation failure. The raw body rides on the exception for diagnosis.
        StubTransport transport = new StubTransport()
            .EnqueueJson("not json at all", HttpStatusCode.UnprocessableEntity);

        using JevClient client = TestClient.Create(transport);

        JevValidationException exception = await Assert.ThrowsAsync<JevValidationException>(
            () => client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None));

        Assert.Empty(exception.Details);
        Assert.Equal("not json at all", exception.ResponseBody);
    }

    [Fact]
    public async Task TooManyRequests_ThrowsRateLimitException()
    {
        StubTransport transport = new StubTransport()
            .EnqueueJson("""{"error":"rate limited"}""", HttpStatusCode.TooManyRequests, retryAfter: TimeSpan.FromSeconds(3));

        using JevClient client = TestClient.Create(transport, o => o.MaxRetries = 0);

        JevRateLimitException exception = await Assert.ThrowsAsync<JevRateLimitException>(
            () => client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None));

        Assert.Equal(TimeSpan.FromSeconds(3), exception.RetryAfter);
    }

    [Fact]
    public async Task Overloaded_ThrowsOverloadedException()
    {
        StubTransport transport = new StubTransport()
            .EnqueueJson("""{"error":"overloaded"}""", (HttpStatusCode)529);

        using JevClient client = TestClient.Create(transport, o => o.MaxRetries = 0);

        JevOverloadedException exception = await Assert.ThrowsAsync<JevOverloadedException>(
            () => client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None));

        Assert.Equal(529, (int)exception.StatusCode);
    }

    [Fact]
    public async Task UnmodelledStatus_ThrowsBaseApiException()
    {
        StubTransport transport = new StubTransport()
            .EnqueueJson("""{"error":"teapot"}""", (HttpStatusCode)418);

        using JevClient client = TestClient.Create(transport, o => o.MaxRetries = 0);

        JevApiException exception = await Assert.ThrowsAsync<JevApiException>(
            () => client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None));

        Assert.Equal((HttpStatusCode)418, exception.StatusCode);
    }

    [Fact]
    public async Task MalformedSuccessBody_ThrowsProtocolErrorAndIsNotRetried()
    {
        StubTransport transport = new StubTransport().EnqueueJson("{ this is not json");

        using JevClient client = TestClient.Create(transport);

        JevConnectionException exception = await Assert.ThrowsAsync<JevConnectionException>(
            () => client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None));

        // A body that cannot be parsed will not parse on a second attempt.
        Assert.True(exception.IsProtocolError);
        Assert.Equal(1, transport.RequestCount);
    }

    [Fact]
    public async Task EmptySuccessBody_ThrowsProtocolError()
    {
        StubTransport transport = new StubTransport().EnqueueRaw(body: []);

        using JevClient client = TestClient.Create(transport);

        JevConnectionException exception = await Assert.ThrowsAsync<JevConnectionException>(
            () => client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None));

        Assert.True(exception.IsProtocolError);
    }

    [Fact]
    public async Task NullJsonSuccessBody_ThrowsProtocolError()
    {
        StubTransport transport = new StubTransport().EnqueueJson("null");

        using JevClient client = TestClient.Create(transport);

        JevConnectionException exception = await Assert.ThrowsAsync<JevConnectionException>(
            () => client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None));

        Assert.True(exception.IsProtocolError);
    }

    [Fact]
    public async Task MissingAnswer_ThrowsKeyNotFoundNamingWhatWasReturned()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        SystemOneResponse response = await client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        KeyNotFoundException exception = Assert.Throws<KeyNotFoundException>(() => response["not_a_question"]);

        // The message lists what was returned, because the usual cause is a typo in the id.
        Assert.Contains("is_urgent", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WrongAnswerKind_ThrowsInvalidCastExplainingBothKinds()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        SystemOneResponse response = await client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        InvalidCastException exception = Assert.Throws<InvalidCastException>(() => response["is_urgent"].AsChoice());

        Assert.Contains("noul", exception.Message, StringComparison.Ordinal);
        Assert.Contains("choice", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TypedReaders_ReturnAnswersDirectly()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        SystemOneResponse response = await client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        Assert.Equal(0.92, response.Noul("is_urgent"), 4);
        Assert.Equal("technical", response.Choice("department"));
        Assert.Equal(1.6, response.Score("frustration"), 4);
        Assert.Null(response.Confidence("is_urgent"));
        Assert.Equal(0.82, response.Confidence("department")!.Value, 4);
    }

    [Fact]
    public async Task CancellationDuringCall_ThrowsOperationCanceled()
    {
        StubTransport transport = new StubTransport()
            .Enqueue(_ => throw new OperationCanceledException());

        using JevClient client = TestClient.Create(transport);
        using CancellationTokenSource cancellation = new();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, cancellation.Token));
    }

    [Fact]
    public async Task PreCancelledToken_DoesNotSendARequest()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, cancellation.Token));

        Assert.Equal(0, transport.RequestCount);
    }
}
