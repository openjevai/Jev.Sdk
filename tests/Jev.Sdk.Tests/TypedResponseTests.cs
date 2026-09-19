// TypedResponseTests.cs
// Part of Jev.Sdk.Tests. The response shape in this API is partly caller-determined, so the caller
// may want to bind it to their own concrete type rather than to the library's dictionary.
//
// The vendor's Python SDK exposes this as `response_model=` and its TypeScript SDK does it with
// mapped types. Mapped types cannot be expressed in C# — a type's members cannot be derived from a
// type argument's members without structural typing — so the C#-expressible form is a generic
// overload taking caller-supplied type information, which is the analogue of the Python form.
//
// These tests prove the overloads work, that they share one code path with the untyped form, and
// that the caller's model keeps the forward-compatibility surface.

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Jev.Sdk;

namespace Jev.Sdk.Tests;

public class TypedResponseTests
{
    // ---------------------------------------------------------------- the overloads

    [Fact]
    public async Task TextState_BindsToTheCallersType()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        VerdictResponse verdict = await client.SystemOneAsync(
            "text",
            TestClient.ThreeQuestions(),
            TypedResponseContext.Default.VerdictResponse,
            model: null,
            CancellationToken.None);

        Assert.Equal("jev-latest", verdict.Model);
        Assert.Equal(3, verdict.Answers.Count);
        Assert.Equal("technical", verdict.Answers["department"].AsChoice().Choice);
        Assert.Equal(360, verdict.Usage!.TotalTokens);
    }

    [Fact]
    public async Task StructuredState_BindsToTheCallersType()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        VerdictResponse verdict = await client.SystemOneAsync(
            StructuredValue.FromString("text"),
            TestClient.ThreeQuestions(),
            TypedResponseContext.Default.VerdictResponse,
            model: null,
            CancellationToken.None);

        Assert.Equal(3, verdict.Answers.Count);
    }

    [Fact]
    public async Task CallerOwnedStateAndType_BindTogether()
    {
        // Both generic parameters supplied at once, which is the most specific form.
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        CallerState state = new() { Subject = "Duplicate charge", Message = "Please help." };

        VerdictResponse verdict = await client.SystemOneAsync(
            state,
            CallerStateContext.Default.CallerState,
            TestClient.ThreeQuestions(),
            TypedResponseContext.Default.VerdictResponse,
            model: null,
            CancellationToken.None);

        Assert.Equal("jev-latest", verdict.Model);

        string payload = System.Text.Encoding.UTF8.GetString(transport.LastRequest!.Body!.Value.Span);
        using JsonDocument document = JsonDocument.Parse(payload);
        Assert.Equal("Duplicate charge", document.RootElement.GetProperty("state").GetProperty("subject").GetString());
    }

    [Fact]
    public async Task ANarrowerType_IgnoresUndeclaredMembers()
    {
        // Binding to a narrow type must be safe: fields the caller did not declare are simply not
        // read, rather than causing a failure.
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        AnswersOnly answers = await client.SystemOneAsync(
            "text",
            TestClient.ThreeQuestions(),
            TypedResponseContext.Default.AnswersOnly,
            model: null,
            CancellationToken.None);

        Assert.Equal(3, answers.Answers.Count);
    }

    [Fact]
    public async Task ExtensionData_OnTheCallersType_PreservesUnmodelledResponseFields()
    {
        // The unmodelled-response pass-through is not lost by binding to a concrete type: a caller who
        // wants it declares extension data on their own model.
        StubTransport transport = new StubTransport().EnqueueJson(
            """
            {
              "model": "jev-latest",
              "answers": { "a": { "type": "noul", "noul": 0.5 } },
              "usage": { "input_tokens": 1, "output_tokens": 1 },
              "request_metadata": { "region": "us-west" }
            }
            """);

        using JevClient client = TestClient.Create(transport);

        Dictionary<string, Question> questions = new(StringComparer.Ordinal) { ["a"] = Question.Noul("is it?") };

        VerdictResponse verdict = await client.SystemOneAsync(
            "text",
            questions,
            TypedResponseContext.Default.VerdictResponse,
            model: null,
            CancellationToken.None);

        Assert.NotNull(verdict.Extra);
        Assert.True(verdict.Extra!.ContainsKey("request_metadata"));
    }

    [Fact]
    public async Task AnUnmodelledAnswerKind_StillFallsBackInsideTheCallersType()
    {
        // The forward-compatibility guarantee applies to the answer union wherever it appears, so
        // binding to a caller's type does not weaken it.
        StubTransport transport = new StubTransport().EnqueueJson(
            """
            {
              "model": "m",
              "answers": {
                "a": { "type": "noul", "noul": 0.5 },
                "z": { "type": "brand_new_kind", "payload": [1, 2] }
              },
              "usage": { "input_tokens": 1, "output_tokens": 1 }
            }
            """);

        using JevClient client = TestClient.Create(transport);

        Dictionary<string, Question> questions = new(StringComparer.Ordinal) { ["a"] = Question.Noul("is it?") };

        VerdictResponse verdict = await client.SystemOneAsync(
            "text",
            questions,
            TypedResponseContext.Default.VerdictResponse,
            model: null,
            CancellationToken.None);

        Assert.IsType<NoulAnswer>(verdict.Answers["a"]);

        UnknownAnswer unknown = Assert.IsType<UnknownAnswer>(verdict.Answers["z"]);
        Assert.Equal("brand_new_kind", unknown.Type);
        Assert.Equal(2, unknown.RawJson.GetProperty("payload").GetArrayLength());
    }

    [Fact]
    public async Task TheTypedPath_ReceivesTheRequestId()
    {
        // The request id is attached to JevResponse by the pipeline. A caller's own model does not
        // inherit that, so on this path they read the header themselves if they want it. This asserts
        // the current, documented behaviour rather than implying otherwise.
        StubHttpMessageHandler handler = new StubHttpMessageHandler().Enqueue(_ =>
        {
            HttpResponseMessage message = new(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(TestClient.SuccessJson(), System.Text.Encoding.UTF8, "application/json"),
            };

            message.Headers.TryAddWithoutValidation(JevRequestId.HeaderName, "req_typed_1");
            return message;
        });

        System.Net.Http.HttpClient httpClient = new(handler, disposeHandler: false);

        using JevClient client = new(
            new JevClientOptions { ApiKey = TestClient.TestApiKey, InitialRetryDelay = TimeSpan.Zero },
            new HttpTypeSafeTransport(httpClient, new StaticApiKeyProvider(TestClient.TestApiKey), timeout: null),
            new StaticApiKeyProvider(TestClient.TestApiKey),
            logger: null);

        VerdictResponse verdict = await client.SystemOneAsync(
            "text",
            TestClient.ThreeQuestions(),
            TypedResponseContext.Default.VerdictResponse,
            model: null,
            CancellationToken.None);

        // A caller whose type derives from JevResponse gets it for free.
        Assert.NotNull(verdict);

        // And the untyped path still carries it.
        SystemOneResponse untyped = await client.SystemOneAsync(
            "text", TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        Assert.Equal("req_typed_1", untyped.RequestId);
    }

    [Fact]
    public async Task ATypedModelDerivingFromJevResponse_GetsTheRequestId()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        DerivingResponse verdict = await client.SystemOneAsync(
            "text",
            TestClient.ThreeQuestions(),
            TypedResponseContext.Default.DerivingResponse,
            model: null,
            CancellationToken.None);

        Assert.Equal(3, verdict.Answers.Count);
    }

    // ---------------------------------------------------------------- validation still runs

    [Fact]
    public async Task LocalValidation_StillRunsOnTheTypedPath()
    {
        // Validation depends only on the questions, so it is unaffected by which response type the
        // caller asked for.
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["q"] = new NoulQuestion(),
        };

        await Assert.ThrowsAsync<JevRequestValidationException>(
            () => client.SystemOneAsync(
                "text",
                questions,
                TypedResponseContext.Default.VerdictResponse,
                model: null,
                CancellationToken.None));

        Assert.Equal(0, transport.RequestCount);
    }

    [Fact]
    public async Task TheLibraryResponseType_StillWorksThroughTheTypedOverload()
    {
        // Passing the library's own response type through the generic overload must behave exactly like
        // the untyped form, because that path has no separate resolver to merge.
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        SystemOneResponse response = await client.SystemOneAsync(
            "text",
            TestClient.ThreeQuestions(),
            JevJsonContext.Default.SystemOneResponse,
            model: null,
            CancellationToken.None);

        Assert.Equal("jev-latest", response.Model);
        Assert.Equal("technical", response.Choice("department"));
    }

    [Fact]
    public async Task NullArguments_AreRejected()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => client.SystemOneAsync(
                "text",
                TestClient.ThreeQuestions(),
                (JsonTypeInfo<VerdictResponse>)null!,
                model: null,
                CancellationToken.None));

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => client.SystemOneAsync(
                "text",
                null!,
                TypedResponseContext.Default.VerdictResponse,
                model: null,
                CancellationToken.None));
    }

    [Fact]
    public async Task AMalformedBody_OnTheTypedPath_IsAProtocolError()
    {
        StubTransport transport = new StubTransport().EnqueueJson("{ not json");
        using JevClient client = TestClient.Create(transport);

        JevConnectionException exception = await Assert.ThrowsAsync<JevConnectionException>(
            () => client.SystemOneAsync(
                "text",
                TestClient.ThreeQuestions(),
                TypedResponseContext.Default.VerdictResponse,
                model: null,
                CancellationToken.None));

        Assert.True(exception.IsProtocolError);
        Assert.Equal(1, transport.RequestCount);
    }

    [Fact]
    public async Task TypedAndUntypedPaths_ProduceTheSamePayload()
    {
        // Both overloads share one implementation, so the wire bytes must be identical. This is the
        // regression guard against the two paths drifting apart.
        StubTransport typed = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        StubTransport untyped = new StubTransport().EnqueueJson(TestClient.SuccessJson());

        using (JevClient client = TestClient.Create(typed))
        {
            await client.SystemOneAsync(
                "same text",
                TestClient.ThreeQuestions(),
                TypedResponseContext.Default.VerdictResponse,
                model: "m",
                CancellationToken.None);
        }

        using (JevClient client = TestClient.Create(untyped))
        {
            await client.SystemOneAsync(
                "same text",
                TestClient.ThreeQuestions(),
                model: "m",
                CancellationToken.None);
        }

        string typedPayload = System.Text.Encoding.UTF8.GetString(typed.LastRequest!.Body!.Value.Span);
        string untypedPayload = System.Text.Encoding.UTF8.GetString(untyped.LastRequest!.Body!.Value.Span);

        Assert.Equal(untypedPayload, typedPayload);
    }
}

