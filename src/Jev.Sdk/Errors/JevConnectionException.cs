// JevConnectionException.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for error mapping.
// See requirements/requirements.md, R7 and R8.
//
// Type: JevConnectionException

namespace Jev.Sdk;

/// <summary>
/// A transport-level failure happened: the connection could not be established, the response body
/// could not be read, or the body was not valid JSON.
/// </summary>
/// <remarks>
/// Connection failures are retried, because they are usually transient. A malformed body is not
/// retried: a body that cannot be parsed will not parse on a second attempt, and retrying a broken
/// contract only adds latency. <see cref="IsProtocolError"/> distinguishes the two.
/// </remarks>
public class JevConnectionException : JevException
{
    /// <summary>Initialises an exception with no underlying cause.</summary>
    /// <param name="message">The message.</param>
    /// <param name="isProtocolError">
    /// True when the connection succeeded but the response could not be understood.
    /// </param>
    /// <param name="responseBody">The raw body, when one was read.</param>
    /// <param name="requestId">The server's request identifier, when the response carried one.</param>
    public JevConnectionException(
        string message,
        bool isProtocolError = false,
        string? responseBody = null,
        string? requestId = null)
        : base(message)
    {
        IsProtocolError = isProtocolError;
        ResponseBody = responseBody;
        RequestId = requestId;
    }

    /// <summary>Initialises an exception with an underlying cause.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The underlying transport failure.</param>
    /// <param name="isProtocolError">
    /// True when the connection succeeded but the response could not be understood.
    /// </param>
    /// <param name="responseBody">The raw body, when one was read.</param>
    /// <param name="requestId">The server's request identifier, when the response carried one.</param>
    public JevConnectionException(
        string message,
        Exception innerException,
        bool isProtocolError = false,
        string? responseBody = null,
        string? requestId = null)
        : base(message, innerException)
    {
        IsProtocolError = isProtocolError;
        ResponseBody = responseBody;
        RequestId = requestId;
    }

    /// <summary>
    /// True when the server responded but the response could not be interpreted, as opposed to a
    /// failure to reach the server at all.
    /// </summary>
    public bool IsProtocolError { get; }

    /// <summary>The raw body, when one was read. Never logged by this library.</summary>
    public string? ResponseBody { get; }

    /// <summary>
    /// The server's request identifier, when the response carried one.
    /// </summary>
    public string? RequestId { get; }
}
