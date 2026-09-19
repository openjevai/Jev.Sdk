// SpecConformanceTests.cs
// Part of Jev.Sdk.Tests. A conformance suite derived directly from the live OpenAPI specification
// at https://api.typesafe.ai/openapi.json, not from the prose docs.
//
// Every property the specification declares is exercised in every shape it declares. The purpose is
// to prove the client supports the whole documented footprint rather than the common cases: the
// permissive `string | object | array | null` members, the map-of-permissive values, the mixed
// string-or-integer path elements, and the extension points the vendor documents as escape hatches.
//
// Where a declared shape is not supported, the test fails here rather than in a caller's code.

using System.Text.Json;
using Jev.Sdk;

namespace Jev.Sdk.Tests;

public class SpecConformanceTests
{
    // ================================================================ state: string | object | array

    [Fact]
    public async Task State_AcceptsAString()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        await client.SystemOneAsync("plain text", TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        JsonElement state = StateOf(transport);
        Assert.Equal(JsonValueKind.String, state.ValueKind);
        Assert.Equal("plain text", state.GetString());
    }

    [Fact]
    public async Task State_AcceptsAnObject()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        await client.SystemOneAsync(
            StructuredValue.FromJson(JsonDocument.Parse("""{"a":1,"b":[2,3]}""").RootElement),
            TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        JsonElement state = StateOf(transport);
        Assert.Equal(JsonValueKind.Object, state.ValueKind);
        Assert.Equal(1, state.GetProperty("a").GetInt32());
    }

    [Fact]
    public async Task State_AcceptsAnArray()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        await client.SystemOneAsync(
            StructuredValue.FromJson(JsonDocument.Parse("""["hi","there"]""").RootElement),
            TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        JsonElement state = StateOf(transport);
        Assert.Equal(JsonValueKind.Array, state.ValueKind);
        Assert.Equal(2, state.GetArrayLength());
    }

    [Fact]
    public async Task State_AcceptsAnArrayOfRecords()
    {
        // The spec's example for an array state is a sequence of records, so this is the shape the
        // vendor actually intends rather than an array of scalars.
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        await client.SystemOneAsync(
            StructuredValue.FromJson(JsonDocument.Parse(
                """[{"role":"user","text":"hi"},{"role":"agent","text":"hello"}]""").RootElement),
            TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        JsonElement state = StateOf(transport);
        Assert.Equal(JsonValueKind.Array, state.ValueKind);
        Assert.Equal("user", state[0].GetProperty("role").GetString());
    }

    // ================================================================ instructions: string | object | array | null

    [Fact]
    public async Task Instructions_AcceptAString()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["q"] = Question.Noul("is it urgent?"),
        };

        await client.SystemOneAsync("text", questions, model: null, CancellationToken.None);

