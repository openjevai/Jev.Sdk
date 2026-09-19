// LiveClient.cs
// Part of Jev.Sdk.IntegrationTests. Builds the client the live tests share.
//
// One client for the whole assembly: the live tests are about the library talking to a real server,
// not about construction, and a fresh client per test would multiply connections for no gain. The
// client is safe to share, which is asserted separately in the unit suite.
//
// Every setting here is a deliberate choice rather than a default, noted so it can be argued with:
//
//   MaxRetries      1   A live test that retries four times against a throttled account turns one
//                       failure into several seconds of waiting and hides the throttle. One retry
//                       proves the path works without turning the suite into a load generator.
//   ValidateRequests true Local validation still runs, because a live test whose request was locally
//                       invalid should say so rather than spend a round trip learning it.
//
// No API key is logged, echoed, or included in a failure message anywhere in this project.

using Jev.Sdk;

namespace Jev.Sdk.IntegrationTests;

/// <summary>
/// Owns the shared client used by the live tests.
/// </summary>
internal static class LiveClient
{
    private static readonly Lazy<JevClient> s_client = new(Create);

    /// <summary>The shared client. Only valid to touch when <see cref="LiveSettings.IsConfigured"/>.</summary>
    public static JevClient Shared => s_client.Value;

    /// <summary>
    /// Creates a client for a caller that needs its own instance, such as a disposal test.
    /// </summary>
    /// <returns>A new live client over the same settings.</returns>
    public static JevClient Create() => new(new JevClientOptions
    {
        ApiKey = LiveSettings.ApiKey,
        MaxRetries = 1,
        ValidateRequests = true,
    });
}
