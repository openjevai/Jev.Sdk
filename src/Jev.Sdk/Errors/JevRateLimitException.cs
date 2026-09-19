// JevRateLimitException.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for error mapping.
// See requirements/requirements.md, R7 and R8.
//
// Type: JevRateLimitException

using System.Net;

namespace Jev.Sdk;

/// <summary>
/// The account has exceeded its rate limit. HTTP 429.
/// </summary>
/// <remarks>
/// Deliberately not a <see cref="JevServerException"/>: a rate limit is the server declining to
/// serve rather than failing to, and a caller acting on one wants to slow down rather than report
/// an outage.
/// <para>
/// Retried automatically with exponential backoff. This exception surfaces only after every retry
/// has been exhausted, so seeing it means the limit is still in force.
/// </para>
/// </remarks>
public sealed class JevRateLimitException : JevApiException
{
    /// <summary>Initialises an exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="retryAfter">The delay the server asked for, when it supplied one.</param>
    /// <param name="responseBody">The raw response body, when one was read.</param>
    /// <param name="requestId">The server's request identifier, when present.</param>
    /// <param name="endpoint">The method and URL that failed, without credentials.</param>
    public JevRateLimitException(
        string message,
        TimeSpan? retryAfter = null,
        string? responseBody = null,
        string? requestId = null,
        string? endpoint = null)
        : base(HttpStatusCode.TooManyRequests, message, responseBody, requestId, endpoint)
    {
        RetryAfter = retryAfter;
    }

    /// <summary>
    /// The delay the server requested, when present. A caller that wants to wait the limit out and
    /// try again at its own level should use this value.
    /// </summary>
    public TimeSpan? RetryAfter { get; }
}
