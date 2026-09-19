// HttpTypeSafeTransport.Requests.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the HTTP transport.
// See requirements/requirements.md, R12 and R19.
//
// Type: HttpTypeSafeTransport
//
// Request construction and authentication. The key is fetched per request and applied as a
// bearer header, and it is never logged: the header is redacted from every diagnostic path in
// this library.

using System.Net.Http.Headers;
using System.Text;

namespace Jev.Sdk;

/// <summary>
/// Request construction and authentication for <see cref="HttpTypeSafeTransport"/>.
/// </summary>
public sealed partial class HttpTypeSafeTransport
{
    private async Task<HttpRequestMessage> BuildRequestAsync(TransportRequest request, CancellationToken cancellationToken)
    {
        HttpRequestMessage message = new(request.Method, request.Uri);

        if (request.Body is { } body)
        {
            message.Content = new ByteArrayContent(body.ToArray());
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
            {
                CharSet = Encoding.UTF8.WebName,
            };
        }

        string? apiKey = await _apiKeyProvider.GetApiKeyAsync(cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return message;
    }
}
