// MarkdownWorldLoader.cs
// Part of Jev.Sdk.World. Reads a world from a markdown document.
//
// A world is meant to be written by a person, and a person writes prose. JSON is a poor place to
// explain why a rule exists, and comments are a poor substitute for a paragraph. So the canonical form
// of a world is a markdown document: front matter for the identity, headings and prose for the human
// reader, and one fenced JSON block under each heading carrying the part of the machine definition
// that heading is about.
//
// The prose is not decoration. It is read by whoever edits the world next, and the two prompt-wording
// bugs recorded in README.md are exactly the kind of thing that needs a paragraph of explanation
// sitting next to the instruction it explains.
//
// Structure:
//
//   ---                       scalar identity: id, title, goal, schemaVersion, turnLimit
//   id: escape-the-room
//   ---
//
//   # Escape the Room          H1 is the title when front matter omits it
//   Prose goal...
//
//   ## Examples                a bullet list
//   - grab the key
//
//   ## State                   a ```json block holding the state array
//   ## State template          a ```text block holding the template
//   ## Judgements              a ```json block holding the judgements array
//   ## Rules                   a ```json block holding the rules array
//   ## Ending                  a ```json block holding the ending object
//
// Anything else in the document is ignored, so a world can carry as much explanation as it deserves.

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jev.Sdk.World;

/// <summary>
/// Loads a world from a markdown document.
/// </summary>
public static class MarkdownWorldLoader
{
    /// <summary>Recognised section headings, matched case-insensitively.</summary>
    private static readonly string[] StateSections = ["state"];
    private static readonly string[] TemplateSections = ["state template", "state description", "template"];
    private static readonly string[] JudgementSections = ["judgements", "judgments", "questions"];
    private static readonly string[] RuleSections = ["rules"];
    private static readonly string[] EndingSections = ["ending", "endings"];
    private static readonly string[] ExampleSections = ["examples", "try"];

    /// <summary>
    /// Loads a world from a markdown file.
    /// </summary>
    /// <param name="path">Path to the markdown document.</param>
    /// <returns>A validated world.</returns>
    /// <exception cref="WorldLoadException">The file cannot be read, or the world is incomplete or inconsistent.</exception>
    public static WorldDefinition Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string markdown;

        try
        {
            markdown = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new WorldLoadException($"Could not read the world file '{path}': {exception.Message}", exception);
        }

