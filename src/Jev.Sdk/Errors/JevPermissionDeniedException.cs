// JevPermissionDeniedException.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for error mapping.
// See requirements/requirements.md, R7.
//
// Type: JevPermissionDeniedException

using System.Net;

namespace Jev.Sdk;

/// <summary>
/// The credential is valid but is not permitted to perform the request. HTTP 403.
/// </summary>
/// <remarks>
/// Distinct from <see cref="JevAuthenticationException"/> (401): there the key was rejected, here
/// the key was accepted and the account lacks access. Never retried, because no amount of
/// repetition grants permission.
/// </remarks>
public sealed class JevPermissionDeniedException : JevApiException
{
    /// <summary>Initialises an exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="responseBody">The raw response body, when one was read.</param>
    /// <param name="requestId">The server's request identifier, when present.</param>
    /// <param name="endpoint">The method and URL that failed, without credentials.</param>
    public JevPermissionDeniedException(
        string message,
        string? responseBody = null,
        string? requestId = null,
        string? endpoint = null)
        : base(HttpStatusCode.Forbidden, message, responseBody, requestId, endpoint)
    {
    }
}
