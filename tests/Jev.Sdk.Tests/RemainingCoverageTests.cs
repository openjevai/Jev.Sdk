// RemainingCoverageTests.cs
// Part of Jev.Sdk.Tests. The last reachable paths: the parameterless exception constructors a
// serializer may call, the exception constructor that preserves an inner cause, the error-detail
// members the server may or may not send, and the union converter's guard clauses.

using System.Text.Json;
using Jev.Sdk;

namespace Jev.Sdk.Tests;

public class RemainingCoverageTests
{
    [Fact]
    public void ParameterlessExceptionConstructors_Exist()
    {
        // These are present for serializers and other infrastructure that construct exceptions
        // reflectively. They must not throw.
        Assert.NotNull(new JevException());
        Assert.NotNull(new JevConfigurationException());
        Assert.NotNull(new JevRequestValidationException("message"));
    }

    [Fact]
    public void ApiException_PreservesAnInnerCause()
    {
        InvalidOperationException cause = new("the real reason");

        JevApiException exception = new(
            System.Net.HttpStatusCode.BadGateway,
            "wrapped",
            "body",
            cause);

        Assert.Same(cause, exception.InnerException);
        Assert.Equal("body", exception.ResponseBody);
    }

    [Fact]
    public void ConnectionException_WithoutACauseHasNoInnerException()
    {
        JevConnectionException exception = new("bare failure");

        // The single-argument overload must not manufacture a placeholder inner exception,
        // because a caller inspecting InnerException would be misled by one.
        Assert.Null(exception.InnerException);
        Assert.False(exception.IsProtocolError);
    }

    [Fact]
    public void ErrorDetails_ReadsTheOptionalInputAndContext()
    {
        ErrorDetails detail = JsonSerializer.Deserialize<ErrorDetails>(
            """
            {
              "loc": ["body", "questions"],
              "msg": "Field required",
              "type": "missing",
              "input": { "type": "score" },
              "ctx": { "min_length": 1 }
            }
            """,
            JevJsonContext.Default.Options)!;

        Assert.NotNull(detail.Input);
        Assert.NotNull(detail.Context);
        Assert.Equal("score", detail.Input!.Element.GetProperty("type").GetString());
        Assert.Equal(1, detail.Context!.Element.GetProperty("min_length").GetInt32());
    }

    [Fact]
    public void ErrorDetails_LeavesInputAndContextNullWhenAbsent()
    {
        // Only loc, msg, and type are required, so the optional members must stay absent rather
        // than default to an empty JSON value that a caller cannot distinguish from real data.
        ErrorDetails detail = JsonSerializer.Deserialize<ErrorDetails>(
            """{"loc":["body"],"msg":"bad","type":"missing"}""",
            JevJsonContext.Default.Options)!;

        Assert.Null(detail.Input);
        Assert.Null(detail.Context);
    }

    [Fact]
    public void QuestionConverter_RejectsANonObject()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<Question>("""["not","a","question"]""", JevJsonContext.Default.Options));
    }

    [Fact]
    public void QuestionConverter_RejectsAMissingType()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<Question>("""{"instructions":"no type here"}""", JevJsonContext.Default.Options));
    }

    [Fact]
    public void QuestionConverter_RejectsANonStringType()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<Question>("""{"type":42,"instructions":"numeric type"}""", JevJsonContext.Default.Options));
    }

    [Fact]
    public void AnswerConverter_RejectsANonStringType()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<Answer>("""{"type":true,"noul":0.5}""", JevJsonContext.Default.Options));
    }

    [Fact]
    public void AnswerConverter_RemovesBothUnionConvertersSoAUnionCanNestAnother()
    {
        // A payload whose answers include a nested union-shaped member exercises the converter
        // list manipulation. Without it, reading a concrete kind would re-enter the union
        // converter and recurse.
        SystemOneResponse response = JsonSerializer.Deserialize<SystemOneResponse>(
            """
            {
              "model": "jev-latest",
              "answers": {
                "a": { "type": "noul", "noul": 0.1 },
                "b": { "type": "choice", "choice": "x",
                       "probabilities": { "x": 1.0 }, "confidence": 1.0 },
                "c": { "type": "score", "score": 0.5,
                       "legend": { "0": "low", "1": "high" },
                       "probabilities": { "0": 0.5, "1": 0.5 }, "confidence": 0.4 },
                "d": { "type": "future_kind", "payload": [1, 2, 3] }
              },
              "usage": { "input_tokens": 1, "output_tokens": 1 }
            }
            """,
            JevJsonContext.Default.Options)!;

        Assert.IsType<NoulAnswer>(response["a"]);
        Assert.IsType<ChoiceAnswer>(response["b"]);
        Assert.IsType<ScoreAnswer>(response["c"]);
        Assert.IsType<UnknownAnswer>(response["d"]);

        // Re-serializing the whole response must survive the unmodelled answer.
        string written = JsonSerializer.Serialize(response, JevJsonContext.Default.Options);
        Assert.Contains("future_kind", written, StringComparison.Ordinal);
        Assert.Contains("payload", written, StringComparison.Ordinal);
    }

    [Fact]
    public void StructuredValue_NullIsASharedInstance()
    {
        // Null is one shared instance, so a caller can compare against it cheaply and every
        // route to a null value produces the same object.
        Assert.Same(StructuredValue.Null, StructuredValue.FromString(null));
        Assert.Same(StructuredValue.Null, StructuredValue.FromObject<CallerState>(null, CallerStateContext.Default.CallerState));
        Assert.Same(StructuredValue.Null, StructuredValue.FromJson(JsonDocument.Parse("null").RootElement));
    }

    [Fact]
    public void StructuredValue_ElementIsAvailableForEveryShape()
    {
        StructuredValue value = StructuredValue.FromJson(JsonDocument.Parse("""{"k":"v"}""").RootElement);

        Assert.Equal(JsonValueKind.Object, value.Element.ValueKind);
    }

    [Fact]
    public void EnvironmentProvider_ExposesItsVariableNameForAnExplicitName()
    {
        EnvironmentApiKeyProvider provider = new("MY_KEY", _ => null);

        Assert.Equal("MY_KEY", provider.VariableName);
    }
}
