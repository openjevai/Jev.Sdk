// WorldPackage.cs
// Part of Jev.Sdk.World. Opens a world from a .zip, a folder, a markdown document, or JSON.
//
// A published world is a package: a zip holding the world document plus anything it wants beside it.
// That keeps a world self-contained and shippable as one file, and it means the player executable
// only has to be handed one thing.
//
// Detection is by content, not by extension alone, because a world author will produce all of these by
// accident:
//
//   .zip                       unpacked to a temporary folder, then the world document inside is found
//   folder                     the world document inside is found
//   .md                        the document is the world
//   .json                      an object with a "rules" property is a world definition
//   .json with an array        a bare list of worlds: the first that loads is played
//   .json holding a string     the string is markdown, and is parsed as a world document
//
// The last three are what "just text, autodetect" means in practice: a generator that emits a JSON
// array, or a JSON object wrapping markdown, is still a playable world.

using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jev.Sdk.World;

/// <summary>
/// A world opened from a package, folder, document, or definition.
/// </summary>
/// <param name="World">The loaded world.</param>
/// <param name="SourceName">Where it came from, for display.</param>
/// <param name="WorkingDirectory">
/// The folder the package was unpacked into, or null when nothing was unpacked. The caller owns it and
/// should delete it when finished.
/// </param>
public sealed record OpenedWorld(WorldDefinition World, string SourceName, string? WorkingDirectory)
    : IDisposable
{
    /// <summary>Removes the temporary folder, when there is one.</summary>
    public void Dispose()
    {
        if (WorkingDirectory is null || !Directory.Exists(WorkingDirectory))
        {
            return;
        }

        try
        {
            Directory.Delete(WorkingDirectory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover temporary folder is untidy, not a failure. Saying so beats throwing from a
            // Dispose that the caller cannot reasonably handle.
            Console.WriteLine($"Note: could not remove {WorkingDirectory}: {exception.Message}");
        }
    }
}

/// <summary>
/// Opens worlds from packages, folders, documents, and definitions.
/// </summary>
public static class WorldPackage
{
    /// <summary>The file names a package's world document is looked for under, in order.</summary>
    private static readonly string[] PreferredNames =
    [
        "world.md",
        "world.json",
        "game.md",
        "game.json",
        "world.game.md",
        "README.md",
    ];

    /// <summary>
    /// Opens a world from any supported source.
    /// </summary>
    /// <param name="path">A zip, a folder, a markdown document, or a JSON file.</param>
    /// <returns>The opened world, with its temporary folder when one was created.</returns>
    /// <exception cref="WorldLoadException">The source cannot be read, or holds no loadable world.</exception>
    public static OpenedWorld Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (Directory.Exists(path))
        {
            return new OpenedWorld(LoadFromDirectory(path), Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)), null);
        }

        if (!File.Exists(path))
        {
            throw new WorldLoadException($"No such world: {path}");
        }

        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            string temporary = Path.Combine(Path.GetTempPath(), $"jev-world-{Guid.NewGuid():N}");

            try
            {
                ZipFile.ExtractToDirectory(path, temporary);

                return new OpenedWorld(LoadFromDirectory(temporary), Path.GetFileName(path), temporary);
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                // The folder may exist and be half-written; leaving it behind would accumulate.
                TryDelete(temporary);

                throw new WorldLoadException($"'{Path.GetFileName(path)}' could not be opened as a zip: {exception.Message}", exception);
            }
        }

        return new OpenedWorld(LoadDocument(path), Path.GetFileName(path), null);
    }

    /// <summary>
    /// Finds and loads the world document inside a folder.
    /// </summary>
    /// <param name="directory">The folder, either a package's or a world's own.</param>
    /// <returns>The loaded world.</returns>
    /// <exception cref="WorldLoadException">No world document was found.</exception>
    /// <remarks>
    /// Preferred names first, then anything that looks like a world document at the top level. A
    /// package is allowed exactly one world; two would be ambiguous, so the extra files are treated as
    /// supporting material rather than as further worlds.
    /// </remarks>
    public static WorldDefinition LoadFromDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        foreach (string name in PreferredNames)
        {
            string candidate = Path.Combine(directory, name);

            if (File.Exists(candidate))
            {
                try
                {
                    return LoadDocument(candidate);
                }
                catch (WorldLoadException)
                {
                    // Keep looking: a README that is not a world should not stop the real document
                    // later in the list from being found.
                }
            }
        }

        List<string> candidates =
        [
            .. Directory.EnumerateFiles(directory, "*.md", SearchOption.TopDirectoryOnly),
            .. Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly),
        ];

        foreach (string candidate in candidates.OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            try
            {
                return LoadDocument(candidate);
            }
            catch (WorldLoadException)
            {
                continue;
            }
        }

        throw new WorldLoadException(
            $"No world document found in '{directory}'. Expected one of {string.Join(", ", PreferredNames)}, or any .md or .json file holding a world.");
    }

    /// <summary>
    /// Loads a world from a single document, detecting its form.
    /// </summary>
    /// <param name="path">The document.</param>
    /// <returns>The loaded world.</returns>
    /// <exception cref="WorldLoadException">The document holds no loadable world.</exception>
    public static WorldDefinition LoadDocument(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string text = ReadText(path);

        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return LoadFromJsonText(text, path);
        }

        return MarkdownWorldLoader.Parse(text, path);
    }

    /// <summary>
    /// Loads a world from JSON text, accepting a definition, a list, or an embedded document.
    /// </summary>
    /// <param name="json">The text.</param>
    /// <param name="sourceName">Name for error messages.</param>
    /// <returns>The loaded world.</returns>
    /// <exception cref="WorldLoadException">Nothing loadable was found.</exception>
    public static WorldDefinition LoadFromJsonText(string json, string? sourceName = null)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonNode? node;

        try
        {
            node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException exception)
        {
            throw new WorldLoadException($"{sourceName ?? "the document"} is not valid JSON: {exception.Message}", exception);
        }

        return LoadFromNode(node, sourceName);
    }

    private static WorldDefinition LoadFromNode(JsonNode? node, string? sourceName)
    {
        switch (node)
        {
            case null:
                throw new WorldLoadException($"{sourceName ?? "the document"} is empty.");

            // A world definition: it has the members a world has.
            case JsonObject world when world.ContainsKey("rules") && world.ContainsKey("state"):
                return WorldLoader.Parse(world.ToJsonString());

            // A list of worlds: try each, and report the last failure if none loads, because a
            // generator emitting several candidates should not have to say which one is the real one.
            case JsonArray array:
                {
                    WorldLoadException? last = null;

                    foreach (JsonNode? item in array)
                    {
                        try
                        {
                            return LoadFromNode(item, sourceName);
                        }
                        catch (WorldLoadException exception)
                        {
                            last = exception;
                        }
                    }

                    throw last ?? new WorldLoadException($"{sourceName ?? "the document"} holds an empty list."); 
                }

            // A wrapped document: {"world": "..."} or {"markdown": "..."} holding the markdown, or a
            // plain string that is itself the markdown. This is the "json property with json content"
            // case: a generator can hand back both a description and a document.
            case JsonObject wrapper:
                {
                    foreach (string key in new[] { "world", "markdown", "document", "text", "content" })
                    {
                        if (wrapper[key] is JsonValue value
                            && value.TryGetValue(out string? embedded)
                            && !string.IsNullOrWhiteSpace(embedded))
                        {
                            return MarkdownWorldLoader.Parse(embedded, sourceName);
                        }

                        if (wrapper[key] is JsonObject nested)
                        {
                            return LoadFromNode(nested, sourceName);
                        }
                    }

                    throw new WorldLoadException(
                        $"{sourceName ?? "the document"} is a JSON object with no 'rules' and 'state', and no 'world', 'markdown', 'document', 'text' or 'content' member holding one.");
                }

            case JsonValue scalar when scalar.TryGetValue(out string? markdown) && !string.IsNullOrWhiteSpace(markdown):
                return MarkdownWorldLoader.Parse(markdown, sourceName);

            default:
                throw new WorldLoadException($"{sourceName ?? "the document"} holds no world.");
        }
    }

    private static string ReadText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new WorldLoadException($"Could not read '{path}': {exception.Message}", exception);
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Note: could not remove {directory}: {exception.Message}");
        }
    }
}