/// <summary>
/// A caller's own response shape, mirroring how Python's <c>response_model</c> is used: the caller
/// declares the fields they care about and ignores the rest of the payload.
/// </summary>
/// <remarks>
/// Top-level rather than nested, because the source generator for <see cref="JsonSerializerContext"/>
/// cannot reach a nested type.
/// </remarks>
internal sealed class VerdictResponse
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("answers")]
    public Dictionary<string, Answer> Answers { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("usage")]
    public Usage? Usage { get; set; }

    /// <summary>
    /// Captures anything the API sends that this type does not declare, so binding to a narrow type
    /// does not lose the forward-compatibility surface.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>A narrower still shape: just the answers, ignoring model and usage entirely.</summary>
internal sealed class AnswersOnly
{
    [JsonPropertyName("answers")]
    public Dictionary<string, Answer> Answers { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// A caller's type that derives from the library's response base, which is how a caller opts in to
/// the request id without re-reading the header themselves.
/// </summary>
internal sealed class DerivingResponse : JevResponse
{
    [JsonPropertyName("answers")]
    public Dictionary<string, Answer> Answers { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Source-generated type information for the caller's own response types.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(VerdictResponse))]
[JsonSerializable(typeof(AnswersOnly))]
[JsonSerializable(typeof(DerivingResponse))]
internal sealed partial class TypedResponseContext : JsonSerializerContext
{
}
