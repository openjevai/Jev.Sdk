// JevRequestId.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for error mapping.
// See requirements/requirements.md, R7.
//
// Type: JevRequestId
//
// The vendor's SDKs surface the x-typesafe-request-id response header as a request id on both
// successful results and errors. It is the handle their support asks for when investigating a
// specific call, so a client that discards it leaves a caller unable to escalate a problem. The
// header is a bounded, server-supplied identifier, so unlike caller content it is safe to log and
// safe to attach to a span.

namespace Jev.Sdk;

/// <summary>
/// The server-assigned identifier for a request, when the response carried one.
/// </summary>
public static class JevRequestId
{
    /// <summary>The response header carrying the request identifier.</summary>
    public const string HeaderName = "x-typesafe-request-id";

    /// <summary>Reads the request id from a set of response headers.</summary>
    /// <param name="headers">The response headers. Null yields null.</param>
    /// <returns>The request id, or null when the header is absent or blank.</returns>
    public static string? FromHeaders(System.Net.Http.Headers.HttpResponseHeaders? headers)
    {
        if (headers is null || !headers.TryGetValues(HeaderName, out IEnumerable<string>? values))
        {
            return null;
        }

        foreach (string value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}
