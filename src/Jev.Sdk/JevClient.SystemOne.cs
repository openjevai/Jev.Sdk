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

    /// <summary>
    /// Evaluates the state and deserializes the response into a caller-supplied type.
    /// </summary>
    /// <typeparam name="TResponse">
    /// The caller's response type. It replaces the library's own <see cref="SystemOneResponse"/> as
    /// the deserialization target, so a caller can bind exactly the shape they care about.
    /// </typeparam>
    /// <param name="state">The content to evaluate. Plain text, or structured JSON.</param>
    /// <param name="questions">Questions keyed by a name you choose.</param>
    /// <param name="responseTypeInfo">
    /// Source-generated type information for <typeparamref name="TResponse"/>, so the library never
    /// reflects over the caller's type. Required for trimming and Native AOT.
    /// </param>
    /// <param name="model">The model to use. Null uses <see cref="DefaultModel"/>.</param>
    /// <param name="cancellationToken">Cancels the call, including any retry delay.</param>
    /// <returns>The response, read into <typeparamref name="TResponse"/>.</returns>
    /// <remarks>
    /// The response shape in this API is partly caller-determined: the answer ids are the question
    /// ids you chose, and each answer's kind mirrors its question's kind. The vendor's Python SDK
    /// exposes the same capability as <c>response_model=</c>, and its TypeScript SDK infers the shape
    /// with mapped types — something C# generics cannot express, because a type's members cannot be
    /// derived from a type argument's members without structural typing.
    /// <para>
    /// This overload is the C#-expressible equivalent of the Python form. Use it when you want a
    /// concrete type rather than the dictionary in <see cref="SystemOneResponse"/>:
    /// </para>
    /// <code>
    /// sealed class BillingVerdict
    /// {
    ///     public Dictionary&lt;string, Answer&gt; Answers { get; set; } = new();
    ///     public Usage? Usage { get; set; }
    /// }
    /// </code>
    /// <para>
    /// The unmodelled-response pass-through is not lost: if the API adds a field your type does not
    /// declare, adding <c>[JsonExtensionData]</c> to your own model captures it.
    /// </para>
    /// <para>
    /// Local request validation still runs, because it depends only on the questions. No response
    /// validation is possible here: the library does not know the shape you asked for.
    /// </para>
    /// </remarks>
    /// <exception cref="JevRequestValidationException">The request is locally invalid.</exception>
    /// <exception cref="JevConfigurationException">No API key is available.</exception>
    /// <exception cref="JevConnectionException">The exchange failed, or the response could not be read as your type.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public Task<TResponse> SystemOneAsync<TResponse>(
        StructuredValue? state,
        IDictionary<string, Question> questions,
        JsonTypeInfo<TResponse> responseTypeInfo,
        string? model,
        CancellationToken cancellationToken) =>
        SystemOneCoreAsync(state ?? StructuredValue.Null, questions, responseTypeInfo, model, cancellationToken);

    /// <summary>
    /// Evaluates a plain-text state and deserializes the response into a caller-supplied type.
    /// </summary>
    /// <typeparam name="TResponse">The caller's response type.</typeparam>
    /// <param name="state">The text to evaluate.</param>
    /// <param name="questions">Questions keyed by a name you choose.</param>
    /// <param name="responseTypeInfo">Source-generated type information for <typeparamref name="TResponse"/>.</param>
    /// <param name="model">The model to use. Null uses <see cref="DefaultModel"/>.</param>
    /// <param name="cancellationToken">Cancels the call, including any retry delay.</param>
    /// <returns>The response, read into <typeparamref name="TResponse"/>.</returns>
    public Task<TResponse> SystemOneAsync<TResponse>(
        string state,
        IDictionary<string, Question> questions,
        JsonTypeInfo<TResponse> responseTypeInfo,
        string? model,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        return SystemOneCoreAsync(StructuredValue.FromString(state), questions, responseTypeInfo, model, cancellationToken);
    }

    /// <summary>
    /// Evaluates a caller-owned object as the state and deserializes the response into a
    /// caller-supplied type.
    /// </summary>
    /// <typeparam name="TState">The caller's state type.</typeparam>
    /// <typeparam name="TResponse">The caller's response type.</typeparam>
    /// <param name="state">The object to evaluate.</param>
    /// <param name="stateTypeInfo">Source-generated type information for <typeparamref name="TState"/>.</param>
    /// <param name="questions">Questions keyed by a name you choose.</param>
    /// <param name="responseTypeInfo">Source-generated type information for <typeparamref name="TResponse"/>.</param>
    /// <param name="model">The model to use. Null uses <see cref="DefaultModel"/>.</param>
    /// <param name="cancellationToken">Cancels the call, including any retry delay.</param>
    /// <returns>The response, read into <typeparamref name="TResponse"/>.</returns>
    public Task<TResponse> SystemOneAsync<TState, TResponse>(
        TState state,
        JsonTypeInfo<TState> stateTypeInfo,
        IDictionary<string, Question> questions,
        JsonTypeInfo<TResponse> responseTypeInfo,
        string? model,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stateTypeInfo);
        ArgumentNullException.ThrowIfNull(questions);

        return SystemOneCoreAsync(StructuredValue.FromObject(state, stateTypeInfo), questions, responseTypeInfo, model, cancellationToken);
    }

    private async Task<TResponse> SystemOneCoreAsync<TResponse>(
        StructuredValue state,
        IDictionary<string, Question> questions,
        JsonTypeInfo<TResponse> responseTypeInfo,
        string? model,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(questions);
        ArgumentNullException.ThrowIfNull(responseTypeInfo);
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
            responseTypeInfo,
            effectiveModel,
            questions.Count,
            isSystemOne: true,
            cancellationToken).ConfigureAwait(false);
    }

    private Task<SystemOneResponse> SystemOneCoreAsync(
        StructuredValue state,
        IDictionary<string, Question> questions,
        string? model,
        CancellationToken cancellationToken) =>
        // One implementation, so the typed and untyped paths cannot drift. The library's own response
        // type is simply the default TResponse.
        SystemOneCoreAsync(state, questions, _jsonContext.SystemOneResponse, model, cancellationToken);
}
