// JevClient.SystemOne.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the client.
// See requirements/requirements.md, R1, R5, R13 and R18.
//
// Type: JevClient
//
// Evaluation entry points. Every call is asynchronous and takes a cancellation token with no
// default, so cancellation is a decision the caller makes at each call site rather than
// something that silently does not happen.

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Jev.Sdk;

/// <summary>
/// Evaluation entry points: send a state and a map of typed questions, receive typed answers.
/// </summary>
public sealed partial class JevClient
{
    private const string SystemOnePath = "systemone";

    /// <summary>
    /// Evaluates <paramref name="state"/> against <paramref name="questions"/>.
    /// </summary>
    /// <param name="state">The content to evaluate. Plain text, or structured JSON.</param>
    /// <param name="questions">
    /// Questions keyed by a name you choose. The response uses the same names, unmodified.
    /// </param>
    /// <param name="model">
    /// The model to use. Null uses <see cref="DefaultModel"/>.
    /// </param>
    /// <param name="cancellationToken">Cancels the call, including any retry delay.</param>
    /// <returns>One answer per question, plus the model used and token usage.</returns>
    /// <exception cref="JevRequestValidationException">The request is locally invalid.</exception>
    /// <exception cref="JevConfigurationException">No API key is available.</exception>
    /// <exception cref="JevAuthenticationException">The API rejected the credentials.</exception>
    /// <exception cref="JevValidationException">The API rejected the request body.</exception>
    /// <exception cref="JevRateLimitException">The rate limit is still in force after retries.</exception>
    /// <exception cref="JevOverloadedException">The service is still overloaded after retries.</exception>
    /// <exception cref="JevConnectionException">The exchange failed, or the response was unreadable.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public Task<SystemOneResponse> SystemOneAsync(
        StructuredValue? state,
        IDictionary<string, Question> questions,
        string? model,
        CancellationToken cancellationToken) =>
        SystemOneCoreAsync(state ?? StructuredValue.Null, questions, model, cancellationToken);

    /// <summary>
    /// Evaluates a plain-text state against <paramref name="questions"/>.
    /// </summary>
    /// <param name="state">The text to evaluate.</param>
    /// <param name="questions">Questions keyed by a name you choose.</param>
    /// <param name="model">The model to use. Null uses <see cref="DefaultModel"/>.</param>
    /// <param name="cancellationToken">Cancels the call, including any retry delay.</param>
    /// <returns>One answer per question, plus the model used and token usage.</returns>
    public Task<SystemOneResponse> SystemOneAsync(
        string state,
        IDictionary<string, Question> questions,
        string? model,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        return SystemOneCoreAsync(StructuredValue.FromString(state), questions, model, cancellationToken);
    }

    /// <summary>
    /// Evaluates a caller-owned object as the state, serialized with the caller's own type
    /// information.
    /// </summary>
    /// <typeparam name="TState">The caller's state type.</typeparam>
    /// <param name="state">The object to evaluate.</param>
    /// <param name="stateTypeInfo">
    /// Source-generated type information for <typeparamref name="TState"/>, so the library
    /// never reflects over the caller's types. Required for trimming and Native AOT.
    /// </param>
    /// <param name="questions">Questions keyed by a name you choose.</param>
    /// <param name="model">The model to use. Null uses <see cref="DefaultModel"/>.</param>
    /// <param name="cancellationToken">Cancels the call, including any retry delay.</param>
    /// <returns>One answer per question, plus the model used and token usage.</returns>
    public Task<SystemOneResponse> SystemOneAsync<TState>(
        TState state,
        JsonTypeInfo<TState> stateTypeInfo,
        IDictionary<string, Question> questions,
        string? model,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stateTypeInfo);

        return SystemOneCoreAsync(StructuredValue.FromObject(state, stateTypeInfo), questions, model, cancellationToken);
    }

    /// <summary>
    /// Evaluates raw JSON as the state. Use this when the state is already JSON, such as a
    /// payload read from a file or an incoming message.
    /// </summary>
    /// <param name="state">The JSON to evaluate.</param>
    /// <param name="questions">Questions keyed by a name you choose.</param>
    /// <param name="model">The model to use. Null uses <see cref="DefaultModel"/>.</param>
    /// <param name="cancellationToken">Cancels the call, including any retry delay.</param>
    /// <returns>One answer per question, plus the model used and token usage.</returns>
    public Task<SystemOneResponse> SystemOneAsync(
        JsonElement state,
        IDictionary<string, Question> questions,
        string? model,
        CancellationToken cancellationToken) =>
        SystemOneCoreAsync(StructuredValue.FromJson(state), questions, model, cancellationToken);

    private async Task<SystemOneResponse> SystemOneCoreAsync(
        StructuredValue state,
        IDictionary<string, Question> questions,
        string? model,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(questions);
        cancellationToken.ThrowIfCancellationRequested();

        string effectiveModel = string.IsNullOrWhiteSpace(model) ? _options.DefaultModel : model;

        if (_options.ValidateRequests)
        {
            ValidateSystemOneRequest(state, questions, effectiveModel);
        }

        await ResolveApiKeyAsync(cancellationToken).ConfigureAwait(false);

        SystemOneRequest body = new()
        {
            State = state,
            Model = effectiveModel,
            Questions = new Dictionary<string, Question>(questions, StringComparer.Ordinal),
        };

        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(body, _jsonContext.SystemOneRequest);

        return await SendAsync(
            HttpMethod.Post,
            SystemOnePath,
            payload,
            _jsonContext.SystemOneResponse,
            effectiveModel,
            questions.Count,
            isSystemOne: true,
            cancellationToken).ConfigureAwait(false);
    }
}
