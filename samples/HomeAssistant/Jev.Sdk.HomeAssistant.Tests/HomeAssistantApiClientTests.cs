using System.Net;
using System.Text;
using Jev.Sdk.HomeAssistant;

namespace Jev.Sdk.HomeAssistant.Tests;

public sealed class HomeAssistantApiClientTests
{
    [Fact]
    public async Task GetSupportedEntitiesAsync_reads_states_with_raw_bearer_request()
    {
        RecordingHandler handler = new(HttpStatusCode.OK, """
            [
              {"entity_id":"light.kitchen","state":"off","attributes":{"friendly_name":"Kitchen"}},
              {"entity_id":"sensor.temperature","state":"22","attributes":{"friendly_name":"Temperature"}}
            ]
            """);
        using HttpClient httpClient = new(handler);
        HomeAssistantApiClient client = new(httpClient, new Uri("http://homeassistant.local:8123/"), "token-value");

        IReadOnlyDictionary<string, HomeAssistantEntity> entities = await client.GetSupportedEntitiesAsync(
            new HashSet<string>(["light"], StringComparer.Ordinal),
            CancellationToken.None);

        HomeAssistantEntity entity = Assert.Single(entities).Value;
        Assert.Equal("light.kitchen", entity.EntityId);
        Assert.Equal("Kitchen", entity.FriendlyName);
        Assert.Equal(HttpMethod.Get, handler.Request.Method);
        Assert.Equal("/api/states", handler.Request.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("token-value", handler.Request.Headers.Authorization.Parameter);
    }

    private sealed class RecordingHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public HttpRequestMessage Request { get; private set; } = null!;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            });
        }
    }
}
