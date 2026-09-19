// JevAuthenticationException.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for error mapping.
// See requirements/requirements.md, R7.
//
// Type: JevAuthenticationException

using System.Net;

namespace Jev.Sdk;

/// <summary>
/// The API rejected the request as unauthenticated: the key is missing, malformed, or no
/// longer valid. HTTP 401.
/// </summary>
/// <remarks>
/// Never retried. Retrying cannot fix a credential, and doing so only wastes the caller's
/// time.
/// </remarks>
public sealed class JevAuthenticationException : JevApiException
{
    /// <summary>Initialises an exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="responseBody">The raw response body, when one was read.</param>
    public JevAuthenticationException(string message, string? responseBody = null)
        : base(HttpStatusCode.Unauthorized, message, responseBody)
    {
    }
}
