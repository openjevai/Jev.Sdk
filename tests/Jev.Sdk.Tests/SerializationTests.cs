// SerializationTests.cs
// Part of Jev.Sdk.Tests. Wire-shape tests: what goes out must match the documented request, and
// what comes back must be read into the right typed kinds. These are the tests that catch a
// contract drift, which is the failure mode a client library exists to absorb.

using System.Text.Json;
using Jev.Sdk;

namespace Jev.Sdk.Tests;

public class SerializationTests
{
    [Fact]
    public async Task SystemOne_SendsSnakeCasedProperties()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        await client.SystemOneAsync("Help!", TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        string payload = System.Text.Encoding.UTF8.GetString(transport.LastRequest!.Body!.Value.Span);
        using JsonDocument document = JsonDocument.Parse(payload);

        Assert.True(document.RootElement.TryGetProperty("state", out _));
        Assert.True(document.RootElement.TryGetProperty("model", out _));
        Assert.True(document.RootElement.TryGetProperty("questions", out _));
        Assert.False(document.RootElement.TryGetProperty("Questions", out _));
    }

    [Fact]
    public async Task SystemOne_QuestionIdsSurviveRoundTripUnmodified()
    {
        // A question id is a dictionary key, not a property name. Any dictionary-key naming
        // policy would rewrite 'isUrgent' to 'is_urgent' and the caller would look for an answer
        // under a name that was never returned.
        const string mixedCaseId = "isUrgent";

        StubTransport transport = new StubTransport().EnqueueJson(
            """{"model":"jev-latest","answers":{"isUrgent":{"type":"noul","noul":0.9}},"usage":{"input_tokens":1,"output_tokens":1}}""");

        using JevClient client = TestClient.Create(transport);

        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            [mixedCaseId] = Question.Noul("Is this urgent?"),
        };

        SystemOneResponse response = await client.SystemOneAsync("text", questions, model: null, CancellationToken.None);

        string payload = System.Text.Encoding.UTF8.GetString(transport.LastRequest!.Body!.Value.Span);
        Assert.Contains("\"isUrgent\"", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("\"is_urgent\"", payload, StringComparison.Ordinal);

        Assert.True(response.AnswersOrEmpty.ContainsKey(mixedCaseId));
        Assert.Equal(0.9, response[mixedCaseId].AsNoul().Noul, 4);
    }

    [Fact]
    public async Task SystemOne_WritesAllThreeQuestionKinds()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        await client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        string payload = System.Text.Encoding.UTF8.GetString(transport.LastRequest!.Body!.Value.Span);

        Assert.Contains("\"type\":\"noul\"", payload, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"choice\"", payload, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"score\"", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SystemOne_ReadsAllThreeAnswerKinds()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        SystemOneResponse response = await client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        Assert.Equal("jev-latest", response.Model);

        NoulAnswer noul = Assert.IsType<NoulAnswer>(response["is_urgent"]);
        Assert.Equal(0.92, noul.Noul, 4);

        ChoiceAnswer choice = Assert.IsType<ChoiceAnswer>(response["department"]);
        Assert.Equal("technical", choice.Choice);
        Assert.Equal(0.85, choice.Probabilities["technical"], 4);
        Assert.Equal(0.82, choice.Confidence!.Value, 4);

        ScoreAnswer score = Assert.IsType<ScoreAnswer>(response["frustration"]);
        Assert.Equal(1.6, score.Score, 4);
        Assert.Equal(3, score.Legend.Count);
        Assert.Equal("Frustrated", score.Legend["1"].AsString());
        Assert.Equal(0.78, score.Confidence!.Value, 4);
    }

    [Fact]
    public async Task SystemOne_PreservesNumbersWithoutRounding()
    {
        StubTransport transport = new StubTransport().EnqueueJson(
            """{"model":"m","answers":{"s":{"type":"score","score":1.035,"legend":{"0":"a","1":"b"},"probabilities":{"0":0.1,"1":0.9},"confidence":0.842}},"usage":{"input_tokens":1,"output_tokens":1}}""");

        using JevClient client = TestClient.Create(transport);

        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["s"] = Question.Score("rate", "a", "b"),
        };

        SystemOneResponse response = await client.SystemOneAsync("text", questions, model: null, CancellationToken.None);

