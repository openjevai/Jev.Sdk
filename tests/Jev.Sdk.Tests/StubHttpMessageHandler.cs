// StubHttpMessageHandler.cs
// Part of Jev.Sdk.Tests. A message handler double, so the built-in transport can be tested
// without a network or a server. The transport is the one part of the client that talks to a
// socket, so it needs a seam below it as well as above it.

using System.Net;
using System.Text;

namespace Jev.Sdk.Tests;

/// <summary>
/// A message handler that returns canned responses and records what it was asked to send.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    /// <summary>Every request this handler was asked to send, in order.</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>Requests that were sent, as a count.</summary>
    public int RequestCount => Requests.Count;

    /// <summary>Queues a response with a body and a JSON content type.</summary>
    public StubHttpMessageHandler Enqueue(
        string body,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(body);

        _responses.Enqueue(_ => new HttpResponseMessage(statusCode)
        {
            Content = new ByteArrayContent(bytes)
            {
                Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") },
            },
        });

        return this;
    }

    /// <summary>Queues a response with no content at all.</summary>
    public StubHttpMessageHandler EnqueueNoContent(HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _responses.Enqueue(_ => new HttpResponseMessage(statusCode) { Content = null });
        return this;
    }

    /// <summary>Queues an empty body with a content header.</summary>
    public StubHttpMessageHandler EnqueueEmpty(HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _responses.Enqueue(_ => new HttpResponseMessage(statusCode) { Content = new ByteArrayContent([]) });
        return this;
    }

    /// <summary>Queues a failure raised while sending.</summary>
    public StubHttpMessageHandler EnqueueThrow(Exception exception)
    {
        _responses.Enqueue(_ => throw exception);
        return this;
    }

    /// <summary>Queues a response computed from the request.</summary>
    public StubHttpMessageHandler Enqueue(Func<HttpRequestMessage, HttpResponseMessage> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _responses.Enqueue(factory);
        return this;
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Requests.Add(request);

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException(
                $"StubHttpMessageHandler received request {Requests.Count} but no response was queued for it.");
        }

        // The last queued response is reused once the queue drains, so a test that expects
        // retries does not have to enumerate every attempt.
        if (_responses.Count == 1)
        {
            return Task.FromResult(_responses.Peek()(request));
        }

        return Task.FromResult(_responses.Dequeue()(request));
    }
}
