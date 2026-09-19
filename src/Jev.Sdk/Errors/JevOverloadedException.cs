// JevOverloadedException.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for error mapping.
// See requirements/requirements.md, R7 and R8.
//
// Type: JevOverloadedException

using System.Net;

namespace Jev.Sdk;

/// <summary>
/// TypeSafe is temporarily overloaded. HTTP 529.
/// </summary>
/// <remarks>
/// Derives from <see cref="JevServerException"/>, so a caller can catch every server-side condition
/// with one type, or this one when the distinction between an overload and any other 5xx matters.
/// <para>
/// Retried automatically with exponential backoff, because 529 falls inside the retryable 5xx
/// range. This exception surfaces only after every retry has been exhausted, which indicates a
/// sustained condition rather than a blip.
/// </para>
/// </remarks>
public sealed class JevOverloadedException : JevServerException
{
    /// <summary>The status code TypeSafe documents for a temporary overload.</summary>
    public const int StatusCodeValue = 529;

    /// <summary>Initialises an exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="retryAfter">The delay the server asked for, when it supplied one.</param>
    /// <param name="responseBody">The raw response body, when one was read.</param>
    /// <param name="requestId">The server's request identifier, when present.</param>
    /// <param name="endpoint">The method and URL that failed, without credentials.</param>
    public JevOverloadedException(
        string message,
        TimeSpan? retryAfter = null,
        string? responseBody = null,
        string? requestId = null,
        string? endpoint = null)
        : base((HttpStatusCode)StatusCodeValue, message, responseBody, retryAfter, requestId, endpoint)
    {
    }
}
