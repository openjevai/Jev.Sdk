// SpecPrecisionTests.cs
// Part of Jev.Sdk.Tests. Three defects found by probing the semantics rather than the names. Every
// one of them passed a name-based diff of the specification against the implementation, because all
// three are about behaviour rather than about which members exist.
//
//   1. A JSON null inside any collection of StructuredValue deserialized to a C# null rather than to
//      StructuredValue.Null, so a perfectly valid response from the API threw NullReferenceException
//      when read. The cause is that JsonConverter<T>.HandleNull defaults to false for reference
//      types, which lets the serializer handle the null token itself and never call the converter.
//   2. JsonElementBox had the same defect, which defeated the reason the box exists: its whole
//      purpose is to make an absent member distinguishable from a JSON null.
//   3. Computed members were being serialized. Parsing a response and writing it back emitted
//      invented fields such as total_tokens, parsed_release_date, and location_path that the API
//      does not define.

using System.Text.Json;
using Jev.Sdk;

namespace Jev.Sdk.Tests;

public class SpecPrecisionTests
{
    // ---------------------------------------------------------------- 1. null inside collections

    [Fact]
    public void ScoreLegend_AcceptsAJsonNullValue()
    {
        // legend is declared map<string | object | array>. A null is not one of those shapes, but a
        // server that emits one must not break the whole response, and reading it must not throw.
        ScoreAnswer answer = JsonSerializer.Deserialize<ScoreAnswer>(
            """
            {
              "type": "score",
              "score": 1.0,
              "legend": { "0": "plain", "1": { "k": "v" }, "2": [ "a" ], "3": null },
              "probabilities": { "0": 0.25, "1": 0.25, "2": 0.25, "3": 0.25 },
              "confidence": 0.5
            }
            """,
            JevJsonContext.Default.Options)!;

        Assert.Equal(4, answer.Legend.Count);
        Assert.Equal("plain", answer.Legend["0"].AsString());
        Assert.Equal(StructuredValue.JsonShape.Record, answer.Legend["1"].Shape);
        Assert.Equal(StructuredValue.JsonShape.Sequence, answer.Legend["2"].Shape);

        // This is the line that used to throw.
        Assert.NotNull(answer.Legend["3"]);
        Assert.True(answer.Legend["3"].IsNull);
    }

    [Fact]
    public void ChoiceCriteria_AcceptsAJsonNullValue()
    {
        ChoiceQuestion question = JsonSerializer.Deserialize<ChoiceQuestion>(
            """{"type":"choice","instructions":"pick","criteria":{"a":"s","b":null,"c":{"k":1},"d":[1]}}""",
            JevJsonContext.Default.Options)!;

        Assert.Equal(4, question.Criteria.Count);
        Assert.Equal("s", question.Criteria["a"]!.AsString());

        // A null description means the option is judged by its name alone, which the docs call out as
        // a supported case, so it must survive rather than becoming a collection of nulls.
        Assert.NotNull(question.Criteria["b"]);
        Assert.True(question.Criteria["b"]!.IsNull);

        Assert.Equal(StructuredValue.JsonShape.Record, question.Criteria["c"]!.Shape);
        Assert.Equal(StructuredValue.JsonShape.Sequence, question.Criteria["d"]!.Shape);
    }

    [Fact]
    public void NullInsideACollection_RoundTrips()
    {
        ChoiceQuestion question = new()
        {
            Instructions = StructuredValue.FromString("pick"),
            Criteria = new Dictionary<string, StructuredValue?>(StringComparer.Ordinal)
            {
                ["a"] = StructuredValue.FromString("first"),
                ["b"] = StructuredValue.Null,
            },
        };

        string written = JsonSerializer.Serialize(question, JevJsonContext.Default.Options);

        using JsonDocument document = JsonDocument.Parse(written);
        JsonElement criteria = document.RootElement.GetProperty("criteria");

        Assert.Equal(JsonValueKind.String, criteria.GetProperty("a").ValueKind);
        Assert.Equal(JsonValueKind.Null, criteria.GetProperty("b").ValueKind);

        // And reading it back gives a value rather than a null.
        ChoiceQuestion reread = JsonSerializer.Deserialize<ChoiceQuestion>(written, JevJsonContext.Default.Options)!;
        Assert.NotNull(reread.Criteria["b"]);
        Assert.True(reread.Criteria["b"]!.IsNull);
    }

    [Fact]
    public void NoulQuestion_WithANullInstruction_DeserializesToANullValue()
    {
        // instructions accepts null per the specification, so a question carrying one must read back
        // as a null *value* rather than as an absent member. The distinction matters because local
        // validation reports them differently.
        NoulQuestion question = JsonSerializer.Deserialize<NoulQuestion>(
            """{"type":"noul","instructions":null}""",
            JevJsonContext.Default.Options)!;

        Assert.NotNull(question.Instructions);
        Assert.True(question.Instructions!.IsNull);
    }

    [Fact]
    public void StructuredValueConverter_HandlesTheNullTokenItself()
    {
        // The regression guard for the root cause. If this becomes false again, the serializer starts
        // handling null tokens and every collection member silently becomes a C# null.
        Assert.True(new StructuredValueJsonConverter().HandleNull);
        Assert.True(new JsonElementBoxConverter().HandleNull);
    }

    // ---------------------------------------------------------------- 2. absent versus null

