// ForwardCompatibilityTests.cs
// Part of Jev.Sdk.Tests. The API will gain question and answer kinds this library does not
// model. When it does, the rest of a payload must still arrive intact. These tests prove that,
// because it is the property that stops a working integration from breaking on an API release
// the caller did not control.

using System.Text.Json;
using Jev.Sdk;

namespace Jev.Sdk.Tests;

public class ForwardCompatibilityTests
{
    [Fact]
    public async Task UnknownAnswerKind_DoesNotFailTheResponse()
    {
        StubTransport transport = new StubTransport().EnqueueJson(
            """
            {
              "model": "jev-latest",
              "answers": {
                "is_urgent": { "type": "noul", "noul": 0.92 },
                "sentiment": { "type": "ranking", "ranking": ["a", "b"], "note": "future kind" }
              },
              "usage": { "input_tokens": 10, "output_tokens": 2 }
            }
            """);

        using JevClient client = TestClient.Create(transport);

        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["is_urgent"] = Question.Noul("Is this urgent?"),
        };

        SystemOneResponse response = await client.SystemOneAsync("text", questions, model: null, CancellationToken.None);

        // The modelled answer is untouched by the unfamiliar one beside it.
        Assert.Equal(0.92, response["is_urgent"].AsNoul().Noul, 4);

        UnknownAnswer unknown = Assert.IsType<UnknownAnswer>(response["sentiment"]);
        Assert.Equal("ranking", unknown.Type);
        Assert.Equal(JsonValueKind.Array, unknown.RawJson.GetProperty("ranking").ValueKind);
        Assert.Equal("future kind", unknown.RawJson.GetProperty("note").GetString());
    }

    [Fact]
    public void UnknownQuestionKind_DeserializesAsRawQuestion()
    {
        Question question = JsonSerializer.Deserialize<Question>(
            """{"type":"ranking","instructions":"rank these","options":["a","b"]}""",
            JevJsonContext.Default.Options)!;

        RawQuestion raw = Assert.IsType<RawQuestion>(question);
        Assert.Equal("ranking", raw.Type);
        Assert.Equal("rank these", raw.RawJson.GetProperty("instructions").GetString());
        Assert.Equal(JsonValueKind.Array, raw.RawJson.GetProperty("options").ValueKind);
    }

    [Fact]
    public void KnownQuestionKinds_DeserializeAsTheirConcreteTypes()
    {
        Question noul = JsonSerializer.Deserialize<Question>(
            """{"type":"noul","instructions":"is it true?"}""",
            JevJsonContext.Default.Options)!;
        Assert.IsType<NoulQuestion>(noul);

        Question choice = JsonSerializer.Deserialize<Question>(
            """{"type":"choice","instructions":"pick","criteria":{"a":null,"b":"second"}}""",
            JevJsonContext.Default.Options)!;
        Assert.IsType<ChoiceQuestion>(choice);

        Question score = JsonSerializer.Deserialize<Question>(
            """{"type":"score","instructions":"rate","criteria":["low","high"]}""",
            JevJsonContext.Default.Options)!;
        Assert.IsType<ScoreQuestion>(score);
    }

    [Fact]
    public void UnmodelledFields_OnAKnownAnswer_ArePreserved()
    {
        Answer answer = JsonSerializer.Deserialize<Answer>(
            """{"type":"noul","noul":0.5,"future_field":{"a":1}}""",
            JevJsonContext.Default.Options)!;

        NoulAnswer noul = Assert.IsType<NoulAnswer>(answer);
        Assert.NotNull(noul.AdditionalProperties);
        Assert.True(noul.AdditionalProperties!.ContainsKey("future_field"));
    }

    [Fact]
    public void UnknownDiscriminator_WithoutType_ThrowsJsonException()
    {
        // A missing discriminator is a malformed payload rather than a forward-compatibility
        // case, so it fails loudly instead of guessing at a kind.
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<Answer>("""{"noul":0.5}""", JevJsonContext.Default.Options));
    }

    [Fact]
    public void ANullAnswerToken_IsRejected()
    {
        // Null is not an object, and a kind cannot be inferred from it. A clear protocol error beats a
        // silently null answer.
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<Answer>("null", JevJsonContext.Default.Options));
    }

    [Fact]
    public void ANullAnswerValue_SerializesAsJsonNull()
    {
        string written = JsonSerializer.Serialize((Answer?)null, JevJsonContext.Default.Options);

        Assert.Equal("null", written);
    }

    [Fact]
    public void NonObjectAnswer_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<Answer>("""["not","an","object"]""", JevJsonContext.Default.Options));
    }

    [Fact]
    public void UnknownAnswerKind_CanBeRoundTripped()
    {
        Answer answer = JsonSerializer.Deserialize<Answer>(
            """{"type":"ranking","ranking":["a","b"]}""",
            JevJsonContext.Default.Options)!;

        string written = JsonSerializer.Serialize(answer, JevJsonContext.Default.Options);

        using JsonDocument document = JsonDocument.Parse(written);
        Assert.Equal("ranking", document.RootElement.GetProperty("type").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("ranking").GetArrayLength());
    }

    [Fact]
    public void UnknownQuestionKind_CanBeRoundTripped()
    {
        Question question = JsonSerializer.Deserialize<Question>(
            """{"type":"ranking","options":["a","b"]}""",
            JevJsonContext.Default.Options)!;

        string written = JsonSerializer.Serialize(question, JevJsonContext.Default.Options);

        using JsonDocument document = JsonDocument.Parse(written);
        Assert.Equal("ranking", document.RootElement.GetProperty("type").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("options").GetArrayLength());
    }
}
