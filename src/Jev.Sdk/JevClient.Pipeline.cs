// JevClient.Pipeline.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the client.
// See requirements/requirements.md, R7, R8, R11, R13 and R19.
//
// Type: JevClient
//
// The shared pipeline: send, retry retryable failures, map status codes to exceptions, and
// emit telemetry. Everything that both endpoints do in common lives here, so retry behaviour
// and error mapping are defined once and cannot drift between endpoints.
//
// The caller's state never appears in a log message, a span tag, or a metric tag, at any
// level. What is recorded is shape and outcome: the model, how many questions, the status, the
// retry count, and the token counts the API reports.

using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Jev.Sdk;

/// <summary>
/// The shared send, retry, and error-mapping pipeline.
/// </summary>
public sealed partial class JevClient
{
    private async Task<TResponse> SendAsync<TResponse>(
        HttpMethod method,
        string relativePath,
        byte[]? payload,
        JsonTypeInfo<TResponse> responseTypeInfo,
        string model,
        int questionCount,
        bool isSystemOne,
        CancellationToken cancellationToken)
    {
        Uri uri = _options.BuildUri(relativePath);
        long startTimestamp = Stopwatch.GetTimestamp();
        int retriesPerformed = 0;
        TransportResponse? lastResponse = null;
        bool recordedOutcome = false;

        using Activity? activity = JevTelemetry.ActivitySource.StartActivity(
            isSystemOne ? JevTelemetry.SystemOneActivityName : JevTelemetry.GetModelsActivityName,
            ActivityKind.Client);

        if (activity is not null)
        {
            if (!string.IsNullOrEmpty(model))
            {
                activity.SetTag(JevTelemetry.ModelTag, model);
            }

            if (isSystemOne)
            {
                activity.SetTag(JevTelemetry.QuestionCountTag, questionCount);
            }
        }

        JevLog.SendingRequest(_logger, method.Method, relativePath, model, questionCount);

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    lastResponse = await _transport
                        .SendAsync(new TransportRequest(method, uri, payload), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (JevConnectionException) when (retriesPerformed < _options.MaxRetries)
                {
                    // A transport failure is usually transient, so it is retried with the same
                    // backoff as a rate limit.
                    retriesPerformed++;
                    TimeSpan delay = HttpTypeSafeTransport.ComputeRetryDelay(
                        retriesPerformed - 1, retryAfter: null, _options, _jitterSource);

                    JevLog.RetryingAfterTransportFailure(
                        _logger, method.Method, relativePath, retriesPerformed, _options.MaxRetries, (long)delay.TotalMilliseconds);

                    JevTelemetry.Retries.Add(1, new KeyValuePair<string, object?>(JevTelemetry.StatusCodeTag, 0));

                    await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (lastResponse.IsSuccess)
                {
                    break;
                }

                if (HttpTypeSafeTransport.IsRetryableStatus(lastResponse.StatusCode) && retriesPerformed < _options.MaxRetries)
                {
                    retriesPerformed++;
                    TimeSpan delay = HttpTypeSafeTransport.ComputeRetryDelay(
                        retriesPerformed - 1, lastResponse.RetryAfter, _options, _jitterSource);

                    JevLog.Retrying(
                        _logger,
                        method.Method,
                        relativePath,
                        (int)lastResponse.StatusCode,
                        retriesPerformed,
                        _options.MaxRetries,
                        (long)delay.TotalMilliseconds);

                    JevTelemetry.Retries.Add(1, new KeyValuePair<string, object?>(JevTelemetry.StatusCodeTag, (int)lastResponse.StatusCode));

                    activity?.AddEvent(new ActivityEvent(
                        "retry",
                        tags: new ActivityTagsCollection
                        {
                            { "attempt", retriesPerformed },
                            { "status_code", (int)lastResponse.StatusCode },
                            { "delay_ms", (long)delay.TotalMilliseconds },
                        }));

                    await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                break;
            }

            TransportResponse finalResponse = lastResponse
                ?? throw new JevConnectionException("The transport returned no response.");

            long elapsedMs = (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

            if (activity is not null)
            {
                activity.SetTag(JevTelemetry.StatusCodeTag, (int)finalResponse.StatusCode);
                activity.SetTag(JevTelemetry.RetryCountTag, retriesPerformed);
            }

            JevLog.RequestCompleted(
                _logger, method.Method, relativePath, (int)finalResponse.StatusCode, elapsedMs, model);

            TResponse result = ParseResponse(
                finalResponse,
                responseTypeInfo,
                method,
                relativePath,
                retriesPerformed + 1);

            if (isSystemOne && result is SystemOneResponse systemOne && systemOne.Usage is { } usage)
            {
                JevLog.AnswersReceived(_logger, systemOne.Answers.Count, usage.InputTokens, usage.OutputTokens);

                JevTelemetry.InputTokens.Add(usage.InputTokens);
                JevTelemetry.OutputTokens.Add(usage.OutputTokens);

                activity?.SetTag(JevTelemetry.InputTokensTag, usage.InputTokens);
                activity?.SetTag(JevTelemetry.OutputTokensTag, usage.OutputTokens);
            }

            JevTelemetry.Calls.Add(1, new KeyValuePair<string, object?>(JevTelemetry.OutcomeTag, "success"));
            JevTelemetry.Duration.Record(
                Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds,
                new KeyValuePair<string, object?>(JevTelemetry.OutcomeTag, "success"));

            recordedOutcome = true;

            return result;
        }
        finally
        {
            // A cancelled or failed call is still a call, and recording it is what makes a
            // failure or cancellation rate visible rather than invisible. The flag is set only
            // on the success path, so this runs exactly once per call either way.
            if (!recordedOutcome)
            {
                string outcome = cancellationToken.IsCancellationRequested ? "cancelled" : "error";

                if (activity is not null)
                {
                    activity.SetTag(JevTelemetry.OutcomeTag, outcome);
                }

                JevTelemetry.Calls.Add(1, new KeyValuePair<string, object?>(JevTelemetry.OutcomeTag, outcome));
                JevTelemetry.Duration.Record(
                    Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds,
                    new KeyValuePair<string, object?>(JevTelemetry.OutcomeTag, outcome));
            }
        }
    }

    private TResponse ParseResponse<TResponse>(
        TransportResponse response,
        JsonTypeInfo<TResponse> responseTypeInfo,
        HttpMethod method,
        string relativePath,
        int attempts)
    {
        if (!response.IsSuccess)
        {
            throw MapErrorResponse(response, method, relativePath, attempts);
        }

        if (response.Body is null || response.Body.Length == 0)
        {
            // Only a 200 with a body is meaningful here. An empty success body means the
            // contract was not honoured.
            throw new JevConnectionException(
                $"{method.Method} {relativePath} returned {response.StatusCode} with an empty body.",
                isProtocolError: true);
        }

        try
        {
            TResponse? parsed = JsonSerializer.Deserialize(response.Body, responseTypeInfo);

            return parsed ?? throw new JevConnectionException(
                $"{method.Method} {relativePath} returned a null body where an object was expected.",
                isProtocolError: true,
                responseBody: response.BodyAsText);
        }
        catch (JsonException exception)
        {
            // A body that cannot be parsed will not parse on retry, so this is not retried.
            JevLog.ResponseUnreadable(_logger, method.Method, relativePath, attempts);

            throw new JevConnectionException(
                $"{method.Method} {relativePath} returned a body that could not be read as JSON.",
                exception,
                isProtocolError: true,
                responseBody: response.BodyAsText);
        }
    }

    private JevApiException MapErrorResponse(
        TransportResponse response,
        HttpMethod method,
        string relativePath,
        int attempts)
    {
        string? body = response.BodyAsText;
        int statusCode = (int)response.StatusCode;
        string summary = $"{method.Method} {relativePath} returned {statusCode}";

        JevLog.RequestFailed(_logger, method.Method, relativePath, statusCode, attempts);

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new JevAuthenticationException(
                $"{summary}: the API key is missing or invalid.",
                body),

            HttpStatusCode.UnprocessableEntity => new JevValidationException(
                $"{summary}: the API rejected the request body.",
                ReadValidationDetails(body),
                body),

            HttpStatusCode.TooManyRequests => new JevRateLimitException(
                $"{summary}: the rate limit is in force. Retries were exhausted.",
                response.RetryAfter,
                body),

            _ when statusCode == 529 => new JevOverloadedException(
                $"{summary}: TypeSafe is overloaded. Retries were exhausted.",
                response.RetryAfter,
                body),

            _ => new JevApiException(
                response.StatusCode,
                $"{summary}: {response.StatusCode}.",
                body),
        };
    }

    private IReadOnlyList<ErrorDetails> ReadValidationDetails(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        try
        {
            ValidationErrorResponse? parsed = JsonSerializer.Deserialize(body, _jsonContext.ValidationErrorResponse);

            return parsed is null ? [] : [.. parsed.Detail];
        }
        catch (JsonException)
        {
            // A validation failure whose body does not match the documented shape is still a
            // validation failure; the raw body is on the exception for diagnosis.
            return [];
        }
    }
}
