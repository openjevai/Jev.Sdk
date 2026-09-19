// JevTypeInfoComposer.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for serialization.
// See requirements/requirements.md, R5 and R14.
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
// Composition is cached per caller resolver type, so it happens once rather than per call.

using System.Collections.Concurrent;
using System.Text.Json.Serialization.Metadata;

namespace Jev.Sdk;

/// <summary>
/// Merges a caller-supplied type-info resolver with the library's own.
/// </summary>
internal static class JevTypeInfoComposer
{
    private static readonly ConcurrentDictionary<Type, IJsonTypeInfoResolver> s_merged = new();

    /// <summary>
    /// Returns a resolver that consults <paramref name="callerResolver"/> first and the library's own
    /// context second, so a caller needs to declare only their root type.
    /// </summary>
    /// <param name="callerResolver">The caller's resolver, taken from their own context.</param>
    /// <returns>A composed resolver, cached per caller resolver type.</returns>
    internal static IJsonTypeInfoResolver ComposeWithLibrary(IJsonTypeInfoResolver callerResolver)
    {
        ArgumentNullException.ThrowIfNull(callerResolver);

        return s_merged.GetOrAdd(
            callerResolver.GetType(),
            _ => JsonTypeInfoResolver.Combine(callerResolver, JevJsonContext.Default));
    }
}
