// JevBadRequestException.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for error mapping.
// See requirements/requirements.md, R7.
//
// Type: JevBadRequestException

using System.Net;

namespace Jev.Sdk;

/// <summary>
/// The request was malformed. HTTP 400.
/// </summary>
/// <remarks>
/// Distinct from <see cref="JevValidationException"/> (422): a 400 means the body could not be
/// parsed as a request at all, while a 422 means it parsed but failed validation. Both are
/// caller-side errors and neither is retried.
/// </remarks>
public sealed class JevBadRequestException : JevApiException
{
    /// <summary>Initialises an exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="responseBody">The raw response body, when one was read.</param>
    /// <param name="requestId">The server's request identifier, when present.</param>
    /// <param name="endpoint">The method and URL that failed, without credentials.</param>
    public JevBadRequestException(
        string message,
        string? responseBody = null,
        string? requestId = null,
        string? endpoint = null)
        : base(HttpStatusCode.BadRequest, message, responseBody, requestId, endpoint)
    {
    }
}
