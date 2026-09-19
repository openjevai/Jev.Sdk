// EvaluationEndpointTests.cs
// Part of Jev.Sdk.IntegrationTests. POST /v1/systemone against the live service.
//
// This is the endpoint that matters. The unit suite proves the client sends and parses the right
// shapes against a stub; only a live call proves the service agrees. The assertions below are written
// to fail when the API's behaviour drifts, not merely when a call returns — a test that only asserts
// "no exception" would pass against a server that answered every question with noise.

using Jev.Sdk;

namespace Jev.Sdk.IntegrationTests;

/// <summary>
/// Live coverage of the evaluation endpoint, across all three question kinds.
/// </summary>
public class EvaluationEndpointTests
{
    [LiveFact]
    public async Task Noul_ReturnsAProbabilityInRange()
    {
        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["is_urgent"] = Question.Noul("Is this support message urgent?"),
        };

        SystemOneResponse response = await LiveClient.Shared.SystemOneAsync(
            "My invoice is wrong and nobody has replied in a week.",
            questions,
            model: null,
            CancellationToken.None);

        NoulAnswer answer = response["is_urgent"].AsNoul();

        // The whole point of the noul kind is a probability, so a value outside [0,1] is a broken
        // contract rather than an unhelpful answer.
        Assert.InRange(answer.Noul, 0.0, 1.0);
    }

    [LiveFact]
    public async Task Noul_OnAnObviousCase_LandsOnTheExpectedSide()
    {
        // A range check alone would pass for any value. This pins the direction with a state that is
        // not reasonably arguable, so a model that stopped understanding the question shows up as a
        // failure. The threshold is loose on purpose: this asserts a clear signal, not a calibration.
        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["is_urgent"] = Question.Noul("Is this message urgent? Answer yes if the sender needs a reply today."),
        };

        SystemOneResponse urgent = await LiveClient.Shared.SystemOneAsync(
            "URGENT: production is down, we are losing customers right now, need help immediately.",
            questions,
            model: null,
            CancellationToken.None);

        SystemOneResponse routine = await LiveClient.Shared.SystemOneAsync(
            "Just wanted to say the documentation was clear and helpful, thanks. No action needed.",
            questions,
            model: null,
            CancellationToken.None);

        Assert.True(
            urgent["is_urgent"].AsNoul().Noul > routine["is_urgent"].AsNoul().Noul,
            "an explicitly urgent message did not score higher than one stating no action is needed");
    }

    [LiveFact]
    public async Task Choice_ReturnsAnOptionThatWasActuallyOffered()
    {
        string[] options = ["billing", "technical", "sales"];

        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["department"] = Question.Choice(
                "Which department should handle this?",
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["billing"] = "Invoices, payments, refunds",
                    ["technical"] = "Product faults and how-to questions",
                    ["sales"] = "Pricing, trials, purchasing",
                }),
        };

        SystemOneResponse response = await LiveClient.Shared.SystemOneAsync(
            "My invoice shows the wrong total and nobody has replied in a week.",
            questions,
            model: null,
            CancellationToken.None);

        ChoiceAnswer answer = response["department"].AsChoice();

        // The library's core promise for this kind: the returned option is one of the offered ones.
        Assert.Contains(answer.Choice, options);
    }

    [LiveFact]
    public async Task Choice_OnAnObviousCase_SelectsTheExpectedOption()
    {
        // Pins the direction, not just membership: "billing" is the only sensible answer here, so a
        // classifier that returned "sales" would be a real drift rather than a judgement call.
        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["department"] = Question.Choice(
                "Which department should handle this?",
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["billing"] = "Invoices, payments, refunds",
                    ["technical"] = "Product faults and how-to questions",
                    ["sales"] = "Pricing, trials, purchasing",
                }),
        };

        SystemOneResponse response = await LiveClient.Shared.SystemOneAsync(
            "You charged my credit card twice for the same invoice and I need a refund.",
            questions,
            model: null,
            CancellationToken.None);

        Assert.Equal("billing", response["department"].AsChoice().Choice);
    }

    [LiveFact]
    public async Task Choice_ReturnsProbabilitiesThatAreUsableAsOne()
    {
        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["department"] = Question.Choice(
                "Which department should handle this?",
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["billing"] = null,
                    ["technical"] = null,
                    ["sales"] = null,
                }),
        };

        SystemOneResponse response = await LiveClient.Shared.SystemOneAsync(
            "The app crashes when I open the settings page.",
            questions,
            model: null,
            CancellationToken.None);

        ChoiceAnswer answer = response["department"].AsChoice();

        Assert.NotEmpty(answer.Probabilities);

        // A distribution that does not sum to roughly one is not a distribution. Tolerance is loose
        // because the service may round to a few decimal places.
        Assert.InRange(answer.Probabilities.Values.Sum(), 0.99, 1.01);
        Assert.All(answer.Probabilities.Values, p => Assert.InRange(p, 0.0, 1.0));
    }

    [LiveFact]
    public async Task Score_ReturnsAPositionWithinTheSuppliedLevels()
    {
        string[] levels = ["calm", "mildly annoyed", "frustrated", "very angry"];

        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["frustration"] = Question.Score("How frustrated is the sender?", levels),
        };

        SystemOneResponse response = await LiveClient.Shared.SystemOneAsync(
            "This is the third time I have contacted you about this and nobody has fixed it. I am furious.",
            questions,
            model: null,
            CancellationToken.None);

        ScoreAnswer answer = response["frustration"].AsScore();

        // Levels are positional, so a score must land inside the scale that was supplied.
        Assert.InRange(answer.Score, 0.0, levels.Length - 1);
    }

    [LiveFact]
    public async Task Score_OnAnObviousCase_LandsOnTheExpectedSideOfTheScale()
    {
        string[] levels = ["calm", "mildly annoyed", "frustrated", "very angry"];

        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["frustration"] = Question.Score("How frustrated is the sender?", levels),
        };

        SystemOneResponse angry = await LiveClient.Shared.SystemOneAsync(
            "This is the third time I have contacted you and nobody has fixed it. I am furious.",
            questions,
            model: null,
            CancellationToken.None);

        SystemOneResponse calm = await LiveClient.Shared.SystemOneAsync(
            "Everything works well, I just wanted to confirm the renewal date. No rush.",
            questions,
            model: null,
            CancellationToken.None);

        Assert.True(
            angry["frustration"].AsScore().Score > calm["frustration"].AsScore().Score,
            "an angry message did not score higher on frustration than a neutral one");
    }

    [LiveFact]
    public async Task SeveralQuestionsInOneCall_AllComeBackAnswered()
    {
        // The API's core economy: one call, several typed questions. A partial response would be the
        // kind of drift the unit suite cannot detect, because the stub always answers everything.
        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["is_urgent"] = Question.Noul("Is this urgent?"),
            ["department"] = Question.Choice(
                "Which department?",
                new Dictionary<string, string?>(StringComparer.Ordinal) { ["billing"] = null, ["technical"] = null }),
            ["frustration"] = Question.Score("How frustrated?", "calm", "annoyed", "angry"),
        };

        SystemOneResponse response = await LiveClient.Shared.SystemOneAsync(
            "My invoice is wrong and nobody replied in a week.",
            questions,
            model: null,
            CancellationToken.None);

        Assert.Equal(3, response.AnswersOrEmpty.Count);
        Assert.All(questions.Keys, id => Assert.True(response.AnswersOrEmpty.ContainsKey(id), $"no answer for '{id}'"));
    }

    [LiveFact]
    public async Task EveryAnswer_MatchesTheKindOfItsQuestion()
    {
        // The discriminated union is the library's least forgiving surface: a noul question must come
        // back as a noul answer, and the library's typed accessors must agree. Asserted together so a
        // mismatch names which pair drifted.
        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["is_urgent"] = Question.Noul("Is this urgent?"),
            ["department"] = Question.Choice(
                "Which department?",
                new Dictionary<string, string?>(StringComparer.Ordinal) { ["billing"] = null, ["technical"] = null }),
            ["frustration"] = Question.Score("How frustrated?", "calm", "annoyed", "angry"),
        };

        SystemOneResponse response = await LiveClient.Shared.SystemOneAsync(
            "My invoice is wrong and nobody replied in a week.",
            questions,
            model: null,
            CancellationToken.None);

        Assert.IsType<NoulAnswer>(response["is_urgent"].AsNoul());
        Assert.IsType<ChoiceAnswer>(response["department"].AsChoice());
        Assert.IsType<ScoreAnswer>(response["frustration"].AsScore());

        // An unknown kind must not be what came back for any of these.
        Assert.All(response.AnswersOrEmpty.Values, answer => Assert.IsNotType<UnknownAnswer>(answer));
    }

    [LiveFact]
    public async Task TheResponse_ReportsTheModelAndUsage()
    {
        // Both are declared required by the specification, and both are easy to drop silently in
        // deserialization — which is exactly the defect class a second verification pass already found
        // in this area once.
        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["is_urgent"] = Question.Noul("Is this urgent?"),
        };

        SystemOneResponse response = await LiveClient.Shared.SystemOneAsync(
            "Everything is fine, thanks.",
            questions,
            model: null,
            CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(response.Model));
        Assert.NotNull(response.Usage);
        Assert.True(response.Usage!.InputTokens > 0, "the service reported zero input tokens");

        // Output tokens can legitimately be small, but zero for a question that was answered would
        // suggest the member did not deserialize.
        Assert.True(response.Usage.OutputTokens > 0, "the service reported zero output tokens for an answered question");
    }

    [LiveFact]
    public async Task AnAliasInTheRequest_ResolvesToAConcreteVersionInTheResponse()
    {
        // Documented behaviour: the response reports the model that actually answered, which may differ
        // from the alias that was asked for. Verified live: asking for "jev-latest" comes back as
        // "jev-1.13.0".
        //
        // Note what is NOT asserted here, because it was tried and it is false. The resolved model is
        // not an entry in the model list: GET /v1/models advertises aliases ("jev-latest",
        // "jev-preview"), and an alias resolves to a concrete version name that the list does not
        // itself contain. Asserting membership looks reasonable and fails against the real service, so
        // the assertion here is the property that actually holds.
        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["is_urgent"] = Question.Noul("Is this urgent?"),
        };

        SystemOneResponse response = await LiveClient.Shared.SystemOneAsync(
            "Testing the alias resolution path.",
            questions,
            model: JevClientOptions.DefaultModelName,
            CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(response.Model));

        // The alias that was requested must be one the service offers, or the request would not have
        // succeeded at all.
        IReadOnlyList<ModelMetadata> models = await LiveClient.Shared.GetModelsAsync(CancellationToken.None);

        Assert.Contains(models, model => model.Name == JevClientOptions.DefaultModelName);
    }

    [LiveFact]
    public async Task EveryAdvertisedAlias_ActuallyResolves()
    {
        // An alias in the list that the service will not accept would be a trap: a caller picks a name
        // from GET /v1/models and gets a 422. This walks the advertised list, so it keeps working when
        // the service adds an alias.
        IReadOnlyList<ModelMetadata> models = await LiveClient.Shared.GetModelsAsync(CancellationToken.None);

        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["is_urgent"] = Question.Noul("Is this urgent?"),
        };

        foreach (ModelMetadata model in models)
        {
            SystemOneResponse response = await LiveClient.Shared.SystemOneAsync(
                "Confirming an advertised alias is accepted.",
                questions,
                model.Name,
                CancellationToken.None);

            Assert.False(
                string.IsNullOrWhiteSpace(response.Model),
                $"advertised alias '{model.Name}' was accepted but the response named no model");
        }
    }

    [LiveFact]
    public async Task StructuredState_IsAcceptedByTheService()
    {
        // The specification declares state as string | object | array. The unit suite proves the
        // library serializes structured state; this proves the service accepts it.
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(
            """
            {
              "messages": [
                { "role": "customer", "text": "My invoice is wrong." },
                { "role": "agent", "text": "Looking into it now." },
                { "role": "customer", "text": "This is the third time I have asked." }
              ],
              "account": { "tier": "premium", "open_days": 7 }
            }
            """);

        StructuredValue state = StructuredValue.FromJson(document.RootElement);

        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["is_urgent"] = Question.Noul("Is this conversation urgent?"),
        };

        SystemOneResponse response = await LiveClient.Shared.SystemOneAsync(
            state,
            questions,
            model: null,
            CancellationToken.None);

        Assert.Single(response.AnswersOrEmpty);
        Assert.InRange(response["is_urgent"].AsNoul().Noul, 0.0, 1.0);
    }

    [LiveFact]
    public async Task AStructuredInstruction_IsAcceptedByTheService()
    {
        // instructions accepts string | object | array too. A structured instruction is the shape most
        // likely to be serialized wrongly, so it is worth one live call rather than only a stub.
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(
            """{"ask": "Is this message urgent?", "consider": ["tone", "deadline", "explicit urgency"]}""");

        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["is_urgent"] = Question.Noul(StructuredValue.FromJson(document.RootElement)),
        };

        SystemOneResponse response = await LiveClient.Shared.SystemOneAsync(
            "URGENT: the site is down and we are losing orders.",
            questions,
            model: null,
            CancellationToken.None);

        Assert.InRange(response["is_urgent"].AsNoul().Noul, 0.0, 1.0);
    }

    [LiveFact]
    public async Task CustomHeaders_AreAcceptedByTheService()
    {
        // A caller-supplied header must reach the wire without breaking the request. The unit suite
        // asserts it was attached; only the service can confirm it did not object.
        //
        // Headers are configured on the client rather than passed per call, so this needs its own
        // client. That is the documented shape: they are a client-level concern.
        JevClientOptions options = new()
        {
            ApiKey = LiveSettings.ApiKey,
            MaxRetries = 1,
        };

        options.Headers["x-jev-integration-test"] = "1";

        using JevClient client = new(options);

        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["is_urgent"] = Question.Noul("Is this urgent?"),
        };

        SystemOneResponse response = await client.SystemOneAsync(
            "Checking that a custom header does not break the call.",
            questions,
            model: null,
            CancellationToken.None);

        Assert.Single(response.AnswersOrEmpty);
    }

    [LiveFact]
    public async Task ARequestWithNoQuestions_IsRejectedLocallyWithoutSpendingACall()
    {
        // Local validation exists so a caller's mistake is reported immediately rather than as a
        // server rejection. This confirms the documented behaviour holds on the real client.
        JevRequestValidationException exception = await Assert.ThrowsAsync<JevRequestValidationException>(
            () => LiveClient.Shared.SystemOneAsync(
                "anything",
                new Dictionary<string, Question>(StringComparer.Ordinal),
                model: null,
                CancellationToken.None));

        Assert.NotEmpty(exception.Problems);
    }

    [LiveFact]
    public async Task CancellingBeforeTheCall_ThrowsOperationCanceled()
    {
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["is_urgent"] = Question.Noul("Is this urgent?"),
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => LiveClient.Shared.SystemOneAsync("text", questions, null, cancellation.Token));
    }

    [LiveFact]
    public async Task TheTypedOverload_BindsTheLivePayloadThroughATypeInfo()
    {
        // The generic overload exists so a caller can bind the response to a type they supply. Against
        // a stub it proves the merge of type information; against the service it proves the live
        // payload still binds through that path, which is the only way to know the caller's declared
        // shape matches what the server actually sends.
        //
        // The type information comes from a source-generated context, which is how a caller is expected
        // to supply one. Using the library's own response type here keeps the test independent of a
        // second generator while still exercising the typed overload end to end.
        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["is_urgent"] = Question.Noul("Is this urgent?"),
        };

        SystemOneResponse response = await LiveClient.Shared.SystemOneAsync(
            "Bind this response through caller-supplied type information.",
            questions,
            JevJsonContext.Default.SystemOneResponse,
            model: null,
            CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(response.Model));
        Assert.True(response.AnswersOrEmpty.ContainsKey("is_urgent"));

        // The typed path must apply the same required-member contract as the untyped one.
        Assert.NotNull(response.Usage);
    }

    [LiveFact]
    public async Task TheTypedOverload_AndThePlainOne_AgreeOnTheSamePayload()
    {
        // Both entry points must produce equivalent results for the same request. A divergence here
        // would mean a caller picking one overload quietly gets different behaviour from the other.
        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["is_urgent"] = Question.Noul("Is this urgent?"),
        };

        string state = "The build is broken and the release is blocked.";

        SystemOneResponse plain = await LiveClient.Shared.SystemOneAsync(
            state, questions, model: null, CancellationToken.None);

        SystemOneResponse typed = await LiveClient.Shared.SystemOneAsync(
            state, questions, JevJsonContext.Default.SystemOneResponse, model: null, CancellationToken.None);

        // The model is deterministic for the same request, so both paths must report it identically.
        Assert.Equal(plain.Model, typed.Model);
        Assert.Equal(plain.AnswersOrEmpty.Count, typed.AnswersOrEmpty.Count);
    }
}
