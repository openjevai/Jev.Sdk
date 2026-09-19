// HttpTypeSafeTransport.Retry.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the HTTP transport.
// See requirements/requirements.md, R8 and R13.
//
// Type: HttpTypeSafeTransport
//
// Retry policy. This is the only place in the library that retries, and it is written as a
// deterministic function of the response so that a test can drive it without timers: the
// delay is computed, then awaited through an injectable gate.
//
// The retry delay is awaited with the caller's token, so cancelling a call cancels the wait
// rather than leaving the caller stuck until the backoff elapses.

using System.Net;

namespace Jev.Sdk;

/// <summary>
/// Retry policy for <see cref="HttpTypeSafeTransport"/>.
/// </summary>
public sealed partial class HttpTypeSafeTransport
{
    private const int OverloadedStatusCode = 529;

    /// <summary>
    /// Returns true when a status code is worth retrying. Only 429 and 529 qualify: the first
    /// is a rate limit and the second a transient overload. A 4xx caused by the request or the
    /// credential cannot be improved by repetition, and a 5xx is not documented by this API.
    /// </summary>
    internal static bool IsRetryableStatus(HttpStatusCode statusCode) =>
        (int)statusCode is (int)HttpStatusCode.TooManyRequests or OverloadedStatusCode;

    /// <summary>
    /// Computes the delay before the next attempt.
    /// </summary>
    /// <param name="attempt">The zero-based count of attempts already made.</param>
    /// <param name="retryAfter">The server-requested delay, when present.</param>
    /// <param name="options">The client's retry settings.</param>
    /// <param name="jitterSource">Supplies a value in [0, 1) for jitter. Null disables jitter.</param>
    /// <returns>The delay to observe before retrying.</returns>
    /// <remarks>
    /// A server-supplied <c>Retry-After</c> takes precedence over the computed backoff,
    /// because the server knows how long its own limit lasts. The result is always bounded by
    /// <see cref="JevClientOptions.MaxRetryDelay"/>.
    /// </remarks>
    internal static TimeSpan ComputeRetryDelay(
        int attempt,
        TimeSpan? retryAfter,
        JevClientOptions options,
        Func<double>? jitterSource)
    {
        TimeSpan computed = retryAfter ?? ComputeBackoff(attempt, options);

        if (jitterSource is not null && options.UseRetryJitter && retryAfter is null)
        {
            // Full jitter: spread attempts across the window so that many callers do not
            // retry in lockstep after a shared limit.
            double factor = 0.5 + (jitterSource() * 0.5);
            computed = TimeSpan.FromTicks((long)(computed.Ticks * factor));
        }

        if (computed > options.MaxRetryDelay)
        {
            computed = options.MaxRetryDelay;
        }

        if (computed < TimeSpan.Zero)
        {
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
}
