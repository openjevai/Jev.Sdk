// WorldLoader.cs
// Part of Jev.Sdk.World. Reads a world document and refuses to accept one that could not be played.
//
// Validation is not a formality here. A world is data written by hand, and the three ways it can be
// silently wrong are precisely the ones that produce a game which runs and behaves stupidly: a rule
// comparing a state field that does not exist, a fallback judgement value that matches no option, or
// a question wording that drops the player's own words. All three are caught at load time, with the
// offending key named, rather than surfacing as a confusing turn later.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jev.Sdk.World;

/// <summary>
/// Raised when a world document cannot be loaded or is internally inconsistent.
/// </summary>
public sealed class WorldLoadException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What is wrong, naming the offending key.</param>
    public WorldLoadException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What is wrong.</param>
    /// <param name="inner">The underlying failure.</param>
    public WorldLoadException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>
/// Loads world documents.
/// </summary>
public static class WorldLoader
{
    /// <summary>The format version this engine understands.</summary>
    public const string SupportedSchemaVersion = "1.0";

    private static readonly JsonSerializerOptions s_options = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Loads a world from JSON text and validates it.
    /// </summary>
    /// <param name="json">The world document.</param>
    /// <returns>A validated world, ready to play.</returns>
    /// <exception cref="WorldLoadException">The document is malformed or inconsistent.</exception>
    /// <remarks>
    /// Comments and trailing commas are permitted, matching the settings files this repository ships:
    /// a world document is written by a person and benefits from being able to explain itself.
    /// </remarks>
    public static WorldDefinition Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        WorldDefinition world;

        try
        {
            world = JsonSerializer.Deserialize<WorldDefinition>(json, s_options)
                ?? throw new WorldLoadException("The world document was empty.");
        }
        catch (JsonException exception)
        {
            throw new WorldLoadException($"The world document is not valid JSON: {exception.Message}", exception);
        }

        Validate(world);

