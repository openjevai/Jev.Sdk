// PublicApiTests.cs
// Part of Jev.Sdk.Tests. Exercises the parts of the public surface a caller reaches for but the
// happy-path tests do not: the fluent question factories, the structured-value shapes, the
// convenience readers, and the exception hierarchy's contract.
//
// These are not busywork. The factories are the documented way to build questions, and a caller
// who uses them rather than the constructors is on a different code path.

using System.Text.Json;
using Jev.Sdk;

namespace Jev.Sdk.Tests;

public class PublicApiTests
{
    [Fact]
    public void NoulFactory_BuildsAQuestion()
    {
        NoulQuestion question = Question.Noul("Does this convey urgency?");

        Assert.Equal(QuestionTypes.Noul, question.Type);
        Assert.Equal("Does this convey urgency?", question.Instructions!.AsString());
        Assert.Null(question.Criteria);
    }

    [Fact]
    public void NoulFactory_AcceptsCriteria()
    {
        NoulQuestion question = Question.Noul(
            StructuredValue.FromString("Is this spam?"),
            new NoulCriteria
            {
                True = StructuredValue.FromString("Unsolicited advertising"),
                False = StructuredValue.FromString("A legitimate conversation"),
            });

        Assert.Equal("Unsolicited advertising", question.Criteria!.True!.AsString());
        Assert.Equal("A legitimate conversation", question.Criteria.False!.AsString());
    }

    [Fact]
    public void ChoiceFactory_BuildsOptionsFromText()
    {
        ChoiceQuestion question = Question.Choice(
            "Which team should handle this?",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["billing"] = "Payments, invoicing, refunds",
                ["technical"] = null,
            });

        Assert.Equal(QuestionTypes.Choice, question.Type);
        Assert.Equal(2, question.Criteria.Count);

