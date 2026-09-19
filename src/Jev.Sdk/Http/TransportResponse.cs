// TransportResponse.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the transport seam.
// See requirements/requirements.md, R7, R12 and R13.
//
// Type: TransportResponse

using System.Net;

namespace Jev.Sdk;

/// <summary>
/// The result of one HTTP exchange, with the response body already materialised.
/// </summary>
/// <remarks>
/// The body is delivered as a byte array rather than a stream so that no disposable lifetime ever
/// escapes the transport. Callers of the client never have to think about when to dispose a
/// response.
/// </remarks>
public sealed class TransportResponse
{
    /// <summary>Initialises a response.</summary>
    /// <param name="statusCode">The HTTP status returned.</param>
    /// <param name="body">The response body bytes, or null when there were none.</param>
    /// <param name="retryAfter">The delay the server requested, when it supplied one.</param>
    /// <param name="requestId">
    /// The server's request identifier from the <c>x-typesafe-request-id</c> header, when present.
    /// </param>
    public TransportResponse(
        HttpStatusCode statusCode,
        byte[]? body = null,
        TimeSpan? retryAfter = null,
        string? requestId = null)
    {
        StatusCode = statusCode;
        Body = body;
        RetryAfter = retryAfter;
        RequestId = requestId;
    }

    /// <summary>The HTTP status code.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>The response body bytes, or null when the response had no body.</summary>
    public byte[]? Body { get; }

    /// <summary>
    /// The delay the server requested, from <c>Retry-After</c> or <c>retry-after-ms</c>, when
    /// present and interpretable.
    /// </summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>
    /// The server's request identifier, when the response carried one. Quoting it is what lets
    /// TypeSafe support find a specific call, so it is surfaced on results and on every error.
    /// </summary>
    public string? RequestId { get; }

    /// <summary>True when the status code is below 400.</summary>
    public bool IsSuccess => (int)StatusCode is >= 200 and < 400;

    /// <summary>The response body decoded as UTF-8, or null when there was no body.</summary>
    public string? BodyAsText => Body is null or { Length: 0 } ? null : System.Text.Encoding.UTF8.GetString(Body);
}
