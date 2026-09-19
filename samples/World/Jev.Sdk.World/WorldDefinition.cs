// WorldDefinition.cs
// Part of Jev.Sdk.World. The schema a world is declared in: its state, the judgements to make about
// it, and the rules that decide what happens.
//
// A world is DATA, not code. Everything the engine does on a turn is derived from one of these
// documents, which is the whole point: adding a room, a creature, or a whole new setting means
// writing JSON, not C#. The engine has no knowledge of keys, guards, windows, or doors.
//
// The format is documented for humans in ../../README.md and for machines in
// ../../schema/world.schema.json. This file is the C# reading of that format, so the three must agree;
// the tests check the schema file against a real world document.

using System.Text.Json.Serialization;

namespace Jev.Sdk.World;

/// <summary>
/// A complete world: what it is, what state it has, what to ask about it, and what the answers mean.
/// </summary>
public sealed class WorldDefinition
{
    /// <summary>Format version, so a future change can be detected rather than misread.</summary>
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = string.Empty;

    /// <summary>Stable identifier, used in file names and error messages.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>One line telling the player what they are trying to do.</summary>
    [JsonPropertyName("goal")]
    public string Goal { get; set; } = string.Empty;

    /// <summary>Examples shown at the start, so the player has somewhere to begin.</summary>
    [JsonPropertyName("examples")]
    public IList<string> Examples { get; set; } = [];

    /// <summary>The state fields that make up the world.</summary>
    [JsonPropertyName("state")]
    public IList<StateField> State { get; set; } = [];

    /// <summary>
    /// How the state is described to the model, as a template.
    /// </summary>
    /// <remarks>
    /// Placeholders named after state fields are replaced with that field's description for its
    /// current value. Written as prose because the model is being asked to judge a situation, and a
    /// dump of booleans reads as a database row rather than a place.
    /// </remarks>
    [JsonPropertyName("stateTemplate")]
    public string StateTemplate { get; set; } = string.Empty;

    /// <summary>The judgement questions asked every turn, in one call.</summary>
    [JsonPropertyName("judgements")]
    public IList<JudgementDefinition> Judgements { get; set; } = [];

    /// <summary>
    /// The rules, evaluated in order. The first whose condition holds is applied.
    /// </summary>
    /// <remarks>
    /// First-match-wins rather than accumulate-everything, because a world's rules usually describe
    /// mutually exclusive outcomes and a reader should be able to tell what a turn does by reading
    /// top to bottom.
    /// </remarks>
    [JsonPropertyName("rules")]
    public IList<RuleDefinition> Rules { get; set; } = [];

    /// <summary>How a run ends.</summary>
    [JsonPropertyName("ending")]
    public EndingDefinition Ending { get; set; } = new();

    /// <summary>Turn limit, so a run cannot continue forever.</summary>
    [JsonPropertyName("turnLimit")]
    public int TurnLimit { get; set; } = 25;
}

/// <summary>
/// One piece of world state: a flag, a counter, or a value the rules read and write.
/// </summary>
public sealed class StateField
{
    /// <summary>Key used in templates, conditions, and effects.</summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>The kind of value held. One of <see cref="StateValueKinds"/>.</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = StateValueKinds.Bool;

    /// <summary>Value before anything happens.</summary>
    [JsonPropertyName("initial")]
    public System.Text.Json.JsonElement Initial { get; set; }

