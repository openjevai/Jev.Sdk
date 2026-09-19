// HttpTypeSafeTransport.Retry.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the HTTP transport.
// See requirements/requirements.md, R8 and R13.
//
// Type: HttpTypeSafeTransport
//
// Retry policy. This is the only place in the library that retries, and it is written as a
// deterministic function of the response so that a test can drive it without timers: the delay is
// computed, then awaited through the caller's token.
//
// The retryable status set and the backoff shape deliberately match the vendor's own SDKs:
//
//   408 Request Timeout      the server gave up waiting for the request
//   429 Too Many Requests    the rate limit
//   500-599                  server-side conditions, which are transient by nature
//
// The documented TypeSafe-specific status 529 (Overloaded) falls inside that 5xx range, so it is
// covered by the range rather than named separately. Retrying only 429 and 529 would have left a
// 500, 502, or 503 un-retried, which is how a transient outage becomes a caller-visible failure.
//
// Backoff is exponential from InitialRetryDelay, bounded by MaxRetryDelay, with subtractive
// jitter: each delay is reduced by a random fraction no greater than RetryJitterFraction. That
// keeps the intended backoff while desynchronising callers that hit the same limit together. It
// is deliberately not "full jitter", which replaces the delay with a uniform random value and
// would shorten the wait far more than the vendor's SDKs do.

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;

namespace Jev.Sdk;

/// <summary>
/// Retry policy for <see cref="HttpTypeSafeTransport"/>.
/// </summary>
public sealed partial class HttpTypeSafeTransport
{
    /// <summary>The status code TypeSafe documents for a temporary overload.</summary>
    internal const int OverloadedStatusCode = 529;

    /// <summary>
    /// Returns true when a status code is worth retrying.
    /// </summary>
    /// <remarks>
    /// The set is 408, 429, and every 5xx. A 4xx caused by the request or the credential cannot be
    /// improved by repetition, so 400, 401, 403, 404, and 422 are never retried.
    /// </remarks>
    internal static bool IsRetryableStatus(HttpStatusCode statusCode)
    {
        int code = (int)statusCode;

        return code is (int)HttpStatusCode.RequestTimeout or (int)HttpStatusCode.TooManyRequests
            || code is >= 500 and <= 599;
    }

    /// <summary>
    /// Computes the delay before the next attempt.
    /// </summary>
    /// <param name="attempt">The zero-based count of attempts already made.</param>
    /// <param name="retryAfter">The server-requested delay, when present.</param>
    /// <param name="options">The client's retry settings.</param>
    /// <param name="jitterSource">Supplies a value in [0, 1) for jitter. Null disables jitter.</param>
    /// <returns>The delay to observe before retrying.</returns>
    /// <remarks>
    /// A server-supplied <c>Retry-After</c> takes precedence over the computed backoff, because the
    /// server knows how long its own limit lasts. It is bounded by
    /// <see cref="JevClientOptions.MaxRetryAfter"/> rather than by the backoff ceiling: a delay
    /// longer than that causes the server value to be discarded and the computed backoff used
    /// instead, on the reasoning that a caller waiting minutes inside one call would rather fail
    /// and retry at its own level.
    /// </remarks>
    internal static TimeSpan ComputeRetryDelay(
        int attempt,
        TimeSpan? retryAfter,
        JevClientOptions options,
        Func<double>? jitterSource)
    {
        bool usingServerDelay = retryAfter is { } serverDelay && serverDelay <= options.MaxRetryAfter;

        if (usingServerDelay)
        {
            // A server instruction is followed as given. Jitter is not applied to it: the server
            // said how long to wait, and shortening that risks another immediate rejection.
            return retryAfter!.Value < TimeSpan.Zero ? TimeSpan.Zero : retryAfter.Value;
        }

        TimeSpan computed = ComputeBackoff(attempt, options);

        if (jitterSource is not null && options.RetryJitterFraction > 0)
        {
            // Subtractive jitter: subtract up to RetryJitterFraction of the delay.
            double fraction = jitterSource() * options.RetryJitterFraction;
            double remaining = 1.0 - fraction;
            computed = TimeSpan.FromTicks((long)(computed.Ticks * remaining));
        }

        if (computed < TimeSpan.Zero)
        {
            // Defensive: a caller can hand this method an options instance built by hand, bypassing
            // the constructor validation that rejects a negative initial delay. Task.Delay rejects a
            // negative TimeSpan, so clamping here keeps a misconfigured caller from failing in a
            // place that points away from the cause.
            computed = TimeSpan.Zero;
        }

        return computed;
    }

    private static TimeSpan ComputeBackoff(int attempt, JevClientOptions options)
    {
        double milliseconds = options.InitialRetryDelay.TotalMilliseconds
            * Math.Pow(options.RetryBackoffMultiplier, attempt);

        if (double.IsInfinity(milliseconds) || milliseconds > options.MaxRetryDelay.TotalMilliseconds)
        {
            return options.MaxRetryDelay;
        }

        return TimeSpan.FromMilliseconds(milliseconds);
    }

    /// <summary>
    /// Parses the <c>Retry-After</c> or <c>retry-after-ms</c> header from a response.
    /// Returns null when neither is present or interpretable.
    /// </summary>
    /// <param name="retryAfter">The standard <c>Retry-After</c> header value.</param>
    /// <param name="retryAfterMs">The <c>retry-after-ms</c> header value, as raw text.</param>
    /// <remarks>
    /// <c>retry-after-ms</c> is a non-standard convenience the vendor's SDKs honour, and it is more
    /// precise than the whole-seconds form of <c>Retry-After</c>. When both are present, the
    /// millisecond form wins.
    /// </remarks>
    internal static TimeSpan? ParseRetryHeaders(RetryConditionHeaderValue? retryAfter, string? retryAfterMs)
    {
        if (!string.IsNullOrWhiteSpace(retryAfterMs)
            && double.TryParse(retryAfterMs, NumberStyles.Float, CultureInfo.InvariantCulture, out double milliseconds))
        {
            TimeSpan fromMs = TimeSpan.FromMilliseconds(milliseconds);
            return fromMs < TimeSpan.Zero ? TimeSpan.Zero : fromMs;
        }

        return ParseRetryAfter(retryAfter);
    }

    /// <summary>
    /// Parses the standard <c>Retry-After</c> header. Returns null when it is absent or
    /// uninterpretable.
    /// </summary>
    internal static TimeSpan? ParseRetryAfter(RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is { } delta)
        {
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }

        if (retryAfter.Date is { } date)
        {
            TimeSpan until = date - DateTimeOffset.UtcNow;
            return until < TimeSpan.Zero ? TimeSpan.Zero : until;
        }

        return null;
    }
}
