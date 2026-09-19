// JevTelemetry.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for telemetry.
// See requirements/requirements.md, R11 and R19.
//
// Type: JevTelemetry
//
// Telemetry is emitted through the base class library only: ILogger for events, ActivitySource
// for operation shape, and Meter for aggregates. Nothing here depends on OpenTelemetry or any
// exporter, because a library that takes such a dependency becomes unusable to consumers who
// chose a different one. The consumer decides where the signals go by listening for the names
// declared here.
//
// Two rules hold throughout this file. The state value a caller supplies is never logged,
// tagged, or attached to a span at any level, because it is caller content and may be
// regulated. And the API key and authorization header are never recorded.

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Jev.Sdk;

/// <summary>
/// The names and shapes of the telemetry this library emits.
/// </summary>
public static class JevTelemetry
{
    /// <summary>Name of the <see cref="ActivitySource"/> and <see cref="Meter"/>.</summary>
    public const string SourceName = "Jev.Sdk";

    /// <summary>Name of the span emitted for an evaluation call.</summary>
    public const string SystemOneActivityName = "systemone.call";

    /// <summary>Name of the span emitted for a model listing call.</summary>
    public const string GetModelsActivityName = "models.list";

    /// <summary>Tag holding the model name used for a call.</summary>
    public const string ModelTag = "jev.model";

    /// <summary>Tag holding the number of questions in a call.</summary>
    public const string QuestionCountTag = "jev.questions.count";

    /// <summary>Tag holding the number of retries performed.</summary>
    public const string RetryCountTag = "jev.retries";

    /// <summary>Tag holding the HTTP status code of the final response.</summary>
    public const string StatusCodeTag = "jev.status_code";

    /// <summary>Tag holding the input tokens reported for a call.</summary>
    public const string InputTokensTag = "jev.tokens.input";

    /// <summary>Tag holding the output tokens reported for a call.</summary>
    public const string OutputTokensTag = "jev.tokens.output";

    /// <summary>Counter of completed calls, tagged by outcome.</summary>
    public const string CallsInstrumentName = "jev.calls";

    /// <summary>Counter of retries performed, tagged by the status that caused them.</summary>
    public const string RetriesInstrumentName = "jev.retries";

    /// <summary>Histogram of call duration in seconds.</summary>
    public const string DurationInstrumentName = "jev.call.duration";

    /// <summary>Counter of input tokens consumed.</summary>
    public const string InputTokensInstrumentName = "jev.tokens.input";

    /// <summary>Counter of output tokens consumed.</summary>
    public const string OutputTokensInstrumentName = "jev.tokens.output";

    /// <summary>Tag holding the outcome of a call: success, error, or cancelled.</summary>
    public const string OutcomeTag = "jev.outcome";

    internal static readonly ActivitySource ActivitySource = new(SourceName, typeof(JevTelemetry).Assembly.GetName().Version?.ToString() ?? "0.1.0");

    internal static readonly Meter Meter = new(SourceName, typeof(JevTelemetry).Assembly.GetName().Version?.ToString() ?? "0.1.0");

    internal static readonly Counter<long> Calls = Meter.CreateCounter<long>(
        CallsInstrumentName,
        unit: "{call}",
        description: "Number of completed TypeSafe calls, tagged by outcome.");

    internal static readonly Counter<long> Retries = Meter.CreateCounter<long>(
        RetriesInstrumentName,
        unit: "{retry}",
        description: "Number of retries performed, tagged by the status that caused them.");

    internal static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        DurationInstrumentName,
        unit: "s",
        description: "Duration of TypeSafe calls in seconds.");

    internal static readonly Counter<long> InputTokens = Meter.CreateCounter<long>(
        InputTokensInstrumentName,
        unit: "{token}",
        description: "Input tokens consumed, as reported by the API.");

    internal static readonly Counter<long> OutputTokens = Meter.CreateCounter<long>(
        OutputTokensInstrumentName,
        unit: "{token}",
        description: "Output tokens consumed, as reported by the API.");
}
