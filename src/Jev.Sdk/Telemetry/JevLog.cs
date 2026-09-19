// JevLog.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for telemetry.
// See requirements/requirements.md, R11 and R19.
//
// Type: JevLog
//
// Log messages are declared here rather than written inline so that the source generator can
// produce allocation-free logging, and so that every message this library can emit is visible
// in one place and can be reviewed for redaction. No message takes the caller's state as a
// parameter, by design.

using Microsoft.Extensions.Logging;

namespace Jev.Sdk;

/// <summary>
/// Source-generated log messages. Every message this library can emit is declared here.
/// </summary>
internal static partial class JevLog
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Debug,
        Message = "Sending {Method} {Path} using model {Model} with {QuestionCount} question(s).")]
    internal static partial void SendingRequest(ILogger logger, string method, string path, string model, int questionCount);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Debug,
        Message = "{Method} {Path} completed with status {StatusCode} in {ElapsedMs} ms, using model {Model}.")]
    internal static partial void RequestCompleted(ILogger logger, string method, string path, int statusCode, long elapsedMs, string model);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Debug,
        Message = "Received {AnswerCount} answer(s) and consumed {InputTokens} input / {OutputTokens} output tokens.")]
    internal static partial void AnswersReceived(ILogger logger, int answerCount, int inputTokens, int outputTokens);

    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Warning,
        Message = "Retrying {Method} {Path} after status {StatusCode}. Attempt {Attempt} of {MaxAttempts}, waiting {DelayMs} ms.")]
    internal static partial void Retrying(ILogger logger, string method, string path, int statusCode, int attempt, int maxAttempts, long delayMs);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Warning,
        Message = "Retrying {Method} {Path} after a transport failure. Attempt {Attempt} of {MaxAttempts}, waiting {DelayMs} ms.")]
    internal static partial void RetryingAfterTransportFailure(ILogger logger, string method, string path, int attempt, int maxAttempts, long delayMs);

    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Error,
        Message = "{Method} {Path} failed with status {StatusCode} after {Attempts} attempt(s). Request id {RequestId}.")]
    internal static partial void RequestFailed(ILogger logger, string method, string path, int statusCode, int attempts, string? requestId);

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Error,
        Message = "{Method} {Path} failed after {Attempts} attempt(s): the response could not be interpreted.")]
    internal static partial void ResponseUnreadable(ILogger logger, string method, string path, int attempts);

    [LoggerMessage(
        EventId = 4000,
        Level = LogLevel.Trace,
        Message = "Serializing request body for {Path}. Contents are not logged.")]
    internal static partial void SerializingRequest(ILogger logger, string path);
}