        // A null description means the option is judged by its name alone, which is a supported
        // case rather than a mistake.
        Assert.True(question.Criteria["technical"]!.IsNull);
        Assert.Equal("Payments, invoicing, refunds", question.Criteria["billing"]!.AsString());
    }

    [Fact]
    public void ChoiceFactory_AcceptsStructuredCriteria()
    {
        ChoiceQuestion question = Question.Choice(
            StructuredValue.FromString("Classify"),
            new Dictionary<string, StructuredValue?>(StringComparer.Ordinal)
            {
                ["a"] = StructuredValue.FromString("first"),
            });

        Assert.Equal("first", question.Criteria["a"]!.AsString());
    }

    [Fact]
    public void ScoreFactory_BuildsLevelsFromText()
    {
        ScoreQuestion question = Question.Score("How frustrated is the customer?", "Calm", "Frustrated", "Very angry");

        Assert.Equal(QuestionTypes.Score, question.Type);
        Assert.Equal(3, question.Criteria.Count);
        Assert.Equal("Calm", question.Criteria[0].AsString());
        Assert.Equal("Very angry", question.Criteria[2].AsString());
    }

    [Fact]
    public void ScoreFactory_AcceptsASequence()
    {
        IEnumerable<StructuredValue> levels =
        [
            StructuredValue.FromString("low"),
            StructuredValue.FromString("high"),
        ];

        ScoreQuestion question = Question.Score(StructuredValue.FromString("rate"), levels);

        Assert.Equal(2, question.Criteria.Count);
    }

    [Fact]
    public void StructuredValue_FromStringPreservesShape()
    {
        StructuredValue value = StructuredValue.FromString("hello");

        Assert.Equal(StructuredValue.JsonShape.Text, value.Shape);
        Assert.Equal("hello", value.AsString());
        Assert.False(value.IsNull);

        // The wrapper's own null check is what stops a blank instruction from being treated as
        // an absent one downstream.
        Assert.Null(StructuredValue.FromString(null).AsString());
        Assert.True(StructuredValue.FromString(null).IsNull);
    }

    [Fact]
    public void StructuredValue_ImplicitConversionFromText()
    {
        StructuredValue value = "implicit";

        Assert.Equal("implicit", value.AsString());
    }

    [Fact]
    public void StructuredValue_RoundTripsThroughJson()
    {
        StructuredValue value = StructuredValue.FromJson(
            JsonDocument.Parse("""{"nested":{"deep":[1,2,3]}}""").RootElement);

        string written = JsonSerializer.Serialize(value, JevJsonContext.Default.Options);

        using JsonDocument document = JsonDocument.Parse(written);
        Assert.Equal(3, document.RootElement.GetProperty("nested").GetProperty("deep").GetArrayLength());
    }

    [Fact]
    public void StructuredValue_NullRoundTripsAsJsonNull()
    {
        string written = JsonSerializer.Serialize(StructuredValue.Null, JevJsonContext.Default.Options);

        Assert.Equal("null", written);
    }

    [Fact]
    public void StructuredValue_DeserializesFromJson()
    {
        StructuredValue value = JsonSerializer.Deserialize<StructuredValue>(
            """{"a":1}""", JevJsonContext.Default.Options)!;

        Assert.Equal(StructuredValue.JsonShape.Record, value.Shape);
    }

    [Fact]
    public void StructuredValue_FromObjectUsesTheSuppliedTypeInfo()
    {
        StructuredValue value = StructuredValue.FromObject(
            new CallerState { Subject = "s", Message = "m" },
            CallerStateContext.Default.CallerState);

        Assert.Equal(StructuredValue.JsonShape.Record, value.Shape);
        using JsonDocument document = JsonDocument.Parse(value.ToRawText());
        Assert.Equal("s", document.RootElement.GetProperty("subject").GetString());
    }

    [Fact]
    public void StructuredValue_FromObjectWithNullIsNull()
    {
        StructuredValue value = StructuredValue.FromObject<CallerState>(null, CallerStateContext.Default.CallerState);

        Assert.True(value.IsNull);
    }

    [Fact]
    public void StructuredValue_ToStringReturnsRawText()
    {
        StructuredValue value = StructuredValue.FromString("text");

        Assert.Equal("\"text\"", value.ToString());
        Assert.Equal(string.Empty, StructuredValue.Null.ToString());
    }

    [Fact]
    public void ScoreAnswer_NearestLevelDescriptionFindsTheClosestLevel()
    {
        ScoreAnswer answer = new()
        {
            Score = 1.6,
            Legend = new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["0"] = StructuredValue.FromString("Calm"),
                ["1"] = StructuredValue.FromString("Frustrated"),
                ["2"] = StructuredValue.FromString("Very angry"),
            },
        };

        // 1.6 is nearer 2 than 1, so the nearest description is the angriest one. This is a
        // display convenience; the score itself is untouched.
        Assert.Equal("Very angry", answer.NearestLevelDescription()!.AsString());
    }

    [Fact]
    public void ScoreAnswer_NearestLevelDescriptionHandlesAnEmptyLegend()
    {
        ScoreAnswer answer = new() { Score = 1.0 };

        Assert.Null(answer.NearestLevelDescription());
    }

    [Fact]
    public void ScoreAnswer_NearestLevelDescriptionIgnoresNonNumericKeys()
    {
        ScoreAnswer answer = new()
        {
            Score = 0,
            Legend = new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["not-a-number"] = StructuredValue.FromString("ignored"),
                ["0"] = StructuredValue.FromString("zero"),
            },
        };

        Assert.Equal("zero", answer.NearestLevelDescription()!.AsString());
    }

    [Fact]
    public void Usage_ReportsATotal()
    {
        Usage usage = new() { InputTokens = 312, OutputTokens = 48 };

        Assert.Equal(360, usage.TotalTokens);
    }

    [Fact]
    public void ModelMetadata_ParsesAWellFormedDate()
    {
        ModelMetadata model = new() { ReleaseDate = "2026-09-15" };

        Assert.Equal(new DateOnly(2026, 9, 15), model.ParsedReleaseDate);
    }

    [Fact]
    public void ModelMetadata_TolerateAMalformedDate()
    {
        // The API reports the date as a string, so a caller must not be given a parsing failure
        // for a value the library does not control.
        ModelMetadata model = new() { ReleaseDate = "sometime in September" };

        Assert.Null(model.ParsedReleaseDate);
    }

    [Fact]
    public void ErrorDetails_RendersADottedPath()
    {
        ErrorDetails detail = new()
        {
            Location = ["body", "questions", "urgency", "score", "criteria"],
            Message = "Field required",
            ErrorType = "missing",
        };

        Assert.Equal("body.questions.urgency.score.criteria", detail.LocationPath);
        Assert.Contains("Field required", detail.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void JsonElementBox_RoundTripsArbitraryJson()
    {
        JsonElementBox box = new(JsonDocument.Parse("""{"any":"shape"}""").RootElement);

        string written = JsonSerializer.Serialize(box, JevJsonContext.Default.Options);

        Assert.Contains("any", written, StringComparison.Ordinal);
        Assert.Contains("shape", box.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void JsonElementBox_Deserializes()
    {
        JsonElementBox box = JsonSerializer.Deserialize<JsonElementBox>("[1,2]", JevJsonContext.Default.Options)!;

        Assert.Equal(JsonValueKind.Array, box.Element.ValueKind);
    }

    [Fact]
    public void ExceptionHierarchy_IsCatcheableAtTheRoot()
    {
        // One catch for everything the library raises is the point of a single root type. Each
        // specific type must therefore sit beneath it.
        Assert.IsAssignableFrom<JevException>(new JevConfigurationException("c"));
        Assert.IsAssignableFrom<JevException>(new JevRequestValidationException("v"));
        Assert.IsAssignableFrom<JevException>(new JevConnectionException("x"));
        Assert.IsAssignableFrom<JevApiException>(new JevAuthenticationException("a"));
        Assert.IsAssignableFrom<JevApiException>(new JevRateLimitException("r"));
        Assert.IsAssignableFrom<JevApiException>(new JevOverloadedException("o"));
        Assert.IsAssignableFrom<JevApiException>(new JevValidationException("v", []));
    }

    [Fact]
    public void ExceptionConstructors_PreserveTheirArguments()
    {
        JevApiException apiException = new(
            System.Net.HttpStatusCode.BadGateway,
            "gateway said no",
            "raw body");

        Assert.Equal(System.Net.HttpStatusCode.BadGateway, apiException.StatusCode);
        Assert.Equal("raw body", apiException.ResponseBody);

        JevRateLimitException rateLimit = new("slow down", TimeSpan.FromSeconds(3), "body");
        Assert.Equal(TimeSpan.FromSeconds(3), rateLimit.RetryAfter);

        JevOverloadedException overloaded = new("busy", TimeSpan.FromSeconds(9));
        Assert.Equal(TimeSpan.FromSeconds(9), overloaded.RetryAfter);

        JevConnectionException protocol = new("unreadable", isProtocolError: true, responseBody: "raw");
        Assert.True(protocol.IsProtocolError);
        Assert.Equal("raw", protocol.ResponseBody);

        JevConfigurationException configuration = new("bad", new InvalidOperationException("cause"));
        Assert.IsType<InvalidOperationException>(configuration.InnerException);
    }

    [Fact]
    public void AnswerReaders_ThrowWhenTheKindIsWrong()
    {
        NoulAnswer noul = new() { Noul = 0.5 };

        Assert.Throws<InvalidCastException>(() => noul.AsChoice());
        Assert.Throws<InvalidCastException>(() => noul.AsScore());
        Assert.Same(noul, noul.AsNoul());
    }

    [Fact]
    public void DefaultOptions_AreTheDocumentedDefaults()
    {
        JevClientOptions options = new();

        Assert.Equal("https://api.typesafe.ai/v1/", options.BaseAddress.AbsoluteUri);
        Assert.Equal("jev-latest", options.DefaultModel);
        Assert.Equal(2, options.MaxRetries);
        Assert.Equal(10, options.Timeout.TotalSeconds);
        Assert.Equal(500, options.InitialRetryDelay.TotalMilliseconds);
        Assert.Equal(2.0, options.RetryBackoffMultiplier);
        Assert.Equal(5, options.MaxRetryDelay.TotalSeconds);
        Assert.Equal(60, options.MaxRetryAfter.TotalSeconds);
        Assert.Equal(0.25, options.RetryJitterFraction);
        Assert.True(options.ValidateRequests);
        Assert.Null(options.ConfigureJson);
        Assert.Null(options.ApiKey);
    }

    [Theory]
    [InlineData("https://api.test/v1/", "https://api.test/v1/systemone")]
    [InlineData("https://api.test/v1", "https://api.test/v1/systemone")]
    public void BuildUri_NormalisesAMissingTrailingSlash(string baseAddress, string expected)
    {
        // A base address without a trailing slash is a normal thing for a caller to supply, and
        // without normalisation the last path segment would be replaced instead of extended.
        JevClientOptions options = new() { BaseAddress = new Uri(baseAddress) };

        Assert.Equal(expected, options.BuildUri("systemone").AbsoluteUri);
    }

    [Fact]
    public void Snapshot_RejectsAnInvalidBaseAddressScheme()
    {
        JevClientOptions options = new() { BaseAddress = new Uri("ftp://api.test/v1/") };

        Assert.Throws<JevConfigurationException>(() => new JevClient(options, new StubTransport(), new StaticApiKeyProvider("k"), null));
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 1)]
    public void Snapshot_RejectsInvalidNumericOptions(double timeoutSeconds, int maxRetries, double multiplier)
    {
        JevClientOptions options = new()
        {
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
            MaxRetries = maxRetries,
            RetryBackoffMultiplier = multiplier,
        };

        Assert.Throws<JevConfigurationException>(
            () => new JevClient(options, new StubTransport(), new StaticApiKeyProvider("k"), null));
    }

    [Fact]
    public void Snapshot_RejectsBlankModelName()
    {
        JevClientOptions options = new() { DefaultModel = "  " };

        Assert.Throws<JevConfigurationException>(
            () => new JevClient(options, new StubTransport(), new StaticApiKeyProvider("k"), null));
    }

    [Fact]
    public void Snapshot_RejectsANegativeMaxRetryDelay()
    {
        JevClientOptions options = new() { MaxRetryDelay = TimeSpan.FromSeconds(-1) };

        Assert.Throws<JevConfigurationException>(
            () => new JevClient(options, new StubTransport(), new StaticApiKeyProvider("k"), null));
    }

    [Fact]
    public void ClientConstructor_OverloadsProduceAUsableClient()
    {
        using JevClient fromKey = new("some-key");
        using JevClient fromOptions = new(new JevClientOptions { ApiKey = "k" });
        using JevClient defaults = new();

        Assert.Equal("jev-latest", fromKey.DefaultModel);
        Assert.Equal("jev-latest", fromOptions.DefaultModel);
        Assert.Equal("jev-latest", defaults.DefaultModel);
        Assert.Equal(2, defaults.MaxRetries);
    }

    [Fact]
    public void ClientExposesItsResolvedSettings()
    {
        using JevClient client = new(new JevClientOptions
        {
            ApiKey = "k",
            DefaultModel = "custom",
            BaseAddress = new Uri("https://example.test/v1/"),
            MaxRetries = 5,
        });

        Assert.Equal("custom", client.DefaultModel);
        Assert.Equal(new Uri("https://example.test/v1/"), client.BaseAddress);
        Assert.Equal(5, client.MaxRetries);
    }

    [Fact]
    public void Dispose_IsIdempotentOnTheClient()
    {
        StubTransport transport = new StubTransport().EnqueueJson("{}");
        JevClient client = TestClient.Create(transport);

        client.Dispose();
        client.Dispose();
    }

    [Fact]
    public async Task NoApiKey_ThrowsConfigurationExceptionNamingTheSources()
    {
        // The error must name every place a key could have come from, because the caller's next
        // action is to put one there.
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());

        using JevClient client = new(
            new JevClientOptions(),
            transport,
            new EnvironmentApiKeyProvider("DEFINITELY_ABSENT_VARIABLE", _ => null),
            logger: null);

        JevConfigurationException exception = await Assert.ThrowsAsync<JevConfigurationException>(
            () => client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None));

        Assert.Contains("TYPESAFE_API_KEY", exception.Message, StringComparison.Ordinal);
        Assert.Contains("appSettings", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, transport.RequestCount);
    }

    [Fact]
    public void Environment_DeclaresTheDocumentedNames()
    {
        Assert.Equal("TYPESAFE_API_KEY", JevEnvironment.ApiKeyVariable);
        Assert.Equal("appSettings.json", JevEnvironment.GenericSettingsFile);
        Assert.Equal("appSettings.{0}.json", JevEnvironment.MachineSettingsFileFormat);
        Assert.Equal("MACHINE_NAME", JevEnvironment.MachineNameVariable);
        Assert.Equal("Jev", JevEnvironment.ConfigurationSection);
        Assert.Equal("ApiKey", JevEnvironment.ApiKeyKey);
    }

    [Fact]
    public void QuestionTypes_DeclareTheWireValues()
    {
        Assert.Equal("noul", QuestionTypes.Noul);
        Assert.Equal("choice", QuestionTypes.Choice);
        Assert.Equal("score", QuestionTypes.Score);
    }
}
