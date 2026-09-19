// HttpTypeSafeTransport.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the HTTP transport.
// See requirements/requirements.md, R12, R13 and R19.
//
// Type: HttpTypeSafeTransport
//
// This partial holds construction and the exchange itself. Retry policy lives in
// HttpTypeSafeTransport.Retry.cs; request construction and header handling live in
// HttpTypeSafeTransport.Requests.cs.

using System.Net;
using System.Net.Http.Headers;

namespace Jev.Sdk;

/// <summary>
/// The built-in transport, backed by <see cref="HttpClient"/>.
/// </summary>
/// <remarks>
/// The client is supplied by the caller or by the host's HTTP client factory, never created
/// here, so socket lifetime is managed where the host expects it to be. The authorization
/// header is set per request rather than on the client, so one client instance can serve
/// callers whose keys differ.
/// </remarks>
public sealed partial class HttpTypeSafeTransport : ITypeSafeTransport, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IApiKeyProvider _apiKeyProvider;
    private readonly bool _ownsHttpClient;
    private bool _disposed;

    /// <summary>Initialises the transport.</summary>
    /// <param name="httpClient">
    /// The HTTP client to send with. When null, a client is created and owned by this
    /// instance and disposed with it.
    /// </param>
    /// <param name="apiKeyProvider">Supplies the API key for each request.</param>
    /// <param name="timeout">Per-attempt timeout applied to the client.</param>
    public HttpTypeSafeTransport(
        HttpClient? httpClient = null,
        IApiKeyProvider? apiKeyProvider = null,
        TimeSpan? timeout = null)
    {
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient();

        if (timeout is { } value)
        {
            _httpClient.Timeout = value;
        }

        _apiKeyProvider = apiKeyProvider ?? new EnvironmentApiKeyProvider();
    }

    /// <summary>
    /// The HTTP client this transport sends with. Disposing the transport disposes it only
    /// when the transport created it.
    /// </summary>
    public HttpClient HttpClient => _httpClient;

    /// <inheritdoc />
    public async Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);

        using HttpRequestMessage message = await BuildRequestAsync(request, cancellationToken).ConfigureAwait(false);

        HttpResponseMessage response;

        try
        {
            response = await _httpClient
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            // The client's own timeout fired rather than the caller's token.
            throw new JevConnectionException(
                $"The request to {request.Uri} timed out after {_httpClient.Timeout}.",
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
            byte[]? body = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);

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

    private static async Task<byte[]?> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content is null)
        {
            return null;
        }

        try
        {
            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
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
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
