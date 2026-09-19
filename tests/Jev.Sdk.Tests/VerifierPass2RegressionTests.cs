// VerifierPass2RegressionTests.cs
// Part of Jev.Sdk.Tests. Regressions for the three defects a second independent verification pass
// found, all confirmed by execution before being fixed.
//
//   1. MAJOR — `SystemOneResponse.Answers` became a C# null on a JSON null, so `response[id]` threw
//      NullReferenceException instead of the documented KeyNotFoundException, a null dictionary was
//      dereferenced on the success path, and re-serializing dropped the member entirely even though
//      the specification declares it required. This was the same defect class already fixed for the
//      model list, and it was missed for the answers map.
//      Resolution: the specification marks `model`, `answers` and `usage` required and non-nullable, so a
//      200 that omits or nulls one is reported as a protocol error (`JevConnectionException` with
//      IsProtocolError) rather than silently tolerated, and the never-null accessor plus the always-written
//      wire member make the class of defect unreachable from either direction.
//   2. MEDIUM — ResolveModelAsync and WarmModelsAsync documented that a fetch failure returns a
//      fallback rather than throwing, but caught only JevException. The transport is a documented
//      substitution point, so a host implementation throwing its own type escaped both.
//   3. LOW — whitespace-only instructions passed local validation for all three question kinds, so the
//      request was sent and rejected by the server, which is the round trip local validation exists
//      to prevent.

using System.Net;
using System.Text;
using System.Text.Json;
using Jev.Sdk;
using Microsoft.Extensions.Logging;

namespace Jev.Sdk.Tests;

public class VerifierPass2RegressionTests
{
    // ---------------------------------------------------------------- 1. null answers

