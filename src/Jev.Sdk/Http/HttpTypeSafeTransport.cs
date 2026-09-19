// HttpTypeSafeTransport.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the HTTP transport.
// See requirements/requirements.md, R12, R13 and R19.
//
// Type: HttpTypeSafeTransport
//
// This partial holds construction, disposal, and the exchange itself. Retry policy lives in
// HttpTypeSafeTransport.Retry.cs; request construction and header handling live in
// HttpTypeSafeTransport.Requests.cs.
//
// The per-attempt timeout is enforced with a linked CancellationTokenSource rather than by setting
// HttpClient.Timeout, and that is a deliberate correction rather than a preference. HttpClient
// forbids setting Timeout once it has started a request: a caller who supplies a pooled, singleton,
// or otherwise already-used HttpClient — a normal thing to do, and what IHttpClientFactory hands
// back — would have got an InvalidOperationException from this constructor. A linked token enforces
// the same deadline without mutating an object the caller owns, and it makes the timeout
// distinguishable from the caller's own cancellation.
//
// The client's own Timeout is left untouched. With a per-attempt timeout of 10 seconds and
// HttpClient's default of 100, ours fires first, so a caller-supplied client that sets its own
// shorter timeout still wins.

using System.Net;
using System.Net.Http.Headers;

namespace Jev.Sdk;

/// <summary>
/// The built-in transport, backed by <see cref="HttpClient"/>.
/// </summary>
/// <remarks>
/// The client is supplied by the caller or by the host's HTTP client factory, never created here,
/// so socket lifetime is managed where the host expects it to be. The authorization header is set
/// per request rather than on the client, so one client instance can serve callers whose keys
/// differ.
/// </remarks>
public sealed partial class HttpTypeSafeTransport : ITypeSafeTransport, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IApiKeyProvider _apiKeyProvider;
    private readonly TimeSpan? _timeout;
    private readonly bool _ownsHttpClient;

    // 0 = live, 1 = disposed. Interlocked rather than a bool so the check and the transition are one
    // atomic operation: a plain bool lets two threads both observe "not disposed", or lets a send pass
    // the check and then run against a client that another thread disposes underneath it.
    private int _disposed;

    /// <summary>Initialises the transport.</summary>
    /// <param name="httpClient">
    /// The HTTP client to send with. When null, a client is created and owned by this instance and
    /// disposed with it.
    /// </param>
    /// <param name="apiKeyProvider">Supplies the API key for each request.</param>
    /// <param name="timeout">
    /// Per-attempt timeout. Enforced with a linked cancellation token, so it applies to each attempt
    /// separately and never mutates the supplied client. Null leaves the client's own timeout in
    /// effect.
    /// </param>
    public HttpTypeSafeTransport(
        HttpClient? httpClient = null,
        IApiKeyProvider? apiKeyProvider = null,
        TimeSpan? timeout = null)
    {
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient();
        _apiKeyProvider = apiKeyProvider ?? new EnvironmentApiKeyProvider();

        // Deliberately not assigned to _httpClient.Timeout: see the file header. A zero or negative
        // value disables the per-attempt deadline rather than producing a token that cancels
        // immediately.
        _timeout = timeout is { } value && value > TimeSpan.Zero ? value : null;
    }

    /// <summary>
    /// The HTTP client this transport sends with. Disposing the transport disposes it only when the
    /// transport created it.
    /// </summary>
    public HttpClient HttpClient => _httpClient;

    /// <summary>
    /// The per-attempt timeout this transport enforces, or null when it enforces none of its own.
    /// </summary>
    public TimeSpan? Timeout => _timeout;

    /// <inheritdoc />
    public async Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(request);

        using HttpRequestMessage message = await BuildRequestAsync(request, cancellationToken).ConfigureAwait(false);

        HttpResponseMessage response;

        try
        {
            response = await SendWithTimeoutAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller asked to stop. That is not a timeout, and reporting it as one would tell a
            // caller its own cancellation was a network failure.
            throw;
        }
        catch (OperationCanceledException exception)
        {
            throw new JevConnectionException(
                $"The request to {request.Uri} timed out after {_timeout ?? _httpClient.Timeout}.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new JevConnectionException(
                $"The request to {request.Uri} could not be completed: {exception.Message}",
                exception);
        }

        using (response)
        {
            // The body read is bounded too. A server that sends headers promptly and then stalls the
            // body would otherwise hold the call open past the configured timeout, because
            // ResponseHeadersRead returns as soon as the headers arrive.
            byte[]? body = await ReadBodyWithTimeoutAsync(response, cancellationToken).ConfigureAwait(false);

            return new TransportResponse(
                response.StatusCode,
                body,
                ParseRetryHeaders(
                    response.Headers.RetryAfter,
                    response.Headers.TryGetValues("retry-after-ms", out IEnumerable<string>? msValues)
                        ? msValues.FirstOrDefault()
                        : null),
                JevRequestId.FromHeaders(response.Headers));
        }
    }

    private async Task<HttpResponseMessage> SendWithTimeoutAsync(
        HttpRequestMessage message,
        CancellationToken cancellationToken)
    {
        if (_timeout is not { } timeout)
        {
            return await _httpClient
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }

        // A linked source gives this attempt its own deadline while still honouring the caller's
        // token, without touching the HttpClient.
        using CancellationTokenSource attempt =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        attempt.CancelAfter(timeout);

        return await _httpClient
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, attempt.Token)
            .ConfigureAwait(false);
    }

    private async Task<byte[]?> ReadBodyWithTimeoutAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (_timeout is not { } timeout)
        {
            return await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
        }

        // A separate linked token, because the send's token was already disposed once the headers
        // arrived. This gives the body its own full attempt deadline.
        using CancellationTokenSource attempt =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        attempt.CancelAfter(timeout);

        try
        {
            return await ReadBodyAsync(response, cancellationToken, attempt.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new JevConnectionException(
                $"The response body from {response.RequestMessage?.RequestUri} was not delivered within {timeout}.",
                isProtocolError: false);
        }
    }

    private static async Task<byte[]?> ReadBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken,
        CancellationToken? readToken = null)
    {
        if (response.Content is null)
        {
            return null;
        }

        try
        {
            return await response.Content
                .ReadAsByteArrayAsync(readToken ?? cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or ObjectDisposedException)
        {
            throw new JevConnectionException(
                "The response body could not be read.",
                exception);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Only the thread that performs the 0 -> 1 transition disposes the client. A second concurrent
        // Dispose is a no-op rather than a double disposal or a race on the field.
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
