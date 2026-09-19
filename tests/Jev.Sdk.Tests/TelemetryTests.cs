// TelemetryTests.cs
// Part of Jev.Sdk.Tests. Two properties matter here and neither is cosmetic.
//
// First, the caller's state is sensitive: it is a support ticket, a customer message, possibly
// regulated content. Nothing this library emits may carry it. These tests drive a state value
// that would be obvious if it leaked, then assert it appears nowhere.
//
// Second, the spans and counters must actually be emitted, because a consumer wiring up
// OpenTelemetry has no other way to know the names are right.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Jev.Sdk;
using Microsoft.Extensions.Logging;

namespace Jev.Sdk.Tests;

public class TelemetryTests
{
    private const string SensitiveState = "SENSITIVE-CUSTOMER-CONTENT-DO-NOT-LOG-4471";

    [Fact]
    public async Task SystemOne_EmitsASpanWithShapeTags()
    {
        using ActivityCollector collector = new();
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        await client.SystemOneAsync(SensitiveState, TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        Activity span = Assert.Single(collector.Activities, a => a.OperationName == JevTelemetry.SystemOneActivityName);

        Assert.Equal("jev-latest", span.GetTagItem(JevTelemetry.ModelTag)?.ToString());
        Assert.Equal(3, span.GetTagItem(JevTelemetry.QuestionCountTag));
        Assert.Equal(200, span.GetTagItem(JevTelemetry.StatusCodeTag));
        Assert.Equal(0, span.GetTagItem(JevTelemetry.RetryCountTag));
    }

    [Fact]
    public async Task SystemOne_RecordsTokenUsageOnTheSpan()
    {
        using ActivityCollector collector = new();
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        await client.SystemOneAsync(SensitiveState, TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        Activity span = Assert.Single(collector.Activities, a => a.OperationName == JevTelemetry.SystemOneActivityName);

        Assert.Equal(312, span.GetTagItem(JevTelemetry.InputTokensTag));
        Assert.Equal(48, span.GetTagItem(JevTelemetry.OutputTokensTag));
    }

    [Fact]
    public async Task TheStateValue_NeverReachesASpanTag()
    {
        using ActivityCollector collector = new();
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        await client.SystemOneAsync(SensitiveState, TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        foreach (Activity span in collector.Activities)
        {
            foreach (KeyValuePair<string, string?> tag in span.TagObjects.Select(t => new KeyValuePair<string, string?>(t.Key, t.Value?.ToString())))
            {
                Assert.DoesNotContain(SensitiveState, tag.Value ?? string.Empty, StringComparison.Ordinal);
            }

            foreach (ActivityEvent activityEvent in span.Events)
            {
                foreach (KeyValuePair<string, object?> tag in activityEvent.Tags)
                {
                    Assert.DoesNotContain(SensitiveState, tag.Value?.ToString() ?? string.Empty, StringComparison.Ordinal);
                }
            }

            Assert.DoesNotContain(SensitiveState, span.DisplayName, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task TheStateValue_NeverReachesALogMessage()
    {
        RecordingLoggerProvider recorder = new();
        ILoggerFactory factory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(recorder));
        ILogger logger = factory.CreateLogger("Jev.Sdk.Tests");

        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport, logger: logger);

        await client.SystemOneAsync(SensitiveState, TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        Assert.NotEmpty(recorder.Messages);

        foreach (string message in recorder.Messages)
        {
            Assert.DoesNotContain(SensitiveState, message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task TheApiKey_NeverReachesALogMessage()
    {
        RecordingLoggerProvider recorder = new();
        ILoggerFactory factory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(recorder));
        ILogger logger = factory.CreateLogger("Jev.Sdk.Tests");

        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport, logger: logger);

        await client.SystemOneAsync(SensitiveState, TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        foreach (string message in recorder.Messages)
        {
            Assert.DoesNotContain(TestClient.TestApiKey, message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task TheQuestionId_NeverBecomesAMetricTag()
    {
        // A question id is caller-supplied and unbounded, so using one as a tag would blow up a
        // metrics backend. Only bounded values may be tags.
        using MetricCollector collector = new();
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        Dictionary<string, Question> questions = new(StringComparer.Ordinal)
        {
            ["caller_invented_unbounded_id_12345"] = Question.Noul("is it?"),
        };

        await client.SystemOneAsync(SensitiveState, questions, model: null, CancellationToken.None);

        foreach (Measurement measurement in collector.Measurements)
        {
            foreach (KeyValuePair<string, object?> tag in measurement.Tags)
            {
                string text = tag.Value?.ToString() ?? string.Empty;
                Assert.DoesNotContain("caller_invented_unbounded_id_12345", text, StringComparison.Ordinal);
                Assert.DoesNotContain(SensitiveState, text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task SuccessfulCall_RecordsCallsAndDuration()
    {
        using MetricCollector collector = new();
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        await client.SystemOneAsync(SensitiveState, TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        Measurement call = Assert.Single(collector.Measurements, m => m.InstrumentName == JevTelemetry.CallsInstrumentName);
        Assert.Equal(1, call.Value);
        Assert.Equal("success", call.Tags.First(t => t.Key == JevTelemetry.OutcomeTag).Value);

        Assert.Contains(collector.Measurements, m => m.InstrumentName == JevTelemetry.DurationInstrumentName);
    }

    [Fact]
    public async Task TokenCounters_RecordTheReportedUsage()
    {
        using MetricCollector collector = new();
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        using JevClient client = TestClient.Create(transport);

        await client.SystemOneAsync(SensitiveState, TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        Measurement input = Assert.Single(collector.Measurements, m => m.InstrumentName == JevTelemetry.InputTokensInstrumentName);
        Measurement output = Assert.Single(collector.Measurements, m => m.InstrumentName == JevTelemetry.OutputTokensInstrumentName);

        Assert.Equal(312, input.Value);
        Assert.Equal(48, output.Value);
    }

    [Fact]
    public async Task Retries_AreCountedAndTaggedWithTheStatus()
    {
        using MetricCollector collector = new();
        StubTransport transport = new StubTransport()
            .EnqueueJson("""{"error":"busy"}""", System.Net.HttpStatusCode.TooManyRequests)
            .EnqueueJson(TestClient.SuccessJson());

        using JevClient client = TestClient.Create(transport);

        await client.SystemOneAsync(SensitiveState, TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        Measurement retry = Assert.Single(collector.Measurements, m => m.InstrumentName == JevTelemetry.RetriesInstrumentName);
        Assert.Equal(1, retry.Value);
        Assert.Equal(429, retry.Tags.First(t => t.Key == JevTelemetry.StatusCodeTag).Value);
    }

    [Fact]
    public async Task Retry_AddsAnEventToTheSpan()
    {
        using ActivityCollector collector = new();
        StubTransport transport = new StubTransport()
            .EnqueueJson("""{"error":"busy"}""", System.Net.HttpStatusCode.TooManyRequests)
            .EnqueueJson(TestClient.SuccessJson());

        using JevClient client = TestClient.Create(transport);

        await client.SystemOneAsync(SensitiveState, TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        Activity span = Assert.Single(collector.Activities, a => a.OperationName == JevTelemetry.SystemOneActivityName);

        ActivityEvent retryEvent = Assert.Single(span.Events, e => e.Name == "retry");
        Assert.Equal(1, retryEvent.Tags.First(t => t.Key == "attempt").Value);
    }

    [Fact]
    public async Task FailedCall_IsStillCountedAsAnOutcome()
    {
        using MetricCollector collector = new();
        StubTransport transport = new StubTransport()
            .EnqueueJson("""{"error":"bad key"}""", System.Net.HttpStatusCode.Unauthorized);

        using JevClient client = TestClient.Create(transport);

        await Assert.ThrowsAsync<JevAuthenticationException>(
            () => client.SystemOneAsync(SensitiveState, TestClient.ThreeQuestions(), model: null, CancellationToken.None));

        // A failed call is still a call. Recording only successes would make a failure rate
        // invisible to whoever is watching the metric.
        Measurement call = Assert.Single(collector.Measurements, m => m.InstrumentName == JevTelemetry.CallsInstrumentName);
        Assert.Equal("error", call.Tags.First(t => t.Key == JevTelemetry.OutcomeTag).Value);
    }

    [Fact]
    public async Task GetModels_EmitsItsOwnSpan()
    {
        using ActivityCollector collector = new();
        StubTransport transport = new StubTransport().EnqueueJson("""{"models":[]}""");
        using JevClient client = TestClient.Create(transport);

        await client.GetModelsAsync(CancellationToken.None);

        Assert.Single(collector.Activities, a => a.OperationName == JevTelemetry.GetModelsActivityName);
    }

    [Fact]
    public void SourceName_IsStable()
    {
        // The name is the contract with whoever is configuring OpenTelemetry, so it must not
        // drift silently.
        Assert.Equal("Jev.Sdk", JevTelemetry.SourceName);
    }
}

/// <summary>
/// Collects activities emitted by the library.
/// </summary>
internal sealed class ActivityCollector : IDisposable
{
    private readonly ActivityListener _listener;

    internal ActivityCollector()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == JevTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = Activities.Add,
        };

        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>Every activity the library stopped, in order.</summary>
    internal List<Activity> Activities { get; } = [];

    public void Dispose() => _listener.Dispose();
}

/// <summary>
/// Collects measurements emitted by the library's instruments.
/// </summary>
internal sealed class MetricCollector : IDisposable
{
    private readonly MeterListener _listener;

    internal MetricCollector()
    {
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == JevTelemetry.SourceName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };

        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            Measurements.Add(new Measurement(instrument.Name, value, [.. tags.ToArray()])));

        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            Measurements.Add(new Measurement(instrument.Name, value, [.. tags.ToArray()])));

        _listener.Start();
    }

    /// <summary>Every measurement recorded, in order.</summary>
    internal List<Measurement> Measurements { get; } = [];

    public void Dispose() => _listener.Dispose();
}

/// <summary>A single recorded measurement.</summary>
/// <param name="InstrumentName">The instrument's name.</param>
/// <param name="Value">The recorded value.</param>
/// <param name="Tags">The tags supplied with it.</param>
internal sealed record Measurement(string InstrumentName, double Value, IReadOnlyList<KeyValuePair<string, object?>> Tags);