    [Theory]
    [InlineData("""{"model":"m","answers":null,"usage":{"input_tokens":1,"output_tokens":2}}""", "answers")]
    [InlineData("""{"answers":{"a":{"type":"noul","noul":0.5}},"usage":{"input_tokens":1,"output_tokens":2}}""", "model")]
    [InlineData("""{"model":"m","answers":{"a":{"type":"noul","noul":0.5}}}""", "usage")]
    public async Task AMissingOrNullRequiredMember_IsAProtocolError(string body, string member)
    {
        // Previously a raw NullReferenceException for answers, which is not in the documented failure set.
        // The specification marks model, answers and usage required and non-nullable on this response, so a
        // 200 that omits or nulls one has not honoured the contract. Reporting a protocol error is the
        // honest outcome, and it keeps the caller inside the documented failure set.
        StubTransport transport = new StubTransport().EnqueueJson(body);

        using JevClient client = TestClient.Create(transport);

        Dictionary<string, Question> questions = new(StringComparer.Ordinal) { ["a"] = Question.Noul("is it?") };

        JevConnectionException exception = await Assert.ThrowsAsync<JevConnectionException>(
            () => client.SystemOneAsync("text", questions, model: null, CancellationToken.None));

        Assert.True(exception.IsProtocolError);
        Assert.Contains(member, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEmptyAnswersObject_IsHonouredBecauseTheMemberWasPresent()
    {
        // The complementary case, and the reason enforcement is on the member's presence rather than its
        // emptiness: the server did honour the contract, there is simply nothing to report.
        StubTransport transport = new StubTransport().EnqueueJson(
            """{"model":"m","answers":{},"usage":{"input_tokens":1,"output_tokens":2}}""");

        using JevClient client = TestClient.Create(transport);

        Dictionary<string, Question> questions = new(StringComparer.Ordinal) { ["a"] = Question.Noul("is it?") };

        SystemOneResponse response = await client.SystemOneAsync("text", questions, model: null, CancellationToken.None);

        Assert.Empty(response.AnswersOrEmpty);
    }

    [Fact]
    public void TheIndexer_ThrowsTheDocumentedException_EvenWithNullAnswers()
    {
        SystemOneResponse response = JsonSerializer.Deserialize<SystemOneResponse>(
            """{"model":"m","answers":null,"usage":{"input_tokens":1,"output_tokens":1}}""",
            JevJsonContext.Default.Options)!;

        // Documented: KeyNotFoundException. Previously NullReferenceException.
        KeyNotFoundException exception = Assert.Throws<KeyNotFoundException>(() => response["a"]);

        Assert.Contains("a", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AResponseWithNullAnswers_StillSerializesTheRequiredMember()
    {
        // The regression: WhenWritingNull dropped the member, producing a body that omits a required
        // field. A round trip must never emit invalid JSON.
        SystemOneResponse response = JsonSerializer.Deserialize<SystemOneResponse>(
            """{"model":"m","answers":null,"usage":{"input_tokens":1,"output_tokens":1}}""",
            JevJsonContext.Default.Options)!;

        string written = JsonSerializer.Serialize(response, JevJsonContext.Default.Options);

        using JsonDocument document = JsonDocument.Parse(written);
        JsonElement answers = document.RootElement.GetProperty("answers");

        Assert.Equal(JsonValueKind.Object, answers.ValueKind);
        Assert.Empty(answers.EnumerateObject());
    }

    [Fact]
    public void AnEmptyAnswersMember_RoundTripsUnchanged()
    {
        SystemOneResponse response = JsonSerializer.Deserialize<SystemOneResponse>(
            """{"model":"m","answers":{},"usage":{"input_tokens":1,"output_tokens":1}}""",
            JevJsonContext.Default.Options)!;

        string written = JsonSerializer.Serialize(response, JevJsonContext.Default.Options);

        using JsonDocument document = JsonDocument.Parse(written);
        Assert.Empty(document.RootElement.GetProperty("answers").EnumerateObject());
        Assert.Empty(response.AnswersOrEmpty);
    }

    [Fact]
    public void APopulatedAnswersMember_RoundTripsUnchanged()
    {
        SystemOneResponse response = JsonSerializer.Deserialize<SystemOneResponse>(
            """{"model":"m","answers":{"a":{"type":"noul","noul":0.5}},"usage":{"input_tokens":1,"output_tokens":1}}""",
            JevJsonContext.Default.Options)!;

        string written = JsonSerializer.Serialize(response, JevJsonContext.Default.Options);

        using JsonDocument document = JsonDocument.Parse(written);
        Assert.Equal(0.5, document.RootElement.GetProperty("answers").GetProperty("a").GetProperty("noul").GetDouble(), 4);
    }

    [Fact]
    public void TheNullableView_IsSettableByACaller()
    {
        // Public and mutable, so a caller assembling a response by hand can set it, including to null.
        SystemOneResponse response = new();

        response.Answers = new Dictionary<string, Answer>(StringComparer.Ordinal)
        {
            ["a"] = new NoulAnswer { Noul = 0.5 },
        };

        Assert.Single(response.AnswersOrEmpty);
        Assert.Equal(0.5, response["a"].AsNoul().Noul, 4);

        response.Answers = null;

        Assert.Empty(response.AnswersOrEmpty);
        Assert.Throws<KeyNotFoundException>(() => response["a"]);
    }

    [Fact]
    public void TheWireView_IsSettableAndAlwaysEmitsTheMember()
    {
        // Setting through the wire view must be equivalent to setting through the nullable view, and the
        // member must be present either way.
        SystemOneResponse response = new() { AnswersForWire = null! };

        string written = JsonSerializer.Serialize(response, JevJsonContext.Default.Options);

        using JsonDocument document = JsonDocument.Parse(written);
        Assert.Equal(JsonValueKind.Object, document.RootElement.GetProperty("answers").ValueKind);
    }

    [Fact]
    public void NullAndEmptyAnswers_AreDistinguishableThroughTheNullableView()
    {
        // A caller can still tell a null apart from an empty result, which is why the raw view stays
        // nullable even though reads go through the never-null accessor.
        SystemOneResponse nulled = JsonSerializer.Deserialize<SystemOneResponse>(
            """{"model":"m","answers":null,"usage":{"input_tokens":1,"output_tokens":1}}""",
            JevJsonContext.Default.Options)!;

        SystemOneResponse empty = JsonSerializer.Deserialize<SystemOneResponse>(
            """{"model":"m","answers":{},"usage":{"input_tokens":1,"output_tokens":1}}""",
            JevJsonContext.Default.Options)!;

        Assert.Null(nulled.Answers);
        Assert.NotNull(empty.Answers);
        Assert.Empty(nulled.AnswersOrEmpty);
        Assert.Empty(empty.AnswersOrEmpty);
    }

    [Fact]
    public async Task Telemetry_ReportsTheAnswerCountWithoutFaulting()
    {
        // The pipeline reads the answer count for a log message, which was the null dereference. The
        // member is now guaranteed non-null by the protocol check, so the log statement cannot fault.
        RecordingLoggerProvider recorder = new();
        ILoggerFactory factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(recorder));

        StubTransport transport = new StubTransport().EnqueueJson(
            """{"model":"m","answers":{"a":{"type":"noul","noul":0.5}},"usage":{"input_tokens":3,"output_tokens":1}}""");

        using JevClient client = TestClient.Create(transport, logger: factory.CreateLogger("test"));

        Dictionary<string, Question> questions = new(StringComparer.Ordinal) { ["a"] = Question.Noul("is it?") };

        await client.SystemOneAsync("text", questions, model: null, CancellationToken.None);

        Assert.Contains(recorder.Messages, message => message.Contains("1 answer", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- 2. documented fallback

    [Fact]
    public async Task ResolveModelAsync_AbsorbsANonJevExceptionFromASubstitutedTransport()
    {
        // The regression: only JevException was caught, so a host transport throwing its own type
        // escaped a method documented to fall back rather than throw.
        using JevClient client = new(
            new JevClientOptions { ApiKey = "k", MaxRetries = 0 },
            new ThrowingTransport(),
            new StaticApiKeyProvider("k"),
            logger: null);

        string model = await client.ResolveModelAsync(preferred: null, CancellationToken.None);

        Assert.Equal("jev-latest", model);
    }

    [Fact]
    public async Task WarmModelsAsync_ReturnsFalseForANonJevExceptionFromASubstitutedTransport()
    {
        using JevClient client = new(
            new JevClientOptions { ApiKey = "k", MaxRetries = 0 },
            new ThrowingTransport(),
            new StaticApiKeyProvider("k"),
            logger: null);

        bool warmed = await client.WarmModelsAsync(CancellationToken.None);

        Assert.False(warmed);
    }

    [Fact]
    public async Task TheFallback_StillHonoursCancellation()
    {
        // Widening the catch must not swallow cancellation: a caller that cancelled wants to know, and
        // the documentation says so explicitly.
        using JevClient client = new(
            new JevClientOptions { ApiKey = "k", MaxRetries = 0 },
            new ThrowingTransport(),
            new StaticApiKeyProvider("k"),
            logger: null);

        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.WarmModelsAsync(cancellation.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.ResolveModelAsync(preferred: null, cancellation.Token));
    }

    [Fact]
    public async Task GetModelsAsync_StillPropagatesNonJevExceptions()
    {
        // The opposite contract: this method documents the failure set, so it must not swallow.
        using JevClient client = new(
            new JevClientOptions { ApiKey = "k", MaxRetries = 0 },
            new ThrowingTransport(),
            new StaticApiKeyProvider("k"),
            logger: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetModelsAsync(CancellationToken.None));
    }

    // ---------------------------------------------------------------- 3. blank instructions

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public async Task WhitespaceOnlyInstructions_AreRejectedLocallyForEveryKind(string blank)
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        (string Kind, Question Question)[] cases =
        [
            ("noul", Question.Noul(blank)),
            ("choice", Question.Choice(blank, new Dictionary<string, string?>(StringComparer.Ordinal) { ["a"] = null })),
            ("score", Question.Score(blank, "low", "high")),
        ];

        foreach ((string kind, Question question) in cases)
        {
            Dictionary<string, Question> questions = new(StringComparer.Ordinal) { ["q"] = question };

            JevRequestValidationException exception = await Assert.ThrowsAsync<JevRequestValidationException>(
                () => client.SystemOneAsync("text", questions, model: null, CancellationToken.None));

            Assert.Contains(exception.Problems, p => p.Contains("needs instructions", StringComparison.Ordinal));
            Assert.False(string.IsNullOrEmpty(kind));
        }

        // Nothing reached the wire: local validation is what prevents the server round trip.
        Assert.Equal(0, transport.RequestCount);
    }

    [Fact]
    public async Task StructuredInstructions_AreNeverTreatedAsBlank()
    {
        // An empty JSON object is content even though its text is "{}" and its string view is null. The
        // blank check must look at the string view only, or a structured instruction would be rejected.
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        StructuredValue[] shapes =
        [
            StructuredValue.FromJson(JsonDocument.Parse("{}").RootElement),
            StructuredValue.FromJson(JsonDocument.Parse("[]").RootElement),
            StructuredValue.FromJson(JsonDocument.Parse("""{"a":1}""").RootElement),
        ];

        foreach (StructuredValue shape in shapes)
        {
            Dictionary<string, Question> questions = new(StringComparer.Ordinal)
            {
                ["q"] = Question.Noul(shape),
            };

            await client.SystemOneAsync("text", questions, model: null, CancellationToken.None);
        }

        Assert.Equal(3, transport.RequestCount);
    }

    [Fact]
    public async Task AWhitespaceInstructionOnOneQuestion_DoesNotMaskAnothersProblem()
    {
        // Every problem is still reported at once, which is the file's stated contract.
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["blank"] = Question.Noul("   "),
            ["no_options"] = new ChoiceQuestion { Instructions = StructuredValue.FromString("pick") },
            ["one_level"] = Question.Score("rate", "only"),
        };

        JevRequestValidationException exception = await Assert.ThrowsAsync<JevRequestValidationException>(
            () => client.SystemOneAsync("text", questions, model: null, CancellationToken.None));

        Assert.True(exception.Problems.Count >= 3);
        Assert.Contains("blank", exception.Message, StringComparison.Ordinal);
        Assert.Contains("no_options", exception.Message, StringComparison.Ordinal);
        Assert.Contains("one_level", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>A transport that throws a non-Jev exception, as a host implementation might.</summary>
    private sealed class ThrowingTransport : ITypeSafeTransport
    {
        public Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken)
        {
            // A caller cancellation must still surface, so honour the token before throwing.
            cancellationToken.ThrowIfCancellationRequested();

            throw new InvalidOperationException("boom: transport seam failed with a non-Jev exception");
        }
    }
}
