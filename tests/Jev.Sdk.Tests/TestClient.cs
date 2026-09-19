// TestClient.cs
// Part of Jev.Sdk.Tests. Builds a client wired to a stub transport with deterministic timing,
// so retry behaviour can be asserted without waiting.

using System.Text.Json;
using System.Text.Json.Serialization;
using Jev.Sdk;
using Microsoft.Extensions.Logging;

namespace Jev.Sdk.Tests;

/// <summary>
/// Helpers for building clients under test.
/// </summary>
internal static class TestClient
{
    internal const string TestApiKey = "test-api-key";

    /// <summary>
    /// Builds a client over a stub transport. Retry delays are eliminated and jitter is pinned
    /// so that computed delays are deterministic.
    /// </summary>
    internal static JevClient Create(
        StubTransport transport,
        Action<JevClientOptions>? configure = null,
        ILogger? logger = null)
    {
        JevClientOptions options = new()
        {
            ApiKey = TestApiKey,
            MaxRetries = 3,
            InitialRetryDelay = TimeSpan.Zero,
            MaxRetryDelay = TimeSpan.Zero,
            MaxRetryAfter = TimeSpan.Zero,
            RetryJitterFraction = 0,
        };

        configure?.Invoke(options);

        return new JevClient(
            options,
            transport,
            new StaticApiKeyProvider(TestApiKey),
            logger,
            TimeProvider.System,
            jitterSource: null);
    }

    /// <summary>Builds the canonical success payload from the API documentation.</summary>
    internal static string SuccessJson() =>
        """
        {
          "model": "jev-latest",
          "answers": {
            "is_urgent": { "type": "noul", "noul": 0.92 },
            "department": {
              "type": "choice",
              "choice": "technical",
              "probabilities": { "billing": 0.08, "technical": 0.85, "sales": 0.07 },
              "confidence": 0.82
            },
            "frustration": {
              "type": "score",
              "score": 1.6,
              "legend": { "0": "Calm", "1": "Frustrated", "2": "Very angry" },
              "probabilities": { "0": 0.05, "1": 0.3, "2": 0.65 },
              "confidence": 0.78
            }
          },
          "usage": { "input_tokens": 312, "output_tokens": 48 }
        }
        """;

    /// <summary>Builds a small question map covering all three kinds.</summary>
    internal static Dictionary<string, Question> ThreeQuestions() => new(StringComparer.Ordinal)
    {
        ["is_urgent"] = Question.Noul("Does this convey urgency?"),
        ["department"] = Question.Choice(
            "Which team should handle this?",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["billing"] = "Payments, invoicing, refunds",
                ["technical"] = "Bugs, outages, integrations",
                ["sales"] = "Pricing, upgrades, new accounts",
            }),
        ["frustration"] = Question.Score("How frustrated is the customer?", "Calm", "Frustrated", "Very angry"),
    };
}

/// <summary>
/// A caller-owned state type, with source-generated serialization, used to prove the library
/// never reflects over a caller's types.
/// </summary>
internal sealed class CallerState
{
    public string Subject { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}

/// <summary>Source-generated serialization for <see cref="CallerState"/>.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(CallerState))]
internal sealed partial class CallerStateContext : JsonSerializerContext
{
}