        try
        {
            return Parse(markdown, path);
        }
        catch (WorldLoadException exception)
        {
            throw new WorldLoadException($"{path}: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// Parses a world from markdown text.
    /// </summary>
    /// <param name="markdown">The document.</param>
    /// <param name="sourceName">The file name, for error messages. Optional.</param>
    /// <returns>A validated world.</returns>
    /// <exception cref="WorldLoadException">The world is incomplete or inconsistent.</exception>
    public static WorldDefinition Parse(string markdown, string? sourceName = null)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        Document document = Document.ParseDocument(markdown);

        JsonObject root = [];

        root["schemaVersion"] = document.Scalar("schemaVersion", WorldLoader.SupportedSchemaVersion);
        root["id"] = Require(document.Scalar("id", null), "the front matter needs an 'id'");
        root["title"] = document.Scalar("title", null) ?? document.Heading ?? throw new WorldLoadException("the document needs a title, as 'title:' in the front matter or as an H1 heading.");
        root["goal"] = document.Scalar("goal", null) ?? document.FirstParagraph ?? string.Empty;

        if (int.TryParse(document.Scalar("turnLimit", null), NumberStyles.Integer, CultureInfo.InvariantCulture, out int turnLimit))
        {
            root["turnLimit"] = turnLimit;
        }

        if (document.SectionJson(StateSections) is { } state)
        {
            root["state"] = state;
        }

        if (document.SectionText(TemplateSections) is { } template)
        {
            root["stateTemplate"] = template;
        }

        if (document.SectionJson(JudgementSections) is { } judgements)
        {
            root["judgements"] = judgements;
        }

        if (document.SectionJson(RuleSections) is { } rules)
        {
            root["rules"] = rules;
        }

        if (document.SectionJson(EndingSections) is { } ending)
        {
            root["ending"] = ending;
        }

        JsonArray examples = document.SectionBullets(ExampleSections);

        if (examples.Count > 0)
        {
            root["examples"] = examples;
        }

        if (root["state"] is null || root["judgements"] is null || root["rules"] is null)
        {
            List<string> missing = [];

            if (root["state"] is null)
            {
                missing.Add("'## State'");
            }

            if (root["judgements"] is null)
            {
                missing.Add("'## Judgements'");
            }

            if (root["rules"] is null)
            {
                missing.Add("'## Rules'");
            }

            throw new WorldLoadException(
                $"the document is missing {string.Join(", ", missing)}. Each needs a fenced json block under its heading.");
        }

        if (root["stateTemplate"] is null)
        {
            throw new WorldLoadException(
                "the document needs a '## State template' section with a fenced text block, which is how the model is told the situation.");
        }

        // Hand off to the JSON loader for validation, so there is exactly one place that decides what a
        // valid world is. A markdown world and a JSON world are held to the same standard.
        return WorldLoader.Parse(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string Require(string? value, string message) =>
        string.IsNullOrWhiteSpace(value) ? throw new WorldLoadException(message) : value;

    /// <summary>The parsed shape of a world document: scalars, headings, sections, and prose.</summary>
    private sealed class Document
    {
        private readonly Dictionary<string, string> _frontMatter = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _sections = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The document's H1, when it has one.</summary>
        public string? Heading { get; private set; }

        /// <summary>The first prose paragraph after any heading, used as the goal when front matter omits it.</summary>
        public string? FirstParagraph { get; private set; }

        /// <summary>Returns a front-matter value, or a default.</summary>
        /// <param name="key">The key.</param>
        /// <param name="fallback">Value when the key is absent.</param>
        /// <returns>The value.</returns>
        public string? Scalar(string key, string? fallback) =>
            _frontMatter.TryGetValue(key, out string? value) ? value : fallback;

        /// <summary>
        /// Returns the parsed JSON of the first matching section that has a fenced json block.
        /// </summary>
        /// <param name="names">Candidate section names.</param>
        /// <returns>The parsed value, or null when no matching section holds json.</returns>
        public JsonNode? SectionJson(string[] names)
        {
            foreach (string name in names)
            {
                if (_sections.TryGetValue(name, out string? body)
                    && FencedBlock(body, "json") is { } json)
                {
                    try
                    {
                        return JsonNode.Parse(json) ?? throw new WorldLoadException($"the '{name}' section holds an empty json block.");
                    }
                    catch (JsonException exception)
                    {
                        throw new WorldLoadException($"the '{name}' section's json block is not valid JSON: {exception.Message}", exception);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Returns the trimmed text of the first matching section that has a fenced text block.
        /// </summary>
        /// <param name="names">Candidate section names.</param>
        /// <returns>The text, or null.</returns>
        public string? SectionText(string[] names)
        {
            foreach (string name in names)
            {
                if (_sections.TryGetValue(name, out string? body))
                {
                    string? text = FencedBlock(body, "text") ?? FencedBlock(body, "markdown");

                    if (text is not null)
                    {
                        return text.Trim();
                    }

                    // A plain section is also allowed, so a short template does not need a fence.
                    string trimmed = Collapse(body);

                    if (trimmed.Length > 0 && !trimmed.Contains("```", StringComparison.Ordinal))
                    {
                        return trimmed;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Returns the bullet items of the first matching section.
        /// </summary>
        /// <param name="names">Candidate section names.</param>
        /// <returns>The items, possibly empty.</returns>
        public JsonArray SectionBullets(string[] names)
        {
            JsonArray items = [];

            foreach (string name in names)
            {
                if (!_sections.TryGetValue(name, out string? body))
                {
                    continue;
                }

                foreach (string line in body.Split('\n'))
                {
                    string trimmed = line.TrimStart();

                    if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
                    {
                        string item = trimmed[2..].Trim();

                        // Quotes are a natural way to write an example, but the world does not need
                        // them, and keeping them would put stray quotation marks in the banner.
                        item = item.Trim('"');

                        if (item.Length > 0)
                        {
                            items.Add(item);
                        }
                    }
                }

                break;
            }

            return items;
        }

        private void AddSection(StringBuilder body)
        {
            string current = _current!;

            if (body.Length > 0)
            {
                _sections[current] = body.ToString();
            }
        }

        private string? _current;

        /// <summary>Parses the document into front matter, sections and prose.</summary>
        /// <param name="markdown">The document.</param>
        /// <returns>The parsed shape.</returns>
        public static Document ParseDocument(string markdown)
        {
            Document document = new();
            StringBuilder body = new();

            bool inFrontMatter = false;
            bool frontMatterComplete = false;
            bool inFence = false;

            string[] lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];
                string trimmed = line.Trim();

                // Front matter: a leading --- block of key: value pairs.
                if (index == 0 && trimmed == "---")
                {
                    inFrontMatter = true;
                    continue;
                }

                if (inFrontMatter)
                {
                    if (trimmed == "---")
                    {
                        inFrontMatter = false;
                        frontMatterComplete = true;
                        continue;
                    }

                    int colon = line.IndexOf(':', StringComparison.Ordinal);

                    if (colon > 0)
                    {
                        string key = line[..colon].Trim();
                        string value = line[(colon + 1)..].Trim().Trim('"');

                        if (key.Length > 0)
                        {
                            document._frontMatter[key] = value;
                        }
                    }

                    continue;
                }

                // A fenced block's content is never interpreted, so ``` inside a template survives.
                if (trimmed.StartsWith("```", StringComparison.Ordinal))
                {
                    inFence = !inFence;
                    body.Append(line).Append('\n');
                    continue;
                }

                if (inFence)
                {
                    body.Append(line).Append('\n');
                    continue;
                }

                if (trimmed.StartsWith("## ", StringComparison.Ordinal))
                {
                    if (document._current is not null)
                    {
                        document.AddSection(body);
                    }

                    document._current = trimmed[3..].Trim().ToLowerInvariant();
                    body.Clear();
                    continue;
                }

                if (trimmed.StartsWith("# ", StringComparison.Ordinal))
                {
                    document.Heading ??= trimmed[2..].Trim();
                    continue;
                }

                if (document._current is null)
                {
                    // Prose before any H2. The first real paragraph is the goal when front matter has none.
                    if (document.FirstParagraph is null
                        && !frontMatterComplete
                        && trimmed.Length > 0
                        && !trimmed.StartsWith('#'))
                    {
                        document.FirstParagraph = trimmed;
                    }
                    else if (document.FirstParagraph is null && trimmed.Length > 0)
                    {
                        document.FirstParagraph = trimmed;
                    }

                    continue;
                }

                body.Append(line).Append('\n');
            }

            if (document._current is not null)
            {
                document.AddSection(body);
            }

            return document;
        }

        private static string? FencedBlock(string body, string language)
        {
            string open = "```" + language;

            int start = body.IndexOf(open, StringComparison.OrdinalIgnoreCase);

            if (start < 0)
            {
                return null;
            }

            start += open.Length;

            // Skip the rest of the opening fence line, which may carry a title.
            int newline = body.IndexOf('\n', start);

            if (newline < 0)
            {
                return null;
            }

            int end = body.IndexOf("```", newline, StringComparison.Ordinal);

            if (end < 0)
            {
                return null;
            }

            return body[(newline + 1)..end];
        }

        private static string Collapse(string body)
        {
            StringBuilder builder = new();

            foreach (string line in body.Split('\n'))
            {
                string trimmed = line.Trim();

                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(trimmed);
            }

            return builder.ToString();
        }
    }
}
