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
/// Retried automatically with exponential backoff. This exception surfaces only after every
/// retry has been exhausted, which indicates a sustained condition rather than a blip.
/// </remarks>
public sealed class JevOverloadedException : JevApiException
{
    /// <summary>Initialises an exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="retryAfter">The delay the server asked for, when it supplied one.</param>
    /// <param name="responseBody">The raw response body, when one was read.</param>
    public JevOverloadedException(string message, TimeSpan? retryAfter = null, string? responseBody = null)
        : base((HttpStatusCode)529, message, responseBody)
    {
        RetryAfter = retryAfter;
    }

    /// <summary>
    /// The delay the server requested via the <c>Retry-After</c> header, when present.
    /// </summary>
    public TimeSpan? RetryAfter { get; }
}