        return world;
    }

    /// <summary>
    /// Loads a world from a file.
    /// </summary>
    /// <param name="path">Path to the world document.</param>
    /// <returns>A validated world.</returns>
    /// <exception cref="WorldLoadException">The file cannot be read, or is malformed or inconsistent.</exception>
    public static WorldDefinition Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string json;

        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new WorldLoadException($"Could not read the world file '{path}': {exception.Message}", exception);
        }

        try
        {
            return Parse(json);
        }
        catch (WorldLoadException exception)
        {
            // Naming the file matters: a directory of worlds makes "which one is broken" the first
            // question, and the answer should not require a second run.
            throw new WorldLoadException($"{path}: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// Checks a loaded world for the mistakes that would make it quietly unplayable.
    /// </summary>
    /// <param name="world">The world to check.</param>
    /// <exception cref="WorldLoadException">Something is missing or inconsistent.</exception>
    /// <remarks>
    /// Every message names the offending key. "Invalid world" is useless to an author; "rule
    /// 'take_key' sets state 'has_keys', which no state field declares" is a fix.
    /// </remarks>
    public static void Validate(WorldDefinition world)
    {
        ArgumentNullException.ThrowIfNull(world);

        Require(world.SchemaVersion == SupportedSchemaVersion,
            $"Unsupported schemaVersion '{world.SchemaVersion}'. This engine understands '{SupportedSchemaVersion}'.");

        Require(!string.IsNullOrWhiteSpace(world.Id), "The world needs an 'id'.");
        Require(!string.IsNullOrWhiteSpace(world.Title), "The world needs a 'title'.");
        Require(world.State.Count > 0, "The world declares no state, so there is nothing to judge or change.");
        Require(world.Judgements.Count > 0, "The world declares no judgements, so a turn would ask nothing.");
        Require(world.Rules.Count > 0, "The world declares no rules, so nothing could ever happen.");
        Require(world.TurnLimit > 0, "turnLimit must be greater than zero.");
        Require(!string.IsNullOrWhiteSpace(world.StateTemplate),
            "The world needs a stateTemplate, which is how the model is told the situation.");

        HashSet<string> stateKeys = ValidateState(world);

        ValidateJudgements(world, stateKeys);

        ValidateRules(world, stateKeys);
    }

    private static HashSet<string> ValidateState(WorldDefinition world)
    {
        HashSet<string> keys = new(StringComparer.Ordinal);

        foreach (StateField field in world.State)
        {
            Require(!string.IsNullOrWhiteSpace(field.Key), "A state field has no 'key'.");
            Require(keys.Add(field.Key), $"State field '{field.Key}' is declared more than once.");

            Require(
                field.Type is StateValueKinds.Bool or StateValueKinds.Counter or StateValueKinds.Text,
                $"State field '{field.Key}' has type '{field.Type}'. Expected one of bool, int, text.");

            // A bool must describe both of its values: without them the state description cannot say
            // what the situation is, which is the one thing the model needs.
            if (field.Type == StateValueKinds.Bool)
            {
                Require(field.Describe.ContainsKey("true") && field.Describe.ContainsKey("false"),
                    $"Bool state field '{field.Key}' must describe both 'true' and 'false'.");
            }
            else
            {
                Require(field.Describe.Count > 0,
                    $"State field '{field.Key}' has no 'describe' entry, so it cannot appear in the state description.");
            }
        }

        // Every placeholder in the template must name a real field, or the model is sent a literal
        // "{{has_keys}}" and judges the situation from nonsense.
        foreach (string placeholder in Placeholders.In(world.StateTemplate))
        {
            Require(keys.Contains(placeholder),
                $"stateTemplate refers to '{{{{{placeholder}}}}}', which no state field declares.");
        }

        return keys;
    }

    private static void ValidateJudgements(WorldDefinition world, HashSet<string> stateKeys)
    {
        HashSet<string> keys = new(StringComparer.Ordinal);

        foreach (JudgementDefinition judgement in world.Judgements)
        {
            Require(!string.IsNullOrWhiteSpace(judgement.Key), "A judgement has no 'key'.");
            Require(keys.Add(judgement.Key), $"Judgement '{judgement.Key}' is declared more than once.");

            Require(
                judgement.Kind is JudgementKinds.Noul or JudgementKinds.Choice or JudgementKinds.Score,
                $"Judgement '{judgement.Key}' has kind '{judgement.Kind}'. Expected one of noul, choice, score.");

            Require(!string.IsNullOrWhiteSpace(judgement.Question),
                $"Judgement '{judgement.Key}' has no 'question'.");

            // A question that does not quote the player is judging nothing.
            Require(judgement.Question.Contains(Placeholders.Action, StringComparison.Ordinal),
                $"Judgement '{judgement.Key}' does not use {Placeholders.Action}, so it is not about what the player did.");

            foreach (string placeholder in Placeholders.In(judgement.Question))
            {
                // Placeholders.In yields the bare name, while the constants include the braces, so the
                // comparison has to be against the bare form. Getting this wrong made every world fail
                // to load with a message naming {{action}} as an unknown field.
                Require(
                    placeholder == Placeholders.ActionName
                        || placeholder == Placeholders.StateName
                        || stateKeys.Contains(placeholder)
                        || keys.Contains(placeholder),
                    $"Judgement '{judgement.Key}' refers to '{{{{{placeholder}}}}}', which is not a state field, an earlier judgement, or one of the built-in placeholders ({Placeholders.Action}, {Placeholders.State}).");
            }

            switch (judgement.Kind)
            {
                case JudgementKinds.Choice:
                    Require(judgement.Options.Count >= 2,
                        $"Choice judgement '{judgement.Key}' offers {judgement.Options.Count} option(s); at least two are needed.");

                    foreach (OptionDefinition option in judgement.Options)
                    {
                        Require(!string.IsNullOrWhiteSpace(option.Key),
                            $"A choice option of '{judgement.Key}' has no 'key'.");
                        Require(!string.IsNullOrWhiteSpace(option.Description),
                            $"Choice option '{judgement.Key}.{option.Key}' has no description, which is what teaches the model the player's vocabulary.");
                    }

                    RequireFallback(judgement, option => option.Key, levels: null);
                    break;

                case JudgementKinds.Score:
                    Require(judgement.Levels.Count >= 2,
                        $"Score judgement '{judgement.Key}' has {judgement.Levels.Count} level(s); at least two are needed.");

                    RequireFallback(judgement, fromOption: null, judgement.Levels);
                    break;

                default:
                    Require(judgement.Fallback is null || double.TryParse(
                            judgement.Fallback,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out _),
                        $"Judgement '{judgement.Key}' has fallback '{judgement.Fallback}', which is not a number.");
                    break;
            }
        }
    }

    private static void RequireFallback(
        JudgementDefinition judgement,
        Func<OptionDefinition, string>? fromOption,
        IList<string>? levels)
    {
        IReadOnlyList<string> allowed = levels?.Count > 0 || fromOption is null
            ? [.. levels ?? []]
            : [.. judgement.Options.Select(fromOption)];

        Require(
            !string.IsNullOrWhiteSpace(judgement.Fallback),
            $"Judgement '{judgement.Key}' has no 'fallback'. The engine cannot guess what to use when the model returns nothing usable.");

        Require(
            allowed.Contains(judgement.Fallback, StringComparer.Ordinal),
            $"Judgement '{judgement.Key}' has fallback '{judgement.Fallback}', which is not one of: {string.Join(", ", allowed)}.");
    }

    private static void ValidateRules(WorldDefinition world, HashSet<string> stateKeys)
    {
        HashSet<string> judgementKeys = new(
            world.Judgements.Select(j => j.Key),
            StringComparer.Ordinal);

        HashSet<string> ids = new(StringComparer.Ordinal);

        foreach (RuleDefinition rule in world.Rules)
        {
            Require(!string.IsNullOrWhiteSpace(rule.Id), "A rule has no 'id'.");
            Require(ids.Add(rule.Id), $"Rule '{rule.Id}' is declared more than once.");
            Require(rule.Then.Count > 0, $"Rule '{rule.Id}' does nothing.");

            if (rule.When is not null)
            {
                ValidateCondition(rule.When, rule.Id, stateKeys, judgementKeys);
            }

            foreach (EffectDefinition effect in rule.Then)
            {
                switch (effect.Kind)
                {
                    case EffectKinds.Set:
                        Require(effect.State is not null && stateKeys.Contains(effect.State),
                            $"Rule '{rule.Id}' sets state '{effect.State}', which no state field declares.");
                        Require(effect.To is not null,
                            $"Rule '{rule.Id}' sets '{effect.State}' without a 'to' value.");
                        break;

                    case EffectKinds.Increment:
                        Require(effect.State is not null && stateKeys.Contains(effect.State),
                            $"Rule '{rule.Id}' increments state '{effect.State}', which no state field declares.");
                        Require(effect.By is not null,
                            $"Rule '{rule.Id}' increments '{effect.State}' without a 'by'.");
                        break;

                    case EffectKinds.Narrate:
                        Require(!string.IsNullOrWhiteSpace(effect.Text),
                            $"Rule '{rule.Id}' narrates nothing.");
                        break;

                    case EffectKinds.Win:
                    case EffectKinds.Lose:
                        break;

                    default:
                        throw new WorldLoadException(
                            $"Rule '{rule.Id}' has effect kind '{effect.Kind}'. Expected one of set, increment, narrate, win, lose.");
                }
            }
        }
    }

    private static void ValidateCondition(
        ConditionDefinition condition,
        string ruleId,
        HashSet<string> stateKeys,
        HashSet<string> judgementKeys)
    {
        int forms = 0;

        if (condition.All is { Count: > 0 })
        {
            forms++;

            foreach (ConditionDefinition child in condition.All)
            {
                ValidateCondition(child, ruleId, stateKeys, judgementKeys);
            }
        }

        if (condition.Any is { Count: > 0 })
        {
            forms++;

            foreach (ConditionDefinition child in condition.Any)
            {
                ValidateCondition(child, ruleId, stateKeys, judgementKeys);
            }
        }

        if (condition.Not is not null)
        {
            forms++;
            ValidateCondition(condition.Not, ruleId, stateKeys, judgementKeys);
        }

        if (condition.Roll is not null)
        {
            forms++;

            Require(
                condition.Roll.ChanceFrom is null || judgementKeys.Contains(condition.Roll.ChanceFrom),
                $"Rule '{ruleId}' rolls on judgement '{condition.Roll.ChanceFrom}', which is not declared.");

            Require(
                condition.Roll.ChanceFrom is not null || condition.Roll.Chance is not null,
                $"Rule '{ruleId}' has a roll with neither 'chanceFrom' nor 'chance'.");

            Require(
                condition.Roll.Chance is null or >= 0 and <= 1,
                $"Rule '{ruleId}' has a fixed chance outside 0..1.");

            Require(
                condition.Roll.Scale is null or > 0,
                $"Rule '{ruleId}' has a roll scale that is not positive.");
        }

        bool isComparison = condition.Judgement is not null
            || condition.State is not null
            || condition.AtLeast is not null
            || condition.Below is not null
            || condition.Matches is not null
            || condition.IsOneOf is not null;

        if (isComparison)
        {
            forms++;

            Require(
                condition.Judgement is not null || condition.State is not null,
                $"Rule '{ruleId}' compares something without saying whether it is 'judgement' or 'state'.");

            if (condition.Judgement is not null)
            {
                Require(judgementKeys.Contains(condition.Judgement),
                    $"Rule '{ruleId}' compares judgement '{condition.Judgement}', which is not declared.");
            }

            if (condition.State is not null)
            {
                Require(stateKeys.Contains(condition.State),
                    $"Rule '{ruleId}' compares state '{condition.State}', which no state field declares.");
            }

            int comparisons = (condition.AtLeast is null ? 0 : 1)
                + (condition.Below is null ? 0 : 1)
                + (condition.Matches is null ? 0 : 1)
                + (condition.IsOneOf is null ? 0 : 1);

            Require(comparisons == 1,
                $"Rule '{ruleId}' has {comparisons} comparison(s) in one condition. A condition is one comparison; use 'all' to combine.");
        }

        Require(forms > 0, $"Rule '{ruleId}' has a condition that tests nothing.");
        Require(forms == 1, $"Rule '{ruleId}' has a condition mixing {forms} forms. Use 'all' to combine them.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new WorldLoadException(message);
        }
    }
}

/// <summary>
/// The placeholder syntax world documents use.
/// </summary>
/// <remarks>
/// Double braces, so a world author can write a single brace for any other reason without escaping it.
/// </remarks>
public static class Placeholders
{
    /// <summary>The player's own words, with braces.</summary>
    public const string Action = "{{action}}";

    /// <summary>The state description, with braces.</summary>
    public const string State = "{{state}}";

    /// <summary>The player's own words, as <see cref="In"/> yields it.</summary>
    public const string ActionName = "action";

    /// <summary>The state description, as <see cref="In"/> yields it.</summary>
    public const string StateName = "state";

    /// <summary>
    /// Returns every placeholder name in a template, without its braces.
    /// </summary>
    /// <param name="template">The template to scan.</param>
    /// <returns>The names found, in order of first appearance.</returns>
    /// <remarks>
    /// Deliberately simple: it matches the same <c>{{name}}</c> shape the renderer substitutes, so the
    /// validator and the renderer cannot disagree about what a placeholder is.
    /// </remarks>
    public static IEnumerable<string> In(string? template)
    {
        if (string.IsNullOrEmpty(template))
        {
            yield break;
        }

        int index = 0;

        while (index < template.Length)
        {
            int open = template.IndexOf("{{", index, StringComparison.Ordinal);

            if (open < 0)
            {
                yield break;
            }

            int close = template.IndexOf("}}", open + 2, StringComparison.Ordinal);

            if (close < 0)
            {
                yield break;
            }

            string name = template[(open + 2)..close].Trim();

            if (name.Length > 0)
            {
                yield return name;
            }

            index = close + 2;
        }
    }

    /// <summary>
    /// Replaces a placeholder with a value.
    /// </summary>
    /// <param name="template">The template.</param>
    /// <param name="name">The placeholder's inner name, without braces.</param>
    /// <param name="value">The replacement.</param>
    /// <returns>The template with every occurrence replaced.</returns>
    public static string Substitute(string template, string name, string value)
    {
        ArgumentNullException.ThrowIfNull(template);

        return template.Replace($"{{{{{name}}}}}", value, StringComparison.Ordinal);
    }
}
