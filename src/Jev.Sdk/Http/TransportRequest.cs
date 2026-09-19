// TransportRequest.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the transport seam.
// See requirements/requirements.md, R12 and R13.
//
// Type: TransportRequest

namespace Jev.Sdk;

/// <summary>
/// A single HTTP exchange as the transport sees it, independent of what the exchange means.
/// </summary>
/// <remarks>
/// The transport knows how to send a request and interpret a status code. It knows nothing
/// about evaluations, questions, or answers, which is what lets a test substitute it and
/// exercise the entire client with no network and no server.
/// </remarks>
public sealed class TransportRequest
{
    /// <summary>Initialises a request.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="uri">The absolute request URI.</param>
    /// <param name="body">The serialized request body, or null when there is none.</param>
    public TransportRequest(HttpMethod method, Uri uri, byte[]? body = null)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(uri);

        Method = method;
        Uri = uri;

        // Assigning a byte[] straight to ReadOnlyMemory<byte>? would turn null into an empty
        // body, because a null array has an implicit conversion to a default ReadOnlyMemory.
        // The same trap applies to a conditional expression whose branches are null and a
        // non-nullable value: its type is the non-nullable one, so the null branch becomes a
        // default value rather than an absent one. The cast is what keeps null null.
        Body = body is null ? (ReadOnlyMemory<byte>?)null : new ReadOnlyMemory<byte>(body);
    }

    /// <summary>The HTTP method.</summary>
    public HttpMethod Method { get; }

    /// <summary>The absolute request URI.</summary>
    public Uri Uri { get; }

    /// <summary>
    /// The serialized request body, or null when the request has no body at all. Distinct from
    /// an empty body, which is what a GET must not send.
    /// </summary>
    public ReadOnlyMemory<byte>? Body { get; }

    /// <summary>True when this request has no body.</summary>
    public bool HasBody => Body is not null;
}
