// JevJson.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for serialization.
// See requirements/requirements.md, R14 and R15.
//
// Type: JevJson

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jev.Sdk;

/// <summary>
/// Builds the client's frozen serialization context.
/// </summary>
/// <remarks>
/// A new context is built for every client rather than shared from a static, because passing an
/// options instance to a context constructor encapsulates that instance: it becomes read-only
/// and can never be handed to a second context. Sharing one options instance between a context
/// and anything else is therefore impossible, and attempting it fails at type-initialisation
/// time with an exception that names neither the client nor the cause. Building per client is
/// cheap, because the generated metadata is compiled in and the cache is populated once.
/// </remarks>
internal static class JevJson
{
    /// <summary>
    /// Builds a context with a caller's customisation applied. The caller's action is invoked
    /// once, against a private options instance that the context then encapsulates.
    /// </summary>
    /// <param name="configure">The caller's customisation, or null for the defaults.</param>
    /// <returns>A context whose serialization matches the supplied customisation.</returns>
    /// <exception cref="JevConfigurationException">
    /// The customisation changed naming for dictionary keys, which would silently rewrite
    /// question ids and return answers under names the caller never used.
    /// </exception>
    internal static JevJsonContext CreateContext(Action<JsonSerializerOptions>? configure)
    {
        JsonSerializerOptions options = new()
        {
            // Property names are snake_cased to match the wire format. Dictionary keys are
            // deliberately not: a question id is a caller's own name for an answer, and
            // rewriting it would lose the answer.
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.Strict,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            WriteIndented = false,
        };

        configure?.Invoke(options);

        if (options.DictionaryKeyPolicy is not null)
        {
            throw new JevConfigurationException(
                "JevClientOptions.ConfigureJson must not set JsonSerializerOptions.DictionaryKeyPolicy. " +
                "Question ids are dictionary keys, and rewriting them would return answers under names " +
                "the caller never supplied.");
        }

        // The context constructor encapsulates the options and freezes them. MakeReadOnly must
        // therefore not be called first, and the instance must not be used anywhere else.
        return new JevJsonContext(options);
    }
}