    /// <summary>
    /// How this field reads in the state description, keyed by its value.
    /// </summary>
    /// <remarks>
    /// A bool needs both values described. An int or string field uses "*" for a fallback sentence.
    /// This is what stops the description degrading into "has_key: false" — the model is given prose.
    /// </remarks>
    [JsonPropertyName("describe")]
    public IDictionary<string, string> Describe { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>The value kinds a state field may hold.</summary>
public static class StateValueKinds
{
    /// <summary>True or false.</summary>
    public const string Bool = "bool";

    /// <summary>A whole number, usually a counter.</summary>
    public const string Counter = "int";

    /// <summary>A string, for a small closed set of situations.</summary>
    public const string Text = "text";
}

/// <summary>
/// One question asked about the turn.
/// </summary>
public sealed class JudgementDefinition
{
    /// <summary>Key the answer is read back under.</summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>The kind of question. One of <see cref="JudgementKinds"/>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// The instruction sent to the model.
    /// </summary>
    /// <remarks>
    /// Supports two placeholders: {{action}} for the player's own words, and {{state}} for the state
    /// description. Wording is behaviour here, not decoration — see ../../README.md for two measured
    /// examples where the same rule and different wording produced an unplayable world.
    /// </remarks>
    [JsonPropertyName("question")]
    public string Question { get; set; } = string.Empty;

    /// <summary>Options, for a choice question.</summary>
    [JsonPropertyName("options")]
    public IList<OptionDefinition> Options { get; set; } = [];

    /// <summary>Ordered levels, for a score question.</summary>
    [JsonPropertyName("levels")]
    public IList<string> Levels { get; set; } = [];

    /// <summary>
    /// What to use when the model returns nothing usable for this judgement.
    /// </summary>
    /// <remarks>
    /// A world must say, because the engine cannot guess: for a probability the honest fallback is a
    /// value that decides nothing, and for a choice it is whichever option means "none of the above".
    /// </remarks>
    [JsonPropertyName("fallback")]
    public string? Fallback { get; set; }
}

/// <summary>The judgement kinds, matching the API's three question types.</summary>
public static class JudgementKinds
{
    /// <summary>A probability between 0 and 1. The API's noul.</summary>
    public const string Noul = "noul";

    /// <summary>One of a named set. The API's choice.</summary>
    public const string Choice = "choice";

    /// <summary>A position on an ordered scale. The API's score.</summary>
    public const string Score = "score";
}

/// <summary>One option of a choice question.</summary>
public sealed class OptionDefinition
{
    /// <summary>The name, which is what the rules compare against.</summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// What the option means, shown to the model.
    /// </summary>
    /// <remarks>
    /// Carries the vocabulary a player is likely to use. A bare name classifies far worse than a
    /// described one, which is the difference between a parser and a demo that appears to understand.
    /// </remarks>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// One rule: when its condition holds, its effects are applied and the rest of the rules are skipped.
/// </summary>
public sealed class RuleDefinition
{
    /// <summary>Identifier, used in narration-of-last-resort and in error messages.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Whether the condition holds. Null means "always", used for a catch-all final rule.</summary>
    [JsonPropertyName("when")]
    public ConditionDefinition? When { get; set; }

    /// <summary>What to do, in order.</summary>
    [JsonPropertyName("then")]
    public IList<EffectDefinition> Then { get; set; } = [];

    /// <summary>Lines narrated when this rule fires.</summary>
    [JsonPropertyName("narrate")]
    public IList<string> Narrate { get; set; } = [];
}

/// <summary>
/// A condition over the state, the judgement, or the dice.
/// </summary>
/// <remarks>
/// Deliberately tiny: a condition is exactly one comparison, or a combinator over others. There is no
/// arithmetic and no nesting beyond all/any/not, because a schema that can express anything is a
/// programming language, and a world author should be writing data.
/// </remarks>
public sealed class ConditionDefinition
{
    /// <summary>Narrows to a judgement by key, for a comparison.</summary>
    [JsonPropertyName("judgement")]
    public string? Judgement { get; set; }

    /// <summary>Narrows to a state field by key, for a comparison.</summary>
    [JsonPropertyName("state")]
    public string? State { get; set; }

    /// <summary>Holds when the value is at or above this. For probabilities and counters.</summary>
    [JsonPropertyName("atLeast")]
    public double? AtLeast { get; set; }

    /// <summary>Holds when the value is below this. For probabilities and counters.</summary>
    [JsonPropertyName("below")]
    public double? Below { get; set; }

    /// <summary>
    /// Holds when the value equals this. For choices, text, bools and counters.
    /// </summary>
    /// <remarks>
    /// Named Matches rather than Equals because Equals would shadow object.Equals, and a schema
    /// property that changes how the CLR compares two conditions is a trap for whoever reads it next.
    /// </remarks>
    [JsonPropertyName("equals")]
    public string? Matches { get; set; }

    /// <summary>Holds when the value matches one of these. For choices and score labels.</summary>
    [JsonPropertyName("isOneOf")]
    public IList<string>? IsOneOf { get; set; }

    /// <summary>Holds when a probability rolls true against the value.</summary>
    [JsonPropertyName("roll")]
    public RollDefinition? Roll { get; set; }

    /// <summary>Holds when every sub-condition holds.</summary>
    [JsonPropertyName("all")]
    public IList<ConditionDefinition>? All { get; set; }

    /// <summary>Holds when at least one sub-condition holds.</summary>
    [JsonPropertyName("any")]
    public IList<ConditionDefinition>? Any { get; set; }

    /// <summary>Holds when the sub-condition does not.</summary>
    [JsonPropertyName("not")]
    public ConditionDefinition? Not { get; set; }
}

/// <summary>
/// A probability compared against a roll of the dice.
/// </summary>
/// <remarks>
/// This is how a world uses a probability as a probability rather than as a threshold: the judgement
/// is the chance, and the engine decides. Used for contests and for creative actions that no rule can
/// resolve.
/// </remarks>
public sealed class RollDefinition
{
    /// <summary>The judgement supplying the chance, or null to use <see cref="Chance"/>.</summary>
    [JsonPropertyName("chanceFrom")]
    public string? ChanceFrom { get; set; }

    /// <summary>A fixed chance, when no judgement supplies one.</summary>
    [JsonPropertyName("chance")]
    public double? Chance { get; set; }

    /// <summary>
    /// A floor the chance must clear before a roll is even made.
    /// </summary>
    /// <remarks>
    /// Keeps a hopeless attempt hopeless: without it, an action the model judged 2% likely would still
    /// succeed on a lucky roll, which reads as the world ignoring its own judgement.
    /// </remarks>
    [JsonPropertyName("atLeast")]
    public double? AtLeast { get; set; }

    /// <summary>Scales the chance, so a world can make a roll harder or easier.</summary>
    [JsonPropertyName("scale")]
    public double? Scale { get; set; }
}

/// <summary>One change a rule makes.</summary>
public sealed class EffectDefinition
{
    /// <summary>What kind of effect. One of <see cref="EffectKinds"/>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    /// <summary>The state field to change, for a set effect.</summary>
    [JsonPropertyName("state")]
    public string? State { get; set; }

    /// <summary>The value to set. Interpreted against the field's declared type.</summary>
    [JsonPropertyName("to")]
    public string? To { get; set; }

    /// <summary>How much to add, for an increment effect.</summary>
    [JsonPropertyName("by")]
    public int? By { get; set; }

    /// <summary>The line to print, for a narrate effect.</summary>
    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

/// <summary>The effects a rule may apply.</summary>
public static class EffectKinds
{
    /// <summary>Set a state field to a value.</summary>
    public const string Set = "set";

    /// <summary>Add to an integer state field.</summary>
    public const string Increment = "increment";

    /// <summary>Print a line.</summary>
    public const string Narrate = "narrate";

    /// <summary>End the run successfully.</summary>
    public const string Win = "win";

    /// <summary>End the run unsuccessfully.</summary>
    public const string Lose = "lose";
}

/// <summary>How a run ends.</summary>
public sealed class EndingDefinition
{
    /// <summary>Printed when the run was won.</summary>
    [JsonPropertyName("won")]
    public string Won { get; set; } = "You are out.";

    /// <summary>Printed when the run was lost, or the turn limit was reached.</summary>
    [JsonPropertyName("lost")]
    public string Lost { get; set; } = "You did not get out.";
}
