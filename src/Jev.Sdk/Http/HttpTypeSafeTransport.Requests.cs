// HttpTypeSafeTransport.Requests.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the HTTP transport.
// See requirements/requirements.md, R12, R19 and R24.
//
// Type: HttpTypeSafeTransport
//
// Request construction and authentication. The key is fetched per request and applied as a bearer
// header, and it is never logged: the header is redacted from every diagnostic path in this library.
//
// The header values and the content type are cached as static instances rather than constructed per
// request. HttpHeaders accepts a shared instance, and these values never vary, so building them every
// time is pure garbage on the hot path.

using System.Net.Http.Headers;
using System.Text;

namespace Jev.Sdk;

/// <summary>
/// Request construction and authentication for <see cref="HttpTypeSafeTransport"/>.
/// </summary>
public sealed partial class HttpTypeSafeTransport
{
    /// <summary>Shared, immutable content type. Never mutated after construction.</summary>
    private static readonly MediaTypeHeaderValue s_jsonContentType = new("application/json")
    {
        CharSet = Encoding.UTF8.WebName,
    };

    /// <summary>Shared, immutable accept value. Never mutated after construction.</summary>
    private static readonly MediaTypeWithQualityHeaderValue s_jsonAccept = new("application/json");

    private async Task<HttpRequestMessage> BuildRequestAsync(TransportRequest request, CancellationToken cancellationToken)
    {
        HttpRequestMessage message = new(request.Method, request.Uri);

        if (request.Body is { } body)
        {
            // A ReadOnlyMemory body is handed over directly: MaterializeToMemory avoids copying the
            // payload into a fresh array on every call, which a byte[] constructor argument would do
            // when the source is a slice of a larger buffer.
            message.Content = new ReadOnlyMemoryContent(body);
            message.Content.Headers.ContentType = s_jsonContentType;
        }

        string? apiKey = await _apiKeyProvider.GetApiKeyAsync(cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        message.Headers.Accept.Add(s_jsonAccept);

        // Caller-supplied headers go last, so a caller can override one of the library's defaults.
        // Authorization is set above and is not re-applied here: JevClientOptions rejects an attempt
        // to set it, so a credential can only arrive through the key provider.
        if (request.Headers is { Count: > 0 } headers)
        {
            foreach (KeyValuePair<string, string> header in headers)
            {
                message.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return message;
    }
}
