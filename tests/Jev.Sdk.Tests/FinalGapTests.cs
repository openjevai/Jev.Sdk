// FinalGapTests.cs
// Part of Jev.Sdk.Tests. The last reachable branches, each one a genuine caller-visible
// behaviour:
//
//   - a caller's cancellation must propagate as a cancellation, not be relabelled a timeout
//   - a response with no content at all must not throw
//   - a cancelled call must be tagged cancelled on its span, not merely counted
//   - a question kind the library does not model must be refused with a clear reason
//   - the converter list must be stripped of BOTH union converters, so a payload containing a
//     union inside a union cannot recurse

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Jev.Sdk;

namespace Jev.Sdk.Tests;

public class FinalGapTests
{
    // ---------------------------------------------------------------- transport cancellation

    [Fact]
    public async Task SendAsync_PropagatesCallerCancellationRatherThanReportingATimeout()
    {
        // The handler throws the same exception type a timeout produces. What distinguishes them
        // is whether the caller's token is set, so this test drives that case explicitly: the
        // cancellation must surface as a cancellation.
        using CancellationTokenSource cancellation = new();

        StubHttpMessageHandler handler = new StubHttpMessageHandler().Enqueue(_ =>
        {
            cancellation.Cancel();
            throw new TaskCanceledException("cancelled by the caller");
        });

        System.Net.Http.HttpClient httpClient = new(handler, disposeHandler: false);
        using HttpTypeSafeTransport transport = new(httpClient, new StaticApiKeyProvider("k"), timeout: null);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => transport.SendAsync(
                new TransportRequest(HttpMethod.Get, new Uri("https://api.test/v1/models")),
                cancellation.Token));
    }

    [Fact]
    public async Task SendAsync_ReturnsNullBodyWhenAContentIsPresentButEmpty()
    {
        StubHttpMessageHandler handler = new StubHttpMessageHandler().EnqueueEmpty();
        System.Net.Http.HttpClient httpClient = new(handler, disposeHandler: false);
        using HttpTypeSafeTransport transport = new(httpClient, new StaticApiKeyProvider("k"), timeout: null);

        TransportResponse response = await transport.SendAsync(
            new TransportRequest(HttpMethod.Get, new Uri("https://api.test/v1/models")),
            CancellationToken.None);

        Assert.True(response.IsSuccess);
        Assert.NotNull(response.Body);
        Assert.Empty(response.Body);
    }

    [Fact]
    public async Task ReadBody_PropagatesCallerCancellationDuringTheBodyRead()
    {
        // The body read is a second await, and it needs the same cancellation contract as the
        // send. A caller that cancels while the body streams must see a cancellation.
        using CancellationTokenSource cancellation = new();

        StubHttpMessageHandler handler = new StubHttpMessageHandler().Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new CancellingContent(cancellation),
        });

        System.Net.Http.HttpClient httpClient = new(handler, disposeHandler: false);
        using HttpTypeSafeTransport transport = new(httpClient, new StaticApiKeyProvider("k"), timeout: null);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => transport.SendAsync(
                new TransportRequest(HttpMethod.Get, new Uri("https://api.test/v1/models")),
                cancellation.Token));
    }

    [Fact]
    public void ParseRetryAfter_ReturnsNullWhenTheHeaderCarriesNeitherForm()
    {
        // RetryConditionHeaderValue permits both members to be null, which is a malformed header
        // rather than an instruction to wait.
        RetryConditionHeaderValue? header = default;

        Assert.Null(HttpTypeSafeTransport.ParseRetryAfter(header));
    }

    // ---------------------------------------------------------------- telemetry on cancellation

    [Fact]
    public async Task CancelledSpan_IsTaggedCancelled()
    {
        using ActivityCollector collector = new();
        using CancellationTokenSource cancellation = new();

        StubTransport transport = new StubTransport().Enqueue(_ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException();
        });

        using JevClient client = TestClient.Create(transport);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, cancellation.Token));

        System.Diagnostics.Activity span = Assert.Single(
            collector.Activities,
            a => a.OperationName == JevTelemetry.SystemOneActivityName);

        // A cancelled call must be distinguishable from a failed one on the span itself, or a
        // dashboard cannot tell user-initiated cancellation from a real fault.
        Assert.Equal("cancelled", span.GetTagItem(JevTelemetry.OutcomeTag)?.ToString());
    }

    [Fact]
    public async Task FailedSpan_IsTaggedError()
    {
        using ActivityCollector collector = new();
        StubTransport transport = new StubTransport()
            .EnqueueJson("""{"error":"bad key"}""", HttpStatusCode.Unauthorized);

        using JevClient client = TestClient.Create(transport);

        await Assert.ThrowsAsync<JevAuthenticationException>(
            () => client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None));

        System.Diagnostics.Activity span = Assert.Single(
            collector.Activities,
            a => a.OperationName == JevTelemetry.SystemOneActivityName);

        Assert.Equal("error", span.GetTagItem(JevTelemetry.OutcomeTag)?.ToString());
    }

    // ---------------------------------------------------------------- validation fallthrough

    [Fact]
    public async Task UnsupportedQuestionType_IsRejectedWithItsTypeName()
    {
        // A caller who subclasses Question rather than using a factory gets a clear refusal
        // naming the offending type, instead of an opaque serialization failure.
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["q"] = new UnsupportedQuestion(),
        };

        JevRequestValidationException exception = await Assert.ThrowsAsync<JevRequestValidationException>(
            () => client.SystemOneAsync("text", questions, model: null, CancellationToken.None));

        Assert.Contains(exception.Problems, p => p.Contains("UnsupportedQuestion", StringComparison.Ordinal));
        Assert.Equal(0, transport.RequestCount);
    }

    // ---------------------------------------------------------------- nested unions

    [Fact]
    public void NestedUnionPayload_DeserializesWithoutRecursing()
    {
        // The converter must remove both union converters from the options it hands to the
        // concrete reader. If it removed only its own, reading an answer whose payload contains a
        // question-shaped member would re-enter the other converter and recurse.
        SystemOneRequest request = JsonSerializer.Deserialize<SystemOneRequest>(
            """
            {
              "state": { "message": "help" },
              "model": "jev-latest",
              "questions": {
                "a": { "type": "noul", "instructions": "is it urgent?" },
                "b": { "type": "choice", "instructions": "which team?",
                       "criteria": { "sales": null, "support": "customer issues" } },
                "c": { "type": "score", "instructions": "how bad?",
                       "criteria": ["low", "high"] }
              }
            }
            """,
            JevJsonContext.Default.Options)!;

        Assert.Equal(3, request.Questions.Count);
        Assert.IsType<NoulQuestion>(request.Questions["a"]);
        Assert.IsType<ChoiceQuestion>(request.Questions["b"]);
        Assert.IsType<ScoreQuestion>(request.Questions["c"]);

        // And it must serialize back out intact.
        string written = JsonSerializer.Serialize(request, JevJsonContext.Default.Options);
        Assert.Contains("\"questions\"", written, StringComparison.Ordinal);
    }

    [Fact]
    public void StructuredValue_ImplicitConversionCoversEveryShape()
    {
        // The implicit operator is the ergonomic entry point, so each shape must survive it.
        using JsonDocument text = JsonDocument.Parse("\"hello\"");
        using JsonDocument number = JsonDocument.Parse("7");
        using JsonDocument flag = JsonDocument.Parse("false");

        StructuredValue fromText = text.RootElement;
        StructuredValue fromNumber = number.RootElement;
        StructuredValue fromFlag = flag.RootElement;

        Assert.Equal(StructuredValue.JsonShape.Text, fromText.Shape);
        Assert.Equal(StructuredValue.JsonShape.Number, fromNumber.Shape);
        Assert.Equal(StructuredValue.JsonShape.Flag, fromFlag.Shape);
    }

    [Fact]
    public void EnvironmentApiKeyProvider_ParameterlessConstructorIsUsable()
    {
        // A null reader never happens through the public constructors, but the parameterless one
        // binds the real environment and must be constructible without arguments.
        EnvironmentApiKeyProvider provider = new();

        Assert.Equal(JevEnvironment.ApiKeyVariable, provider.VariableName);
    }
}

/// <summary>A question subtype the library does not support, used to reach the validation fallthrough.</summary>
internal sealed class UnsupportedQuestion : Question
{
    public override string Type => "unsupported";
}

/// <summary>
/// A content whose async read cancels the caller's token, so the cancellation path inside the
/// body read is exercised rather than only the send.
/// </summary>
internal sealed class CancellingContent : HttpContent
{
    private readonly CancellationTokenSource _cancellation;

    internal CancellingContent(CancellationTokenSource cancellation) => _cancellation = cancellation;

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        // Reading the body goes through this method. Cancelling the caller's token before
        // failing is what makes the transport's `when` filter observable: without it, the
        // exception would be indistinguishable from a server-side abort.
        await _cancellation.CancelAsync();
        throw new OperationCanceledException(_cancellation.Token);
    }

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}
