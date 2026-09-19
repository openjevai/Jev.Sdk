// JevClient.Pipeline.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the client.
// See requirements/requirements.md, R7, R8, R11, R13 and R19.
//
// Type: JevClient
//
// The shared pipeline: send, retry retryable failures, map status codes to exceptions, and emit
// telemetry. Everything that both endpoints do in common lives here, so retry behaviour and error
// mapping are defined once and cannot drift between endpoints.
//
// The caller's state never appears in a log message, a span tag, or a metric tag, at any level.
// What is recorded is shape and outcome: the model, how many questions, the status, the retry
// count, the token counts the API reports, and the server's request id. The request id is safe to
// record because it is a bounded, server-supplied identifier rather than caller content.

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
                        .SendAsync(
                            new TransportRequest(method, uri, payload, _options.Headers),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (JevConnectionException) when (retriesPerformed < _options.MaxRetries)
                {
                    // A transport failure is usually transient, so it is retried with the same
                    // backoff as a server-side status.
                    retriesPerformed++;
                    TimeSpan delay = HttpTypeSafeTransport.ComputeRetryDelay(
                        retriesPerformed - 1, retryAfter: null, _options, _jitterSource);

                    JevLog.RetryingAfterTransportFailure(
                        _logger,
                        method.Method,
                        relativePath,
                        retriesPerformed,
                        _options.MaxRetries,
                        (long)delay.TotalMilliseconds);

                    JevTelemetry.Retries.Add(
                        1,
                        new KeyValuePair<string, object?>(JevTelemetry.StatusCodeTag, 0));

                    await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (lastResponse.IsSuccess)
                {
                    break;
                }

                if (HttpTypeSafeTransport.IsRetryableStatus(lastResponse.StatusCode)
                    && retriesPerformed < _options.MaxRetries)
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

                    JevTelemetry.Retries.Add(
                        1,
                        new KeyValuePair<string, object?>(JevTelemetry.StatusCodeTag, (int)lastResponse.StatusCode));

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

                // The request id is a server-supplied identifier, so it is safe on a span and it is
                // the one thing that lets an operator find this exact call in TypeSafe's own logs.
                if (finalResponse.RequestId is { Length: > 0 } requestId)
                {
                    activity.SetTag(JevTelemetry.RequestIdTag, requestId);
                }
            }

            JevLog.RequestCompleted(
                _logger, method.Method, relativePath, (int)finalResponse.StatusCode, elapsedMs, model);

            TResponse result = ParseResponse(
                finalResponse,
                responseTypeInfo,
                method,
                relativePath,
                retriesPerformed + 1);

            // The request id is attached to the materialised result, so a caller that logs a
            // successful call has the handle for it too.
            if (result is JevResponse jeevResponse && finalResponse.RequestId is { Length: > 0 } successRequestId)
            {
                jeevResponse.RequestId = successRequestId;
            }

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
            // A cancelled or failed call is still a call, and recording it is what makes a failure
            // or cancellation rate visible rather than invisible. The flag is set only on the
            // success path, so this runs exactly once per call either way.
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
            // Only a 200 with a body is meaningful here. An empty success body means the contract
            // was not honoured.
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
                responseBody: response.BodyAsText,
                requestId: response.RequestId);
        }
        catch (JsonException exception)
        {
            // A body that cannot be parsed will not parse on retry, so this is not retried.
            JevLog.ResponseUnreadable(_logger, method.Method, relativePath, attempts);

            throw new JevConnectionException(
                $"{method.Method} {relativePath} returned a body that could not be read as JSON.",
                exception,
                isProtocolError: true,
                responseBody: response.BodyAsText,
                requestId: response.RequestId);
        }
    }

    private JevApiException MapErrorResponse(
        TransportResponse response,
        HttpMethod method,
        string relativePath,
        int attempts)
    {
        string? body = response.BodyAsText;
        string? requestId = response.RequestId;
        string endpoint = $"{method.Method} {relativePath}";
        int statusCode = (int)response.StatusCode;

        JevLog.RequestFailed(_logger, method.Method, relativePath, statusCode, attempts, requestId);

        return response.StatusCode switch
        {
            HttpStatusCode.BadRequest => new JevBadRequestException(
                $"{endpoint} returned 400: the request was malformed.",
                body, requestId, endpoint),

            HttpStatusCode.Unauthorized => new JevAuthenticationException(
                $"{endpoint} returned 401: the API key is missing or invalid.",
                body, requestId, endpoint),

            HttpStatusCode.Forbidden => new JevPermissionDeniedException(
                $"{endpoint} returned 403: the credential is valid but not permitted for this request.",
                body, requestId, endpoint),

            HttpStatusCode.NotFound => new JevNotFoundException(
                $"{endpoint} returned 404: the resource does not exist.",
                body, requestId, endpoint),

            HttpStatusCode.UnprocessableEntity => new JevValidationException(
                $"{endpoint} returned 422: the API rejected the request body.",
                ReadValidationDetails(body),
                body, requestId, endpoint),

            HttpStatusCode.TooManyRequests => new JevRateLimitException(
                $"{endpoint} returned 429: the rate limit is in force. Retries were exhausted.",
                response.RetryAfter, body, requestId, endpoint),

            _ when statusCode == HttpTypeSafeTransport.OverloadedStatusCode => new JevOverloadedException(
                $"{endpoint} returned 529: TypeSafe is overloaded. Retries were exhausted.",
                response.RetryAfter, body, requestId, endpoint),

            _ when statusCode is >= 500 and <= 599 => new JevServerException(
                response.StatusCode,
                $"{endpoint} returned {statusCode}: the server failed to process the request. Retries were exhausted.",
                body, response.RetryAfter, requestId, endpoint),

            _ => new JevApiException(
                response.StatusCode,
                $"{endpoint} returned {statusCode}: {response.StatusCode}.",
                body, requestId, endpoint),
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
