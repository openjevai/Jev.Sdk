// JevClientWarmup.cs
// Part of Jev.Sdk.DependencyInjection. This file is one of the partial-class/file set for
// dependency injection. See requirements/requirements.md, R2 and R23.
//
// Type: JevClientWarmup
//
// The client never fetches the model list on its own, because a constructor cannot await and must
// not perform I/O. A host, however, can await at its own startup, where failure is handleable. This
// type is that opt-in: call it once during host initialisation and the model cache is warm for every
// later cached read.

using Microsoft.Extensions.Logging;

namespace Jev.Sdk.DependencyInjection;

/// <summary>
/// Warms the model cache at host startup, for a host that wants it.
/// </summary>
/// <remarks>
/// This is entirely optional. A host that never calls it still gets a working client: the first
/// cached read fetches the list, and the result is cached from then on.
/// <para>
/// Warming is worth doing when the first request matters, such as an interactive service whose first
/// user should not pay for a discovery call. It is not worth doing when startup latency matters more
/// than the first request, or when the client may never need the model list at all.
/// </para>
/// </remarks>
public static partial class JevClientWarmup
{
    /// <summary>
    /// Fetches the model list so later cached reads are served without a request.
    /// </summary>
    /// <param name="client">The client to warm.</param>
    /// <param name="logger">Logger to record the outcome, or null for silence.</param>
    /// <param name="cancellationToken">Cancels the warmup.</param>
    /// <returns>True when the model list was fetched.</returns>
    /// <remarks>
    /// <para>
    /// A failure is logged and reported as <see langword="false"/> rather than thrown, so a host can
    /// treat warmup as best-effort without a try/catch. Cancellation does propagate, because a host
    /// that cancelled its own startup wants to know rather than silently continuing.
    /// </para>
    /// <para>
    /// Neither parameter is optional, and the logger is required only because C# will not accept a
    /// required parameter after an optional one. D2 locks the token as mandatory at every call site
    /// - cancellation is a decision, not an oversight - and this is an I/O method, so it is not
    /// exempt. Pass <see langword="null"/> for the logger when there is nothing to log to.
    /// </para>
    /// </remarks>
    public static async Task<bool> WarmAsync(
        JevClient client,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        bool warmed = await client.WarmModelsAsync(cancellationToken).ConfigureAwait(false);

        if (logger is not null)
        {
            if (warmed)
            {
                LogWarmed(logger);
            }
            else
            {
                LogNotWarmed(logger);
            }
        }

        return warmed;
    }

    [LoggerMessage(
        EventId = 5000,
        Level = LogLevel.Debug,
        Message = "Model list fetched at startup; later cached reads are served locally.")]
    private static partial void LogWarmed(ILogger logger);

    [LoggerMessage(
        EventId = 5001,
        Level = LogLevel.Information,
        Message = "Model list could not be fetched at startup. The client will retry on first use.")]
    private static partial void LogNotWarmed(ILogger logger);
}