    [Fact]
    public void ValidationInput_DistinguishesAbsentFromJsonNull()
    {
        // input has no declared type, so JSON null is a legal value. The box exists precisely so that
        // an absent member and a null member are not the same thing.
        ValidationErrorResponse absent = JsonSerializer.Deserialize<ValidationErrorResponse>(
            """{"detail":[{"loc":["x"],"msg":"m","type":"t"}]}""",
            JevJsonContext.Default.Options)!;

        ValidationErrorResponse present = JsonSerializer.Deserialize<ValidationErrorResponse>(
            """{"detail":[{"loc":["x"],"msg":"m","type":"t","input":null}]}""",
            JevJsonContext.Default.Options)!;

        Assert.Null(absent.Detail[0].Input);
        Assert.NotNull(present.Detail[0].Input);
        Assert.Equal(JsonValueKind.Null, present.Detail[0].Input!.Element.ValueKind);
    }

    [Fact]
    public void ANullBox_SerializesAsJsonNull()
    {
        // A null box is written as JSON null rather than throwing or emitting nothing, so a caller
        // that constructs one by hand gets valid JSON.
        string written = JsonSerializer.Serialize(
            (JsonElementBox?)null,
            JevJsonContext.Default.Options);

        Assert.Equal("null", written);
    }

    [Fact]
    public void ANullStructuredValue_SerializesAsJsonNull()
    {
        string written = JsonSerializer.Serialize(
            (StructuredValue?)null,
            JevJsonContext.Default.Options);

        Assert.Equal("null", written);
    }

    [Fact]
    public void ValidationInput_AcceptsEveryJsonKind()
    {
        foreach ((string Json, JsonValueKind Expected) value in new[]
        {
            ("\"s\"", JsonValueKind.String),
            ("42", JsonValueKind.Number),
            ("true", JsonValueKind.True),
            ("false", JsonValueKind.False),
            ("[1,2]", JsonValueKind.Array),
            ("{\"a\":1}", JsonValueKind.Object),
            ("null", JsonValueKind.Null),
        })
        {
            ValidationErrorResponse response = JsonSerializer.Deserialize<ValidationErrorResponse>(
                "{\"detail\":[{\"loc\":[\"x\"],\"msg\":\"m\",\"type\":\"t\",\"input\":" + value.Json + "}]}",
                JevJsonContext.Default.Options)!;

            Assert.NotNull(response.Detail[0].Input);
            Assert.Equal(value.Expected, response.Detail[0].Input!.Element.ValueKind);
        }
    }

    // ---------------------------------------------------------------- 3. no computed members on the wire

    [Fact]
    public void Usage_DoesNotSerializeItsComputedTotal()
    {
        // total_tokens is not a field the API defines. Writing it back would be a protocol deviation,
        // and it invites the reader to think the server sends it.
        string written = JsonSerializer.Serialize(
            new Usage { InputTokens = 312, OutputTokens = 48 },
            JevJsonContext.Default.Options);

        using JsonDocument document = JsonDocument.Parse(written);

        Assert.Equal(312, document.RootElement.GetProperty("input_tokens").GetInt32());
        Assert.Equal(48, document.RootElement.GetProperty("output_tokens").GetInt32());
        Assert.False(document.RootElement.TryGetProperty("total_tokens", out _));
    }

    [Fact]
    public void ModelMetadata_DoesNotSerializeItsParsedDate()
    {
        string written = JsonSerializer.Serialize(
            new ModelMetadata { Name = "n", Description = "d", ReleaseDate = "2026-01-01" },
            JevJsonContext.Default.Options);

        using JsonDocument document = JsonDocument.Parse(written);

        Assert.Equal("2026-01-01", document.RootElement.GetProperty("release_date").GetString());
        Assert.False(document.RootElement.TryGetProperty("parsed_release_date", out _));
    }

    [Fact]
    public void ErrorDetails_DoesNotSerializeItsRenderedPath()
    {
        string written = JsonSerializer.Serialize(
            new ErrorDetails { Location = ["a", 1], Message = "m", ErrorType = "t" },
            JevJsonContext.Default.Options);

        using JsonDocument document = JsonDocument.Parse(written);

        Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty("loc").ValueKind);
        Assert.False(document.RootElement.TryGetProperty("location_path", out _));
    }

    [Fact]
    public async Task EveryWireMemberOfAResponse_ParsesAndRewritesToTheSameFieldNames()
    {
        // A whole-response round trip, which is the check that catches a computed member leaking:
        // anything that appears in the output but not in the input is invented.
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        SystemOneResponse response = await client.SystemOneAsync(
            "text", TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        string rewritten = JsonSerializer.Serialize(response, JevJsonContext.Default.Options);

        using JsonDocument original = JsonDocument.Parse(TestClient.SuccessJson());
        using JsonDocument round = JsonDocument.Parse(rewritten);

        List<string> originalFields = [.. Flatten(original.RootElement)];
        List<string> roundFields = [.. Flatten(round.RootElement)];

        string[] invented = [.. roundFields.Except(originalFields).Order()];

        Assert.Empty(invented);

        static IEnumerable<string> Flatten(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        yield return property.Name;

                        foreach (string nested in Flatten(property.Value))
                        {
                            yield return nested;
                        }
                    }

                    break;

                case JsonValueKind.Array:
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        foreach (string nested in Flatten(item))
                        {
                            yield return nested;
                        }
                    }

                    break;
            }
        }
    }
}