        Assert.Equal(JsonValueKind.String, InstructionsOf(transport, "q").ValueKind);
    }

    [Fact]
    public async Task Instructions_AcceptAnObject()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["q"] = Question.Noul(StructuredValue.FromJson(
                JsonDocument.Parse("""{"field":{"name":"invoice_number","type":"string"}}""").RootElement)),
        };

        await client.SystemOneAsync("text", questions, model: null, CancellationToken.None);

        JsonElement instructions = InstructionsOf(transport, "q");
        Assert.Equal(JsonValueKind.Object, instructions.ValueKind);
        Assert.Equal("invoice_number", instructions.GetProperty("field").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Instructions_AcceptAnArray()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["q"] = Question.Noul(StructuredValue.FromJson(
                JsonDocument.Parse("""["first step","second step"]""").RootElement)),
        };

        await client.SystemOneAsync("text", questions, model: null, CancellationToken.None);

        JsonElement instructions = InstructionsOf(transport, "q");
        Assert.Equal(JsonValueKind.Array, instructions.ValueKind);
        Assert.Equal(2, instructions.GetArrayLength());
    }

    // ================================================================ choice criteria: map<string|object|array|null>

    [Fact]
    public async Task ChoiceCriteria_AcceptStringObjectArrayAndNullValues()
    {
        // The widest declaration in the spec: a map whose *values* may each independently be a
        // string, an object, an array, or null.
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        Dictionary<string, StructuredValue?> criteria = new(StringComparer.Ordinal)
        {
            ["as_string"] = StructuredValue.FromString("a plain description"),
            ["as_object"] = StructuredValue.FromJson(JsonDocument.Parse("""{"when":"always"}""").RootElement),
            ["as_array"] = StructuredValue.FromJson(JsonDocument.Parse("""["a","b"]""").RootElement),
            ["as_null"] = null,
        };

        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["q"] = Question.Choice(StructuredValue.FromString("classify"), criteria),
        };

        await client.SystemOneAsync("text", questions, model: null, CancellationToken.None);

        JsonElement sent = CriteriaOf(transport, "q");
        Assert.Equal(JsonValueKind.String, sent.GetProperty("as_string").ValueKind);
        Assert.Equal(JsonValueKind.Object, sent.GetProperty("as_object").ValueKind);
        Assert.Equal(JsonValueKind.Array, sent.GetProperty("as_array").ValueKind);
        Assert.Equal(JsonValueKind.Null, sent.GetProperty("as_null").ValueKind);
    }

    // ================================================================ score criteria: array<string|object|array>

    [Fact]
    public async Task ScoreCriteria_AcceptStringObjectAndArrayLevels()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        StructuredValue[] levels =
        [
            StructuredValue.FromString("Calm"),
            StructuredValue.FromJson(JsonDocument.Parse("""{"label":"Frustrated","weight":1}""").RootElement),
            StructuredValue.FromJson(JsonDocument.Parse("""["Very angry","strong language"]""").RootElement),
        ];

        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["q"] = Question.Score(StructuredValue.FromString("how frustrated?"), levels),
        };

        await client.SystemOneAsync("text", questions, model: null, CancellationToken.None);

        JsonElement criteria = CriteriaOf(transport, "q");
        Assert.Equal(JsonValueKind.Array, criteria.ValueKind);
        Assert.Equal(3, criteria.GetArrayLength());
        Assert.Equal(JsonValueKind.String, criteria[0].ValueKind);
        Assert.Equal(JsonValueKind.Object, criteria[1].ValueKind);
        Assert.Equal(JsonValueKind.Array, criteria[2].ValueKind);
    }

    // ================================================================ noul criteria: {true, false} each permissive

    [Fact]
    public async Task NoulCriteria_AcceptStructuredTrueAndFalse()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["q"] = Question.Noul(
                StructuredValue.FromString("is this spam?"),
                new NoulCriteria
                {
                    True = StructuredValue.FromJson(JsonDocument.Parse("""{"kind":"unsolicited"}""").RootElement),
                    False = StructuredValue.FromJson(JsonDocument.Parse("""["legitimate","expected"]""").RootElement),
                }),
        };

        await client.SystemOneAsync("text", questions, model: null, CancellationToken.None);

        JsonElement criteria = CriteriaOf(transport, "q");
        Assert.Equal(JsonValueKind.Object, criteria.GetProperty("true").ValueKind);
        Assert.Equal(JsonValueKind.Array, criteria.GetProperty("false").ValueKind);
    }

    // ================================================================ legend: map<string|object|array>

    [Fact]
    public async Task ScoreLegend_ReadsStructuredAndScalarLevels()
    {
        StubTransport transport = new StubTransport().EnqueueJson(
            """
            {
              "model": "jev-latest",
              "answers": {
                "s": {
                  "type": "score",
                  "score": 1.5,
                  "legend": { "0": "Calm", "1": { "label": "Frustrated" }, "2": ["Angry", "loud"] },
                  "probabilities": { "0": 0.1, "1": 0.4, "2": 0.5 },
                  "confidence": 0.7
                }
              },
              "usage": { "input_tokens": 1, "output_tokens": 1 }
            }
            """);

        using JevClient client = TestClient.Create(transport);

        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["s"] = Question.Score("rate", "a", "b", "c"),
        };

        SystemOneResponse response = await client.SystemOneAsync("text", questions, model: null, CancellationToken.None);

        ScoreAnswer score = response["s"].AsScore();
        Assert.Equal("Calm", score.Legend["0"].AsString());
        Assert.Equal(StructuredValue.JsonShape.Record, score.Legend["1"].Shape);
        Assert.Equal(StructuredValue.JsonShape.Sequence, score.Legend["2"].Shape);
    }

    // ================================================================ validation error shapes

    [Fact]
    public async Task ValidationError_ReadsAMixedStringAndIntegerLocationPath()
    {
        // The spec declares loc as array<string | integer>, so a path can mix a field name and an
        // array index. Reading it must not throw or lose either part.
        StubTransport transport = new StubTransport().EnqueueJson(
            """
            {
              "detail": [
                { "loc": ["body", "questions", 2, "criteria"], "msg": "Field required", "type": "missing" }
              ]
            }
            """,
            System.Net.HttpStatusCode.UnprocessableEntity);

        using JevClient client = TestClient.Create(transport);

        JevValidationException exception = await Assert.ThrowsAsync<JevValidationException>(
            () => client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None));

        ErrorDetails detail = Assert.Single(exception.Details);
        Assert.Equal(4, detail.Location.Count);
        Assert.Equal("body", detail.Location[0]?.ToString());
        Assert.Equal("2", detail.Location[2]?.ToString());
    }

    [Fact]
    public async Task ValidationError_ReadsArrayShapedInput()
    {
        // `input` has no declared type, so any JSON must survive.
        StubTransport transport = new StubTransport().EnqueueJson(
            """
            {
              "detail": [
                { "loc": ["body"], "msg": "bad", "type": "value_error", "input": [1, 2, 3] }
              ]
            }
            """,
            System.Net.HttpStatusCode.UnprocessableEntity);

        using JevClient client = TestClient.Create(transport);

        JevValidationException exception = await Assert.ThrowsAsync<JevValidationException>(
            () => client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None));

        Assert.Equal(JsonValueKind.Array, Assert.Single(exception.Details).Input!.Element.ValueKind);
    }

    // ================================================================ forward-compat escape hatches

    [Fact]
    public async Task UnknownFieldsOnAQuestion_AreSentOnTheWire()
    {
        // The vendor documents unknown fields as a forward-compatibility escape hatch, so a caller
        // must be able to add one without the client stripping it.
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        NoulQuestion question = Question.Noul("is it?");
        question.AdditionalProperties = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["weight"] = JsonDocument.Parse("2").RootElement.Clone(),
        };

        Dictionary<string, Question> questions = new(StringComparer.Ordinal) { ["q"] = question };

        await client.SystemOneAsync("text", questions, model: null, CancellationToken.None);

        JsonElement sent = QuestionsOf(transport).GetProperty("q");
        Assert.Equal(2, sent.GetProperty("weight").GetInt32());
    }

    [Fact]
    public void UnknownFieldsOnTheRequest_AreSentOnTheWire()
    {
        // The vendor's SDKs expose `extra_body` for top-level request fields their models do not
        // know about, and their docs call unrecognised fields a forward-compatibility escape hatch.
        // The equivalent here is extension data on the request, and it must reach the wire.
        SystemOneRequest request = new()
        {
            State = StructuredValue.FromString("text"),
            Model = "jev-latest",
            Questions = new Dictionary<string, Question>(StringComparer.Ordinal) { ["q"] = Question.Noul("is it?") },
            AdditionalProperties = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["beam_width"] = JsonDocument.Parse("4").RootElement.Clone(),
                ["experimental_flag"] = JsonDocument.Parse("true").RootElement.Clone(),
            },
        };

        string json = JsonSerializer.Serialize(request, JevJsonContext.Default.Options);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement body = document.RootElement;

        // The three declared members are present...
        Assert.True(body.TryGetProperty("state", out _));
        Assert.True(body.TryGetProperty("model", out _));
        Assert.True(body.TryGetProperty("questions", out _));

        // ...and so are the caller's unmodelled ones.
        Assert.Equal(4, body.GetProperty("beam_width").GetInt32());
        Assert.True(body.GetProperty("experimental_flag").GetBoolean());
    }

    [Fact]
    public async Task UnknownFieldsOnAChoiceOption_AreSentOnTheWire()
    {
        // A Choice option's description is itself permissive, but a caller may also want to attach
        // metadata to the question. Extension data must survive serialization inside a nested map.
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        ChoiceQuestion question = Question.Choice(
            StructuredValue.FromString("classify"),
            new Dictionary<string, StructuredValue?>(StringComparer.Ordinal) { ["a"] = StructuredValue.FromString("first") });

        question.AdditionalProperties = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["weight"] = JsonDocument.Parse("3").RootElement.Clone(),
        };

        Dictionary<string, Question> questions = new(StringComparer.Ordinal) { ["q"] = question };

        await client.SystemOneAsync("text", questions, model: null, CancellationToken.None);

        JsonElement sent = QuestionsOf(transport).GetProperty("q");
        Assert.Equal(3, sent.GetProperty("weight").GetInt32());
    }

    [Fact]
    public async Task UnknownFieldsOnTheResponse_ArePreserved()
    {
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

        SystemOneResponse response = await client.SystemOneAsync("text", questions, model: null, CancellationToken.None);

        Assert.NotNull(response.AdditionalProperties);
        Assert.True(response.AdditionalProperties!.ContainsKey("request_metadata"));
    }

    // ================================================================ headers

    [Fact]
    public async Task RequestId_Header_IsReadFromARealResponse()
    {
        StubHttpMessageHandler handler = new StubHttpMessageHandler().Enqueue(_ =>
        {
            HttpResponseMessage message = new(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(TestClient.SuccessJson(), System.Text.Encoding.UTF8, "application/json"),
            };

            message.Headers.TryAddWithoutValidation("x-typesafe-request-id", "req_abc");
            return message;
        });

        System.Net.Http.HttpClient httpClient = new(handler, disposeHandler: false);

        using JevClient client = new(
            new JevClientOptions { ApiKey = TestClient.TestApiKey, InitialRetryDelay = TimeSpan.Zero },
            new HttpTypeSafeTransport(httpClient, new StaticApiKeyProvider(TestClient.TestApiKey), timeout: null),
            new StaticApiKeyProvider(TestClient.TestApiKey),
            logger: null);

        Dictionary<string, Question> questions = new(StringComparer.Ordinal) { ["a"] = Question.Noul("is it?") };

        SystemOneResponse response = await client.SystemOneAsync("text", questions, model: null, CancellationToken.None);

        Assert.Equal("req_abc", response.RequestId);
    }

    [Fact]
    public async Task CustomRequestHeaders_AreSentWhenConfigured()
    {
        // The vendor's SDKs both accept additional request headers. Without an equivalent here, a
        // caller behind a proxy or gateway that needs its own header cannot use this client at all.
        RecordingHandler handler = new RecordingHandler(TestClient.SuccessJson());

        System.Net.Http.HttpClient httpClient = new(handler, disposeHandler: false);

        JevClientOptions options = new()
        {
            ApiKey = TestClient.TestApiKey,
            InitialRetryDelay = TimeSpan.Zero,
        };

        options.Headers["X-Tenant"] = "acme";
        options.Headers["X-Trace"] = "trace-123";

        using JevClient client = new(
            options,
            new HttpTypeSafeTransport(httpClient, new StaticApiKeyProvider(TestClient.TestApiKey), timeout: null),
            new StaticApiKeyProvider(TestClient.TestApiKey),
            logger: null);

        Dictionary<string, Question> questions = new(StringComparer.Ordinal) { ["a"] = Question.Noul("is it?") };

        await client.SystemOneAsync("text", questions, model: null, CancellationToken.None);

        Assert.Equal("acme", handler.LastRequest!.Headers.GetValues("X-Tenant").Single());
        Assert.Equal("trace-123", handler.LastRequest.Headers.GetValues("X-Trace").Single());
    }

    [Fact]
    public async Task AuthorizationHeader_IsPresentOnEveryRequest()
    {
        RecordingHandler handler = new RecordingHandler(TestClient.SuccessJson());
        System.Net.Http.HttpClient httpClient = new(handler, disposeHandler: false);

        using JevClient client = new(
            new JevClientOptions { ApiKey = "my-key", InitialRetryDelay = TimeSpan.Zero },
            new HttpTypeSafeTransport(httpClient, new StaticApiKeyProvider("my-key"), timeout: null),
            new StaticApiKeyProvider("my-key"),
            logger: null);

        Dictionary<string, Question> questions = new(StringComparer.Ordinal) { ["a"] = Question.Noul("is it?") };

        await client.SystemOneAsync("text", questions, model: null, CancellationToken.None);

        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("my-key", handler.LastRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task ContentType_IsApplicationJsonOnPost()
    {
        RecordingHandler handler = new RecordingHandler(TestClient.SuccessJson());
        System.Net.Http.HttpClient httpClient = new(handler, disposeHandler: false);

        using JevClient client = new(
            new JevClientOptions { ApiKey = TestClient.TestApiKey, InitialRetryDelay = TimeSpan.Zero },
            new HttpTypeSafeTransport(httpClient, new StaticApiKeyProvider(TestClient.TestApiKey), timeout: null),
            new StaticApiKeyProvider(TestClient.TestApiKey),
            logger: null);

        Dictionary<string, Question> questions = new(StringComparer.Ordinal) { ["a"] = Question.Noul("is it?") };

        await client.SystemOneAsync("text", questions, model: null, CancellationToken.None);

        Assert.Equal("application/json", handler.LastRequest!.Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Accept_IsApplicationJson()
    {
        RecordingHandler handler = new RecordingHandler(TestClient.SuccessJson());
        System.Net.Http.HttpClient httpClient = new(handler, disposeHandler: false);

        using JevClient client = new(
            new JevClientOptions { ApiKey = TestClient.TestApiKey, InitialRetryDelay = TimeSpan.Zero },
            new HttpTypeSafeTransport(httpClient, new StaticApiKeyProvider(TestClient.TestApiKey), timeout: null),
            new StaticApiKeyProvider(TestClient.TestApiKey),
            logger: null);

        Dictionary<string, Question> questions = new(StringComparer.Ordinal) { ["a"] = Question.Noul("is it?") };

        await client.SystemOneAsync("text", questions, model: null, CancellationToken.None);

        Assert.Contains(handler.LastRequest!.Headers.Accept, a => a.MediaType == "application/json");
    }

    [Fact]
    public void Headers_RejectABlankName()
    {
        JevClientOptions options = new() { ApiKey = "k" };
        options.Headers["   "] = "value";

        JevConfigurationException exception = Assert.Throws<JevConfigurationException>(
            () => new JevClient(options, new StubTransport(), new StaticApiKeyProvider("k"), null));

        Assert.Contains("blank header name", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Headers_RejectAnAttemptToSetAuthorization()
    {
        // A credential must arrive through the key provider rather than as an ordinary string in a
        // header table, so attempting to set it here is refused at construction.
        JevClientOptions options = new() { ApiKey = "k" };
        options.Headers["Authorization"] = "Bearer sneaky";

        JevConfigurationException exception = Assert.Throws<JevConfigurationException>(
            () => new JevClient(options, new StubTransport(), new StaticApiKeyProvider("k"), null));

        Assert.Contains("cannot set the Authorization header", exception.Message, StringComparison.Ordinal);

        // The check is case-insensitive, because HTTP header names are.
        JevClientOptions lower = new() { ApiKey = "k" };
        lower.Headers["authorization"] = "Bearer sneaky";

        Assert.Throws<JevConfigurationException>(
            () => new JevClient(lower, new StubTransport(), new StaticApiKeyProvider("k"), null));
    }

    [Fact]
    public void Headers_AreCopiedOntoTheSnapshot()
    {
        // A caller mutating the options object after construction must not change a live client.
        JevClientOptions options = new() { ApiKey = "k" };
        options.Headers["X-One"] = "1";

        JevClientOptions snapshot = options.Snapshot();
        options.Headers["X-Two"] = "2";

        Assert.Equal("1", snapshot.Headers["X-One"]);
        Assert.False(snapshot.Headers.ContainsKey("X-Two"));
    }

    // ================================================================ helpers

    private static JsonElement RequestBody(StubTransport transport)
    {
        string payload = System.Text.Encoding.UTF8.GetString(transport.LastRequest!.Body!.Value.Span);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private static JsonElement StateOf(StubTransport transport) =>
        RequestBody(transport).GetProperty("state");

    private static JsonElement QuestionsOf(StubTransport transport) =>
        RequestBody(transport).GetProperty("questions");

    private static JsonElement InstructionsOf(StubTransport transport, string questionId) =>
        QuestionsOf(transport).GetProperty(questionId).GetProperty("instructions");

    private static JsonElement CriteriaOf(StubTransport transport, string questionId) =>
        QuestionsOf(transport).GetProperty(questionId).GetProperty("criteria");
}

/// <summary>
/// A handler that captures the outgoing request so headers can be asserted, and returns a fixed
/// body.
/// </summary>
internal sealed class RecordingHandler : HttpMessageHandler
{
    private readonly string _body;

    internal RecordingHandler(string body) => _body = body;

    /// <summary>The most recent request received.</summary>
    internal HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;

        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(_body, System.Text.Encoding.UTF8, "application/json"),
        });
    }
}
