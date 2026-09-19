// ValidationTests.cs
// Part of Jev.Sdk.Tests. Local validation must catch a caller's mistake before any network call,
// and must report every problem at once rather than one per attempt.

using Jev.Sdk;

namespace Jev.Sdk.Tests;

public class ValidationTests
{
    [Fact]
    public async Task EmptyQuestionMap_IsRejected()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        JevRequestValidationException exception = await Assert.ThrowsAsync<JevRequestValidationException>(
            () => client.SystemOneAsync("text", new Dictionary<string, Question>(), model: null, CancellationToken.None));

        Assert.Contains(exception.Problems, p => p.Contains("At least one question", StringComparison.Ordinal));
        Assert.Equal(0, transport.RequestCount);
    }

    [Fact]
    public async Task NoulWithoutInstructions_IsRejected()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["q"] = new NoulQuestion(),
        };

        JevRequestValidationException exception = await Assert.ThrowsAsync<JevRequestValidationException>(
            () => client.SystemOneAsync("text", questions, model: null, CancellationToken.None));

        Assert.Contains(exception.Problems, p => p.Contains("needs instructions", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScoreWithOneLevel_IsRejected()
    {
        // The server accepts a single level, because the specification sets a minimum of one,
        // but a one-level scale returns a constant, so it is always a caller's mistake.
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["q"] = Question.Score("rate this", "only level"),
        };

        JevRequestValidationException exception = await Assert.ThrowsAsync<JevRequestValidationException>(
            () => client.SystemOneAsync("text", questions, model: null, CancellationToken.None));

        Assert.Contains(exception.Problems, p => p.Contains("at least two levels", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ChoiceWithNoOptions_IsRejected()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["q"] = new ChoiceQuestion { Instructions = StructuredValue.FromString("pick one") },
        };

        JevRequestValidationException exception = await Assert.ThrowsAsync<JevRequestValidationException>(
            () => client.SystemOneAsync("text", questions, model: null, CancellationToken.None));

        Assert.Contains(exception.Problems, p => p.Contains("at least one option", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ChoiceWithEmptyOptionName_IsRejected()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["q"] = Question.Choice(
                "pick",
                new Dictionary<string, string?>(StringComparer.Ordinal) { ["  "] = "desc" }),
        };

        JevRequestValidationException exception = await Assert.ThrowsAsync<JevRequestValidationException>(
            () => client.SystemOneAsync("text", questions, model: null, CancellationToken.None));

        Assert.Contains(exception.Problems, p => p.Contains("empty name", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NullQuestion_IsRejected()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["q"] = null!,
        };

        JevRequestValidationException exception = await Assert.ThrowsAsync<JevRequestValidationException>(
            () => client.SystemOneAsync("text", questions, model: null, CancellationToken.None));

        Assert.Contains(exception.Problems, p => p.Contains("is null", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EmptyQuestionId_IsRejected()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["   "] = Question.Noul("is it?"),
        };

        JevRequestValidationException exception = await Assert.ThrowsAsync<JevRequestValidationException>(
            () => client.SystemOneAsync("text", questions, model: null, CancellationToken.None));

        Assert.Contains(exception.Problems, p => p.Contains("cannot be null", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RawQuestionCannotBeSent_AndSaysWhy()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse("{}");

        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["q"] = new RawQuestion("ranking", document.RootElement),
        };

        JevRequestValidationException exception = await Assert.ThrowsAsync<JevRequestValidationException>(
            () => client.SystemOneAsync("text", questions, model: null, CancellationToken.None));

        Assert.Contains(exception.Problems, p => p.Contains("raw question", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NullState_IsRejected()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        JevRequestValidationException exception = await Assert.ThrowsAsync<JevRequestValidationException>(
            () => client.SystemOneAsync(StructuredValue.Null, TestClient.ThreeQuestions(), model: null, CancellationToken.None));

        Assert.Contains(exception.Problems, p => p.Contains("state is required", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MultipleProblems_AreAllReportedAtOnce()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["a"] = new NoulQuestion(),
            ["b"] = new ChoiceQuestion(),
            ["c"] = Question.Score("rate", "single"),
        };

        JevRequestValidationException exception = await Assert.ThrowsAsync<JevRequestValidationException>(
            () => client.SystemOneAsync("text", questions, model: null, CancellationToken.None));

        // One round trip of feedback rather than three.
        Assert.True(exception.Problems.Count >= 3);
        Assert.Contains("a", exception.Message, StringComparison.Ordinal);
        Assert.Contains("b", exception.Message, StringComparison.Ordinal);
        Assert.Contains("c", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidationCanBeDisabled()
    {
        // Turning validation off trades a fast, clear error for a slower, less specific one,
        // which is occasionally what a caller wants when tracking down a server-side rejection.
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport, o => o.ValidateRequests = false);

        await client.SystemOneAsync("text", new Dictionary<string, Question>(), model: null, CancellationToken.None);

        Assert.Equal(1, transport.RequestCount);
    }

    [Fact]
    public void ValidationExceptionListsEveryProblem()
    {
        JevRequestValidationException exception = new(["first", "second"]);

        Assert.Equal(2, exception.Problems.Count);
        Assert.Contains("first", exception.Message, StringComparison.Ordinal);
        Assert.Contains("second", exception.Message, StringComparison.Ordinal);
    }
}