        ScoreAnswer score = response["s"].AsScore();
        Assert.Equal(1.035, score.Score, 10);
        Assert.Equal(0.842, score.Confidence!.Value, 10);
    }

    [Fact]
    public async Task SystemOne_NoulAnswerCarriesNoConfidence()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        SystemOneResponse response = await client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        Assert.Null(response["is_urgent"].Confidence);
        Assert.NotNull(response["department"].Confidence);
        Assert.NotNull(response["frustration"].Confidence);
    }

    [Fact]
    public async Task SystemOne_StructuredStateIsSentAsStructure()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        using JsonDocument state = JsonDocument.Parse("""{"message":"hi","subject":"order"}""");

        await client.SystemOneAsync(state.RootElement, TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        string payload = System.Text.Encoding.UTF8.GetString(transport.LastRequest!.Body!.Value.Span);
        using JsonDocument document = JsonDocument.Parse(payload);

        Assert.Equal(JsonValueKind.Object, document.RootElement.GetProperty("state").ValueKind);
        Assert.Equal("hi", document.RootElement.GetProperty("state").GetProperty("message").GetString());
    }

    [Fact]
    public async Task SystemOne_CallerOwnedStateUsesCallerSerialization()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        CallerState state = new() { Subject = "Duplicate charge", Message = "Please help." };

        await client.SystemOneAsync(
            state,
            CallerStateContext.Default.CallerState,
            TestClient.ThreeQuestions(),
            model: null,
            CancellationToken.None);

        string payload = System.Text.Encoding.UTF8.GetString(transport.LastRequest!.Body!.Value.Span);
        using JsonDocument document = JsonDocument.Parse(payload);

        Assert.Equal(
            "Duplicate charge",
            document.RootElement.GetProperty("state").GetProperty("subject").GetString());
    }

    [Fact]
    public async Task SystemOne_StructuredInstructionsAreSentAsStructure()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        using JsonDocument instructions = JsonDocument.Parse("""{"task":"Identify spam","locale":"en"}""");

        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["q"] = Question.Noul(StructuredValue.FromJson(instructions.RootElement)),
        };

        await client.SystemOneAsync("text", questions, model: null, CancellationToken.None);

        string payload = System.Text.Encoding.UTF8.GetString(transport.LastRequest!.Body!.Value.Span);
        using JsonDocument document = JsonDocument.Parse(payload);

        JsonElement sent = document.RootElement.GetProperty("questions").GetProperty("q").GetProperty("instructions");
        Assert.Equal(JsonValueKind.Object, sent.ValueKind);
        Assert.Equal("Identify spam", sent.GetProperty("task").GetString());
    }

    [Fact]
    public async Task GetModels_ReadsModelList()
    {
        StubTransport transport = new StubTransport().EnqueueJson(
            """{"models":[{"name":"jev-latest","description":"General-purpose system one model.","release_date":"2026-09-15"}]}""");

        using JevClient client = TestClient.Create(transport);

        IReadOnlyList<ModelMetadata> models = await client.GetModelsAsync(CancellationToken.None);

        ModelMetadata model = Assert.Single(models);
        Assert.Equal("jev-latest", model.Name);
        Assert.Equal("General-purpose system one model.", model.Description);
        Assert.Equal(new DateOnly(2026, 9, 15), model.ParsedReleaseDate);
    }

    [Fact]
    public async Task GetModels_SendsGetWithNoBody()
    {
        StubTransport transport = new StubTransport().EnqueueJson("""{"models":[]}""");
        using JevClient client = TestClient.Create(transport);

        await client.GetModelsAsync(CancellationToken.None);

        Assert.Equal(HttpMethod.Get, transport.LastRequest!.Method);
        Assert.Null(transport.LastRequest.Body);
        Assert.EndsWith("models", transport.LastRequest.Uri.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SystemOne_UsesConfiguredBaseAddress()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport, o => o.BaseAddress = new Uri("https://example.test/v1/"));

        await client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        Assert.Equal("https://example.test/v1/systemone", transport.LastRequest!.Uri.AbsoluteUri);
    }

    [Fact]
    public async Task SystemOne_UsesDefaultModelWhenNoneGiven()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport, o => o.DefaultModel = "my-model");

        await client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        string payload = System.Text.Encoding.UTF8.GetString(transport.LastRequest!.Body!.Value.Span);
        using JsonDocument document = JsonDocument.Parse(payload);

        Assert.Equal("my-model", document.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public void JevJson_RejectsDictionaryKeyPolicy()
    {
        // Applying a dictionary-key naming policy would silently rewrite question ids, so the
        // client refuses the configuration rather than letting it corrupt a response.
        JevClientOptions options = new()
        {
            ApiKey = TestClient.TestApiKey,
            ConfigureJson = o => o.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        };

        Assert.Throws<JevConfigurationException>(() => new JevClient(
            options,
            new StubTransport(),
            new StaticApiKeyProvider(TestClient.TestApiKey),
            logger: null));
    }
}
