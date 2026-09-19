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
/// Retried automatically with exponential backoff. This exception surfaces only after every
/// retry has been exhausted, so seeing it means the limit is still in force.
/// </remarks>
public sealed class JevRateLimitException : JevApiException
{
    /// <summary>Initialises an exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="retryAfter">The delay the server asked for, when it supplied one.</param>
    /// <param name="responseBody">The raw response body, when one was read.</param>
    public JevRateLimitException(string message, TimeSpan? retryAfter = null, string? responseBody = null)
        : base(HttpStatusCode.TooManyRequests, message, responseBody)
    {
        RetryAfter = retryAfter;
    }

    /// <summary>
    /// The delay the server requested via the <c>Retry-After</c> header, when present.
    /// </summary>
    public TimeSpan? RetryAfter { get; }
}
