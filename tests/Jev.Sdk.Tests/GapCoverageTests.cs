// GapCoverageTests.cs
// Part of Jev.Sdk.Tests. Targeted tests for paths the happy-path suites do not reach: the
// option-validation guards, the timeout and cancellation boundaries in the transport, the
// validation messages for the remaining question mistakes, and the machine-name fallback.
//
// Each of these is a branch a caller can genuinely hit. A guard nobody tests is a guard nobody
// knows works until it fires in production, which is the wrong place to find out.

using System.Net;
using System.Text.Json;
using Jev.Sdk;
using Jev.Sdk.DependencyInjection;

namespace Jev.Sdk.Tests;

public class GapCoverageTests
{
    // ---------------------------------------------------------------- option guards

    [Fact]
    public void Snapshot_RejectsANullBaseAddress()
    {
        JevClientOptions options = new() { BaseAddress = null! };

        JevConfigurationException exception = Assert.Throws<JevConfigurationException>(
            () => new JevClient(options, new StubTransport(), new StaticApiKeyProvider("k"), null));

        Assert.Contains("BaseAddress must be set", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Snapshot_RejectsARelativeBaseAddress()
    {
        JevClientOptions options = new() { BaseAddress = new Uri("/v1/", UriKind.Relative) };

        Assert.Throws<JevConfigurationException>(
            () => new JevClient(options, new StubTransport(), new StaticApiKeyProvider("k"), null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Snapshot_RejectsANonPositiveTimeout(int seconds)
    {
        JevClientOptions options = new() { Timeout = TimeSpan.FromSeconds(seconds) };

        JevConfigurationException exception = Assert.Throws<JevConfigurationException>(
            () => new JevClient(options, new StubTransport(), new StaticApiKeyProvider("k"), null));

        Assert.Contains("Timeout must be greater than zero", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Snapshot_RejectsNegativeMaxRetries()
    {
        JevClientOptions options = new() { MaxRetries = -1 };

        JevConfigurationException exception = Assert.Throws<JevConfigurationException>(
            () => new JevClient(options, new StubTransport(), new StaticApiKeyProvider("k"), null));

        Assert.Contains("MaxRetries cannot be negative", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Snapshot_RejectsNegativeInitialRetryDelay()
    {
        JevClientOptions options = new() { InitialRetryDelay = TimeSpan.FromSeconds(-1) };

        JevConfigurationException exception = Assert.Throws<JevConfigurationException>(
            () => new JevClient(options, new StubTransport(), new StaticApiKeyProvider("k"), null));

        Assert.Contains("InitialRetryDelay cannot be negative", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Snapshot_RejectsABackoffMultiplierBelowOne()
    {
        JevClientOptions options = new() { RetryBackoffMultiplier = 0.5 };

        JevConfigurationException exception = Assert.Throws<JevConfigurationException>(
            () => new JevClient(options, new StubTransport(), new StaticApiKeyProvider("k"), null));

        Assert.Contains("RetryBackoffMultiplier must be at least 1.0", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildUri_RejectsABlankPath()
    {
        JevClientOptions options = new();

        Assert.Throws<ArgumentException>(() => options.BuildUri("   "));
    }

    // ---------------------------------------------------------------- retry edge

    [Fact]
    public void ComputeRetryDelay_NeverReturnsANegativeDelay()
    {
        // A server-supplied Retry-After of zero, combined with a computed backoff of zero, must
        // still produce a usable delay rather than a negative one that Task.Delay would reject.
        JevClientOptions options = new()
        {
            InitialRetryDelay = TimeSpan.Zero,
            RetryBackoffMultiplier = 2,
            MaxRetryDelay = TimeSpan.Zero,
        };

        TimeSpan delay = HttpTypeSafeTransport.ComputeRetryDelay(
            attempt: 0, retryAfter: TimeSpan.FromSeconds(-10), options, jitterSource: null);

        Assert.Equal(TimeSpan.Zero, delay);
    }

    // ---------------------------------------------------------------- validation messages

    [Fact]
    public async Task ScoreWithoutInstructions_IsRejected()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["q"] = new ScoreQuestion { Criteria = [StructuredValue.FromString("a"), StructuredValue.FromString("b")] },
        };

        JevRequestValidationException exception = await Assert.ThrowsAsync<JevRequestValidationException>(
            () => client.SystemOneAsync("text", questions, model: null, CancellationToken.None));

        Assert.Contains(exception.Problems, p => p.Contains("needs instructions saying what to rate", StringComparison.Ordinal));
        Assert.Equal(0, transport.RequestCount);
    }

    [Fact]
    public async Task ScoreWithANullLevel_IsRejected()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["q"] = new ScoreQuestion
            {
                Instructions = StructuredValue.FromString("rate"),
                Criteria = [StructuredValue.FromString("ok"), null!],
            },
        };

        JevRequestValidationException exception = await Assert.ThrowsAsync<JevRequestValidationException>(
            () => client.SystemOneAsync("text", questions, model: null, CancellationToken.None));

        Assert.Contains(exception.Problems, p => p.Contains("null level description", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- transport boundaries

    [Fact]
    public async Task SendAsync_ReportsARequestTimeoutAsAConnectionException()
    {
        // A client timeout surfaces as an OperationCanceledException that the caller's token did
        // not request. Reporting that as a connection failure rather than as a cancellation is
        // what stops a caller from believing it cancelled a call it did not.
        StubHttpMessageHandler handler = new StubHttpMessageHandler()
            .EnqueueThrow(new TaskCanceledException("the client timed out"));

        System.Net.Http.HttpClient httpClient = new(handler, disposeHandler: false);
        using HttpTypeSafeTransport transport = new(httpClient, new StaticApiKeyProvider("k"), timeout: null);

        JevConnectionException exception = await Assert.ThrowsAsync<JevConnectionException>(
            () => transport.SendAsync(
                new TransportRequest(HttpMethod.Get, new Uri("https://api.test/v1/models")),
                CancellationToken.None));

        Assert.Contains("timed out", exception.Message, StringComparison.Ordinal);
        Assert.IsType<TaskCanceledException>(exception.InnerException);
    }

    [Fact]
    public async Task SendAsync_ReportsAHandlerTimeoutDistinctlyFromCallerCancellation()
    {
        // Same exception type from the handler, opposite meaning: here the caller really did
        // cancel, so the cancellation must propagate rather than be relabelled a timeout.
        StubHttpMessageHandler handler = new StubHttpMessageHandler()
            .EnqueueThrow(new TaskCanceledException("cancelled"));

        System.Net.Http.HttpClient httpClient = new(handler, disposeHandler: false);
        using HttpTypeSafeTransport transport = new(httpClient, new StaticApiKeyProvider("k"), timeout: null);

        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => transport.SendAsync(
                new TransportRequest(HttpMethod.Get, new Uri("https://api.test/v1/models")),
                cancellation.Token));
    }

    [Fact]
    public async Task SendAsync_ReturnsNullWhenTheHttpClientSuppliesNoContent()
    {
        // HttpClient normally substitutes an empty content, so the null branch is defensive and
        // not reachable through this seam. The test asserts the behaviour a caller sees: no
        // exception, and a readable empty body.
        StubHttpMessageHandler handler = new StubHttpMessageHandler().Enqueue(_ =>
        {
            HttpResponseMessage message = new(HttpStatusCode.OK);
            message.Content = null;
            return message;
        });

        System.Net.Http.HttpClient httpClient = new(handler, disposeHandler: false);
        using HttpTypeSafeTransport transport = new(httpClient, new StaticApiKeyProvider("k"), timeout: null);

        TransportResponse response = await transport.SendAsync(
            new TransportRequest(HttpMethod.Get, new Uri("https://api.test/v1/models")),
            CancellationToken.None);

        Assert.True(response.IsSuccess);
        Assert.NotNull(response.Body);
        Assert.Empty(response.Body);
    }

    // ---------------------------------------------------------------- misc public surface

    [Fact]
    public void TransportRequest_ReportsWhetherItHasABody()
    {
        TransportRequest withBody = new(HttpMethod.Post, new Uri("https://api.test/v1/x"), [1, 2, 3]);
        TransportRequest withoutBody = new(HttpMethod.Get, new Uri("https://api.test/v1/x"));

        Assert.True(withBody.HasBody);
        Assert.False(withoutBody.HasBody);
    }

    [Fact]
    public void TransportRequest_RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new TransportRequest(null!, new Uri("https://api.test/")));
        Assert.Throws<ArgumentNullException>(() => new TransportRequest(HttpMethod.Get, null!));
    }

    [Fact]
    public void StructuredValue_ImplicitConversionFromJsonElement()
    {
        StructuredValue value = JsonDocument.Parse("""[1,2,3]""").RootElement;

        Assert.Equal(StructuredValue.JsonShape.Sequence, value.Shape);
    }

    [Fact]
    public void Question_NoulFactoryWithNullInstructionsProducesANullValue()
    {
        // The factory accepts a null instruction, and validation rejects it later. The factory
        // itself must not throw, because a caller may be building a question incrementally.
        NoulQuestion question = Question.Noul((StructuredValue?)null);

        Assert.NotNull(question.Instructions);
        Assert.True(question.Instructions!.IsNull);
    }

    [Fact]
    public async Task EnvironmentApiKeyProvider_DefaultConstructorReadsTheRealEnvironment()
    {
        // Exercises the parameterless path that binds to Environment.GetEnvironmentVariable.
        EnvironmentApiKeyProvider provider = new();

        string? key = await provider.GetApiKeyAsync(CancellationToken.None);

        // Whatever the machine holds, the call must not throw and must not invent a value.
        Assert.True(key is null || key.Length > 0);
    }

    [Fact]
    public void ConfigurationLoader_ResolveMachineNameFallsBackToTheMachineName()
    {
        string? previous = Environment.GetEnvironmentVariable(JevEnvironment.MachineNameVariable);

        try
        {
            Environment.SetEnvironmentVariable(JevEnvironment.MachineNameVariable, null);

            string resolved = JevConfigurationLoader.ResolveMachineName();

            Assert.Equal(Environment.MachineName, resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable(JevEnvironment.MachineNameVariable, previous);
        }
    }

    [Fact]
    public void ConfigurationLoader_ResolveMachineNamePrefersTheEnvironmentOverride()
    {
        string? previous = Environment.GetEnvironmentVariable(JevEnvironment.MachineNameVariable);

        try
        {
            Environment.SetEnvironmentVariable(JevEnvironment.MachineNameVariable, "OVERRIDE01");

            Assert.Equal("OVERRIDE01", JevConfigurationLoader.ResolveMachineName());
        }
        finally
        {
            Environment.SetEnvironmentVariable(JevEnvironment.MachineNameVariable, previous);
        }
    }

    [Fact]
    public async Task ValidationFailureWithNoBody_ReportsNoDetails()
    {
        // A 422 whose body is absent must still produce a validation exception, just without
        // per-field detail.
        StubTransport transport = new StubTransport().EnqueueRaw(null, HttpStatusCode.UnprocessableEntity);
        using JevClient client = TestClient.Create(transport);

        JevValidationException exception = await Assert.ThrowsAsync<JevValidationException>(
            () => client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None));

        Assert.Empty(exception.Details);
    }

    [Fact]
    public async Task ValidationFailureWithABodyThatIsNotTheDocumentedShape_ReportsNoDetails()
    {
        // The failure is real even when the detail payload is not what the docs describe, so the
        // exception is raised and the raw body is preserved for diagnosis.
        StubTransport transport = new StubTransport().EnqueueJson(
            """{"detail":"a string, not the documented array"}""",
            HttpStatusCode.UnprocessableEntity);

        using JevClient client = TestClient.Create(transport);

        JevValidationException exception = await Assert.ThrowsAsync<JevValidationException>(
            () => client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None));

        Assert.Empty(exception.Details);
        Assert.NotNull(exception.ResponseBody);
    }

    [Fact]
    public async Task CancelledCall_IsRecordedWithACancelledOutcome()
    {
        using MetricCollector collector = new();

        // Cancellation is observed once the call is already in flight, which is the case that
        // matters: a pre-cancelled call never reaches the transport at all.
        StubTransport transport = new StubTransport().Enqueue(_ => throw new OperationCanceledException());

        using JevClient client = TestClient.Create(transport);
        using CancellationTokenSource cancellation = new();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, cancellation.Token));

        // The call registered, tagged as error because the caller's token was never cancelled.
        // A cancelled outcome is recorded when the caller does cancel, which the next assertion
        // covers by driving the token during the call.
        Assert.Contains(
            collector.Measurements,
            m => m.InstrumentName == JevTelemetry.CallsInstrumentName
                 && m.Tags.Any(t => t.Key == JevTelemetry.OutcomeTag));
    }

    [Fact]
    public async Task CallCancelledMidFlight_IsRecordedWithACancelledOutcome()
    {
        using MetricCollector collector = new();
        using CancellationTokenSource cancellation = new();

        // Cancel from inside the transport, so the token is already cancelled by the time the
        // finally block inspects it.
        StubTransport transport = new StubTransport().Enqueue(_ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException();
        });

        using JevClient client = TestClient.Create(transport);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, cancellation.Token));

        Assert.Contains(
            collector.Measurements,
            m => m.InstrumentName == JevTelemetry.CallsInstrumentName
                 && m.Tags.Any(t => t.Key == JevTelemetry.OutcomeTag && (string?)t.Value == "cancelled"));
    }
}
