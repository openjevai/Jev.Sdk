// StubTransport.cs
// Part of Jev.Sdk.Tests. A hand-written transport double. No mocking framework is used: a stub
// of the client's own seam is smaller, survives refactoring, and makes the whole client
// testable with no network and no server.

using System.Net;
using System.Text;
using Jev.Sdk;

namespace Jev.Sdk.Tests;

/// <summary>
/// A transport that returns queued responses and records what it was asked to send.
/// </summary>
internal sealed class StubTransport : ITypeSafeTransport
{
    private readonly Queue<Func<TransportRequest, TransportResponse>> _responses = new();

    /// <summary>Every request this transport was asked to send, in order.</summary>
    public List<TransportRequest> Requests { get; } = [];

    /// <summary>The number of requests received.</summary>
    public int RequestCount => Requests.Count;

    /// <summary>The most recent request, or null when none was sent.</summary>
    public TransportRequest? LastRequest => Requests.Count == 0 ? null : Requests[^1];

    /// <summary>Queues a JSON response with the given status.</summary>
    public StubTransport EnqueueJson(string json, HttpStatusCode statusCode = HttpStatusCode.OK, TimeSpan? retryAfter = null)
    {
        byte[] body = Encoding.UTF8.GetBytes(json);

        _responses.Enqueue(_ => new TransportResponse(statusCode, body, retryAfter));
        return this;
    }

    /// <summary>Queues a JSON response for the first request only, then behaves as queued next.</summary>
    public StubTransport EnqueueRaw(byte[]? body, HttpStatusCode statusCode = HttpStatusCode.OK, TimeSpan? retryAfter = null)
    {
        _responses.Enqueue(_ => new TransportResponse(statusCode, body, retryAfter));
        return this;
    }

    /// <summary>Queues a transport-level failure.</summary>
    public StubTransport EnqueueFailure(string message = "connection refused", bool isProtocolError = false)
    {
        _responses.Enqueue(_ => throw new JevConnectionException(message, isProtocolError: isProtocolError));
        return this;
    }

    /// <summary>Queues a response computed from the request.</summary>
    public StubTransport Enqueue(Func<TransportRequest, TransportResponse> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _responses.Enqueue(factory);
        return this;
    }

    /// <inheritdoc />
    public Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Requests.Add(request);

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException(
                $"StubTransport received request {Requests.Count} but no response was queued for it.");
        }

        // The last queued response is reused once the queue is drained, so a test that expects
        // retries does not have to enumerate every attempt.
        if (_responses.Count == 1)
        {
            Func<TransportRequest, TransportResponse> only = _responses.Peek();
            return Task.FromResult(only(request));
        }

        return Task.FromResult(_responses.Dequeue()(request));
    }
}
