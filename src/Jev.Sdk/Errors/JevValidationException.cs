// JevValidationException.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for error mapping.
// See requirements/requirements.md, R7.
//
// Type: JevValidationException

using System.Net;

namespace Jev.Sdk;

/// <summary>
/// The API rejected the request body as invalid. HTTP 422.
/// </summary>
/// <remarks>
/// Never retried. The server explains the problem in <see cref="Details"/>, identifying the
/// offending field by path.
/// </remarks>
public sealed class JevValidationException : JevApiException
{
    /// <summary>Initialises an exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="details">Per-field failures reported by the server.</param>
    /// <param name="responseBody">The raw response body, when one was read.</param>
    /// <param name="requestId">The server's request identifier, when present.</param>
    /// <param name="endpoint">The method and URL that failed, without credentials.</param>
    public JevValidationException(
        string message,
        IReadOnlyList<ErrorDetails> details,
        string? responseBody = null,
        string? requestId = null,
        string? endpoint = null)
        : base(HttpStatusCode.UnprocessableEntity, message, responseBody, requestId, endpoint)
    {
        ArgumentNullException.ThrowIfNull(details);
        Details = details;
    }

    /// <summary>Per-field failures reported by the server. Never null.</summary>
    public IReadOnlyList<ErrorDetails> Details { get; }
}
