// JevResponse.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the wire contracts.
// See requirements/requirements.md, R2, R6 and R7.
//
// Type: JevResponse
//
// The vendor's SDKs expose the x-typesafe-request-id response header on results as well as errors.
// A caller that logs a successful call needs the same handle as one that reports a failure, so the
// response models share this base rather than carrying the identifier only on the failure path.
//
// The base also carries extension data. The vendor documents unrecognised response fields as a
// forward-compatibility surface: their SDKs ignore them, but a field the API starts returning before
// this library models it must still be reachable rather than silently dropped.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jev.Sdk;

/// <summary>
/// Base for response models that carry the server's request identifier.
/// </summary>
public abstract class JevResponse
{
    /// <summary>
    /// The server's request identifier, when the response carried one, from the
    /// <c>x-typesafe-request-id</c> header.
    /// </summary>
    /// <remarks>
    /// Set by the client after a call completes. Never sent on the wire: it is a response header,
    /// not a body member.
    /// <para>
    /// Quote this value when escalating a problem to TypeSafe support, because it is how they
    /// locate one specific call. It is a bounded, server-supplied identifier, so unlike a caller's
    /// state it is safe to log.
    /// </para>
    /// </remarks>
    [JsonIgnore]
    public string? RequestId { get; set; }

    /// <summary>
    /// Any JSON members present on the wire that this library does not model.
    /// </summary>
    /// <remarks>
    /// Preserved so that a field the API adds before this library models it is still reachable by a
    /// caller, rather than being silently discarded on deserialization.
    /// </remarks>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}
