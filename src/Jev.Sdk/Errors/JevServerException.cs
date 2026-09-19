// JevServerException.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for error mapping.
// See requirements/requirements.md, R7 and R8.
//
// Type: JevServerException

using System.Net;

namespace Jev.Sdk;

/// <summary>
/// The server failed to process the request. HTTP 5xx.
/// </summary>
/// <remarks>
/// Every 5xx is retried, because these conditions are transient by nature. This exception surfaces
/// only after every retry has been exhausted, so seeing it means the condition persisted.
/// <para>
/// <see cref="JevOverloadedException"/> derives from this type, so catching
/// <c>JevServerException</c> covers a 529 as well. Note that HTTP 429 is deliberately <i>not</i>
/// modelled here: a rate limit is the server declining to serve rather than failing to, and
/// <see cref="JevRateLimitException"/> is its own type.
/// </para>
/// </remarks>
public class JevServerException : JevApiException
{
    /// <summary>Initialises an exception.</summary>
    /// <param name="statusCode">The 5xx status returned.</param>
    /// <param name="message">The message.</param>
    /// <param name="responseBody">The raw response body, when one was read.</param>
    /// <param name="retryAfter">The delay the server asked for, when it supplied one.</param>
    /// <param name="requestId">The server's request identifier, when present.</param>
    /// <param name="endpoint">The method and URL that failed, without credentials.</param>
    public JevServerException(
        HttpStatusCode statusCode,
        string message,
        string? responseBody = null,
        TimeSpan? retryAfter = null,
        string? requestId = null,
        string? endpoint = null)
        : base(statusCode, message, responseBody, requestId, endpoint)
    {
        RetryAfter = retryAfter;
    }

    /// <summary>
    /// The delay the server requested, when present.
    /// </summary>
    public TimeSpan? RetryAfter { get; }
}
