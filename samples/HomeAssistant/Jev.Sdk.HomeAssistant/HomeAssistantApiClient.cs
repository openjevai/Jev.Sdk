using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Jev.Sdk.HomeAssistant;

/// <summary>Small raw-HTTP adapter used only by this sample; it is not a Home Assistant SDK.</summary>
internal sealed class HomeAssistantApiClient(HttpClient httpClient, Uri baseUri, string accessToken)
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly Uri _baseUri = baseUri;
    private readonly string _accessToken = accessToken;

    public async Task<IReadOnlyDictionary<string, HomeAssistantEntity>> GetSupportedEntitiesAsync(
        IReadOnlySet<string> supportedDomains,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest(HttpMethod.Get, "api/states");
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using JsonDocument document = JsonDocument.Parse(body);
        Dictionary<string, HomeAssistantEntity> entities = new(StringComparer.Ordinal);

        foreach (JsonElement value in document.RootElement.EnumerateArray())
        {
            if (!value.TryGetProperty("entity_id", out JsonElement idElement)
                || idElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            string? entityId = idElement.GetString();

            if (string.IsNullOrWhiteSpace(entityId))
            {
                continue;
            }

            int separator = entityId.IndexOf('.', StringComparison.Ordinal);
            string domain = separator > 0 ? entityId[..separator] : string.Empty;

            if (!supportedDomains.Contains(domain))
            {
                continue;
            }

            string state = value.TryGetProperty("state", out JsonElement stateElement)
                && stateElement.ValueKind == JsonValueKind.String
                ? stateElement.GetString() ?? "unknown"
                : "unknown";
            string friendlyName = ReadFriendlyName(value) ?? entityId;
            entities[entityId] = new HomeAssistantEntity(entityId, state, friendlyName);
        }

        return entities;
    }

    public async Task ExecuteAsync(HomeAssistantCommandPlan plan, CancellationToken cancellationToken)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new { entity_id = plan.EntityId });
        using HttpRequestMessage request = CreateRequest(
            HttpMethod.Post,
            $"api/services/{plan.Domain}/{plan.Service}",
            payload);
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task<string?> GetStateAsync(string entityId, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest(HttpMethod.Get, $"api/states/{entityId}");
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using JsonDocument document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty("state", out JsonElement state)
            && state.ValueKind == JsonValueKind.String
            ? state.GetString()
            : null;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath, byte[]? payload = null)
    {
        HttpRequestMessage request = new(method, new Uri(_baseUri, relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        if (payload is not null)
        {
            request.Content = new ByteArrayContent(payload);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        return request;
    }

    private static string? ReadFriendlyName(JsonElement entity)
    {
        return entity.TryGetProperty("attributes", out JsonElement attributes)
            && attributes.TryGetProperty("friendly_name", out JsonElement name)
            && name.ValueKind == JsonValueKind.String
            ? name.GetString()
            : null;
    }
}
