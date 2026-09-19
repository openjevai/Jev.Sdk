// JevApiException.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the exception hierarchy.
// See requirements/requirements.md, R7.
//
// Type: JevApiException

using System.Net;

namespace Jev.Sdk;

/// <summary>
/// The API returned an error response. Base type for the status-specific exceptions.
/// </summary>
public class JevApiException : JevException
{
    /// <summary>Initialises an exception.</summary>
    /// <param name="statusCode">The HTTP status returned.</param>
    /// <param name="message">The message.</param>
    /// <param name="responseBody">The raw response body, when one was read.</param>
    /// <param name="requestId">
    /// The server's request identifier, when the response carried one. Quote it when escalating to
    /// TypeSafe support so they can find the call.
    /// </param>
    /// <param name="endpoint">
    /// The method and URL that failed, without credentials, query parameters, or fragment.
    /// </param>
    public JevApiException(
        HttpStatusCode statusCode,
        string message,
        string? responseBody = null,
        string? requestId = null,
        string? endpoint = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        RequestId = requestId;
        Endpoint = endpoint;
    }

    /// <summary>Initialises an exception with an inner exception.</summary>
    /// <param name="statusCode">The HTTP status returned.</param>
    /// <param name="message">The message.</param>
    /// <param name="responseBody">The raw response body, when one was read.</param>
    /// <param name="innerException">The underlying cause.</param>
    /// <param name="requestId">The server's request identifier, when present.</param>
    /// <param name="endpoint">The method and URL that failed, without credentials.</param>
    public JevApiException(
        HttpStatusCode statusCode,
        string message,
        string? responseBody,
        Exception innerException,
        string? requestId = null,
        string? endpoint = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        RequestId = requestId;
        Endpoint = endpoint;
    }

    /// <summary>The HTTP status code returned by the API.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// The raw response body, when the server sent one. Never logged by this library, because it
    /// can echo caller-supplied content.
    /// </summary>
    public string? ResponseBody { get; }

    /// <summary>
    /// The server's request identifier, when the response carried one, from the
    /// <c>x-typesafe-request-id</c> header. Safe to log: it is a bounded server-supplied
    /// identifier, not caller content.
    /// </summary>
    public string? RequestId { get; }

    /// <summary>
    /// The method and URL that failed, without credentials, query parameters, or fragment.
    /// </summary>
    public string? Endpoint { get; }
}
