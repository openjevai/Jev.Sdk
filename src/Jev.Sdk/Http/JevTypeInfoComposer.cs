// JevTypeInfoComposer.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for serialization.
// See requirements/requirements.md, R5, R14 and R24.
//
// Type: JevTypeInfoComposer
//
// A caller who binds the response to their own type supplies source-generated type information for
// that root type. Their context knows nothing about the library's types, so a member such as
// Dictionary<string, Answer> fails to resolve: the answer union and its concrete kinds live in the
// library's context, not the caller's. Observed failure, before this existed:
//
//   NotSupportedException: JsonTypeInfo metadata for type 'Jev.Sdk.NoulAnswer' was not provided by
//   TypeInfoResolver of type 'MyApp.MyContext'. The unsupported member type is located on type
//   'Jev.Sdk.Answer'. Path: $.answers.is_urgent
//
// Requiring the caller to annotate the library's types from their own context would be unreasonable:
// it leaks the library's type graph into caller code and breaks whenever the library adds a type.
// Composing the two resolvers is the correct fix — the caller's types win where they exist, and
// anything they do not declare falls through to the library's own context.
//
// Composition is resolved ONCE per caller resolver instance and cached, and the resulting JsonTypeInfo
// is cached too. A JsonTypeInfo is immutable and thread-safe once configured, so caching it is both
// correct and the reason this costs nothing per call. Building fresh options per response — the first
// version of this — allocated a JsonSerializerOptions on every call and threw away the serializer's
// own metadata cache each time, which is measurable overhead on a hot path.

using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Jev.Sdk;

/// <summary>
/// Merges a caller-supplied type-info resolver with the library's own, and caches the result.
/// </summary>
internal static class JevTypeInfoComposer
{
    /// <summary>
    /// Cache keyed by the caller's resolver instance and the requested type. The resolver is compared
    /// by reference because a compiled context is a long-lived singleton in practice, which is what
    /// makes the cache hit on every call after the first.
    /// </summary>
    private static readonly ConcurrentDictionary<CacheKey, JsonTypeInfo> s_cache = new();

    private static readonly JsonSerializerOptions s_libraryOnlyOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        TypeInfoResolver = JevJsonContext.Default,
    };

    /// <summary>
    /// Returns type information that resolves both the caller's root type and the library's own types.
    /// </summary>
    /// <param name="callerOptions">The options the caller's type information came from.</param>
    /// <returns>
    /// The caller's own type information when there is nothing to merge, otherwise a cached instance
    /// whose resolver falls through from the caller's to the library's.
    /// </returns>
    internal static JsonTypeInfo<TResponse> Resolve<TResponse>(JsonSerializerOptions callerOptions)
    {
        ArgumentNullException.ThrowIfNull(callerOptions);

        IJsonTypeInfoResolver? callerResolver = callerOptions.TypeInfoResolver;

        // The common case: the caller passed no resolver, or one that already resolves everything
        // because it is the library's own context. Nothing to merge, so the supplied metadata is used
        // as-is with no lookup at all.
        if (callerResolver is null or JevJsonContext)
        {
            return (JsonTypeInfo<TResponse>)s_libraryOnlyOptions.GetTypeInfo(typeof(TResponse));
        }

        JsonTypeInfo resolved = s_cache.GetOrAdd(
            new CacheKey(callerResolver, typeof(TResponse)),
            key => Build(callerOptions, key));

        return (JsonTypeInfo<TResponse>)resolved;
    }

    private static JsonTypeInfo Build(JsonSerializerOptions callerOptions, CacheKey key)
    {
        // One options instance per (resolver, type) pair, built once and cached. A JsonTypeInfo is
        // bound to the options that produced it, so the options must be owned here rather than created
        // per call.
        JsonSerializerOptions merged = new(callerOptions)
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(
                callerOptions.TypeInfoResolver!,
                JevJsonContext.Default),
        };

        return merged.GetTypeInfo(key.Type);
    }

    private readonly record struct CacheKey(IJsonTypeInfoResolver Resolver, Type Type);
}
