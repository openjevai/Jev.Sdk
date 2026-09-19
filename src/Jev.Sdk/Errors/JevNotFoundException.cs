// JevNotFoundException.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for error mapping.
// See requirements/requirements.md, R7.
//
// Type: JevNotFoundException

using System.Net;

namespace Jev.Sdk;

/// <summary>
/// The requested resource does not exist. HTTP 404.
/// </summary>
/// <remarks>
/// Never retried. A missing route or resource does not appear because it was asked for again.
/// </remarks>
public sealed class JevNotFoundException : JevApiException
{
    /// <summary>Initialises an exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="responseBody">The raw response body, when one was read.</param>
    /// <param name="requestId">The server's request identifier, when present.</param>
    /// <param name="endpoint">The method and URL that failed, without credentials.</param>
    public JevNotFoundException(
        string message,
        string? responseBody = null,
        string? requestId = null,
        string? endpoint = null)
        : base(HttpStatusCode.NotFound, message, responseBody, requestId, endpoint)
    {
    }
}
