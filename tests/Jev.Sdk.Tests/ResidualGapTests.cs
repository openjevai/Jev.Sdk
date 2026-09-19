// ResidualGapTests.cs
// Part of Jev.Sdk.Tests. The final reachable branches:
//
//   - FromObject across every JSON shape, not only objects
//   - the StructuredValue-typed Score factory overload (the string overload shadows it for the
//     common call, so it takes an explicit call to reach)
//   - the single-argument EnvironmentApiKeyProvider constructor
//   - the union converters' converter-list handling, which only runs when a caller has registered
//     the converter explicitly rather than relying on the attribute
//
// Two branches in the transport are deliberately left uncovered and are documented as such in the
// requirements: a null Content on a response, and a Retry-After header carrying neither of its two
// forms. Neither is reachable through HttpClient's documented behaviour, and both are cheap guards
// against the framework changing underneath the library.

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Jev.Sdk;

namespace Jev.Sdk.Tests;

public class ResidualGapTests
{
    private static readonly string[] s_strings = ["a", "b"];

    /// <summary>
    /// Options with both union converters registered explicitly, built once.
    /// </summary>
    private static readonly JsonSerializerOptions s_unionOptions = BuildUnionOptions();

    [Fact]
    public void FromObject_DetectsAnArrayShape()
    {
        StructuredValue value = StructuredValue.FromObject(s_strings, ArrayContext.Default.StringArray);

        Assert.Equal(StructuredValue.JsonShape.Sequence, value.Shape);
        Assert.Equal(2, value.Element.GetArrayLength());
    }

    [Fact]
    public void FromObject_DetectsAScalarShape()
    {
        StructuredValue number = StructuredValue.FromObject(42, ScalarContext.Default.Int32);
        Assert.Equal(StructuredValue.JsonShape.Number, number.Shape);

        StructuredValue text = StructuredValue.FromObject("hello", ScalarContext.Default.String);
        Assert.Equal(StructuredValue.JsonShape.Text, text.Shape);

        StructuredValue flag = StructuredValue.FromObject(true, ScalarContext.Default.Boolean);
        Assert.Equal(StructuredValue.JsonShape.Flag, flag.Shape);
    }

    [Fact]
    public void FromObject_RejectsNullTypeInfo()
    {
        Assert.Throws<ArgumentNullException>(
            () => StructuredValue.FromObject("x", (JsonTypeInfo<string>)null!));
    }

    [Fact]
    public void ScoreFactory_StructuredValuesOverloadIsUsable()
    {
        // The string overload shadows this one for the obvious call, so reaching it takes an
        // explicit StructuredValue call. It exists so a caller with structured level descriptions
        // is not forced through text.
        ScoreQuestion question = Question.Score(
            StructuredValue.FromString("How severe?"),
            StructuredValue.FromString("Low"),
            StructuredValue.FromString("High"));

        Assert.Equal(2, question.Criteria.Count);
        Assert.Equal("Low", question.Criteria[0].AsString());
    }

    [Fact]
    public void EnvironmentApiKeyProvider_AcceptsJustAVariableName()
    {
        EnvironmentApiKeyProvider provider = new("JEV_TEST_ABSENT_VARIABLE");

        Assert.Equal("JEV_TEST_ABSENT_VARIABLE", provider.VariableName);
    }

    [Fact]
    public void AnswerConverter_HandlesBeingRegisteredExplicitly()
    {
        // A caller who registers the converter themselves must still get a correct payload. This
        // exercises the converter-list handling that the attribute-driven path skips: the value
        // must be declared as the base type, or the serializer resolves the concrete type's own
        // metadata and never consults this converter at all.
        Answer answer = new NoulAnswer { Noul = 0.75 };
        string written = JsonSerializer.Serialize(answer, s_unionOptions);

        using JsonDocument document = JsonDocument.Parse(written);
        Assert.Equal("noul", document.RootElement.GetProperty("type").GetString());
        Assert.Equal(0.75, document.RootElement.GetProperty("noul").GetDouble(), 4);
    }

    [Fact]
    public void QuestionConverter_HandlesBeingRegisteredExplicitly()
    {
        Question question = Question.Score("rate", "low", "high");
        string written = JsonSerializer.Serialize(question, s_unionOptions);

        using JsonDocument document = JsonDocument.Parse(written);
        Assert.Equal("score", document.RootElement.GetProperty("type").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("criteria").GetArrayLength());
    }

    [Fact]
    public void UnionConverter_ReadsThroughExplicitRegistration()
    {
        Question question = JsonSerializer.Deserialize<Question>(
            """{"type":"choice","instructions":"pick","criteria":{"a":null}}""",
            s_unionOptions)!;

        ChoiceQuestion choice = Assert.IsType<ChoiceQuestion>(question);
        Assert.Equal("pick", choice.Instructions!.AsString());
    }

    private static JsonSerializerOptions BuildUnionOptions()
    {
        JsonSerializerOptions options = new()
        {
            TypeInfoResolver = JevJsonContext.Default,
        };

        options.Converters.Add(new AnswerJsonConverter());
        options.Converters.Add(new QuestionJsonConverter());
        options.MakeReadOnly();

        return options;
    }
}

/// <summary>Source-generated metadata for an array, used to reach the sequence shape detection.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(string[]))]
internal sealed partial class ArrayContext : JsonSerializerContext
{
}

/// <summary>Source-generated metadata for scalar types, used to reach the scalar shape detection.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
internal sealed partial class ScalarContext : JsonSerializerContext
{
}
