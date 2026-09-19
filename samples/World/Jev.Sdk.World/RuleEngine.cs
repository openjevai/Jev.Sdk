// RuleEngine.cs
// Part of Jev.Sdk.World. Evaluating a world's declared rules against a turn's judgement.
//
// The engine is ignorant of the world it is running. It knows how to compare a number, match a word,
// roll dice, and apply effects - and nothing about keys, guards, or doors. That ignorance is what makes
// a new world a JSON file rather than a code change.
//
// Dice are injected. A rule that rolls is therefore reproducible in a test rather than a coin toss, and
// the same mechanism gives a world real uncertainty without giving it real unpredictability.

namespace Jev.Sdk.World;

/// <summary>What one turn did, for the console to print.</summary>
/// <param name="Narration">Lines to print, in order.</param>
/// <param name="RuleId">The id of the rule that fired.</param>
/// <param name="Changed">Whether any state changed.</param>
public sealed record TurnResult(IReadOnlyList<string> Narration, string RuleId, bool Changed);

/// <summary>
/// Applies a world's rules to a turn.
/// </summary>
public sealed class RuleEngine
{
    private readonly WorldDefinition _world;
    private readonly Random _dice;

    /// <summary>Creates an engine for a world.</summary>
    /// <param name="world">The validated world.</param>
    /// <param name="dice">Source of randomness. Injected so a test can pin a roll.</param>
    public RuleEngine(WorldDefinition world, Random? dice = null)
    {
        ArgumentNullException.ThrowIfNull(world);

        _world = world;
        _dice = dice ?? Random.Shared;
    }

    /// <summary>
    /// Applies the first rule whose condition holds.
    /// </summary>
    /// <param name="state">The current state. Mutated in place.</param>
    /// <param name="judgements">The turn's judgements.</param>
    /// <returns>What happened.</returns>
    /// <remarks>
    /// First-match-wins. A world's rules are written in the order its author intends them to be
    /// considered, and reading a turn's outcome should be possible by reading top to bottom.
    ///
    /// The turn counter is incremented before the rules run, so a rule can see it, and a rule that ends
    /// the run can do so knowing which turn it ended on.
    /// </remarks>
    public TurnResult Apply(WorldState state, JudgementSet judgements)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(judgements);

        state.CountTurn();

        RuleDefinition? matched = _world.Rules.FirstOrDefault(rule => Holds(rule.When, state, judgements));

        if (matched is null)
        {
            // A world with no catch-all rule can reach here. Saying so plainly is better than
            // inventing an outcome, and the validator's rules make it easy to add a final catch-all.
            return new TurnResult(
                [$"Nothing in '{_world.Title}' describes what happens next."],
                "(no rule matched)",
                Changed: false);
        }

        List<string> narration = [];
        bool changed = false;

        foreach (string line in matched.Narrate)
        {
            narration.Add(JudgementSet.Render(line, action: string.Empty, stateDescription: state.Describe()));
        }

        foreach (EffectDefinition effect in matched.Then)
        {
            changed |= ApplyEffect(effect, state, narration);
        }

        return new TurnResult(narration, matched.Id, changed);
    }

    /// <summary>
    /// Evaluates a condition.
    /// </summary>
    /// <param name="condition">The condition, or null for "always".</param>
    /// <param name="state">The current state.</param>
    /// <param name="judgements">The turn's judgements.</param>
    /// <returns>Whether it holds.</returns>
    public bool Holds(ConditionDefinition? condition, WorldState state, JudgementSet judgements)
    {
        // A rule with no condition always applies. This is the documented catch-all.
        if (condition is null)
        {
            return true;
        }

        if (condition.All is { Count: > 0 } all)
        {
            return all.All(child => Holds(child, state, judgements));
        }

        if (condition.Any is { Count: > 0 } any)
        {
            return any.Any(child => Holds(child, state, judgements));
        }

        if (condition.Not is { } not)
        {
            return !Holds(not, state, judgements);
        }

        if (condition.Roll is { } roll)
        {
            return Roll(roll, judgements);
        }

        string actual = condition.Judgement is not null
            ? judgements[condition.Judgement].AsText()
            : state.GetText(condition.State!);

        double number = condition.Judgement is not null
            ? judgements[condition.Judgement].Number ?? 0.0
            : double.TryParse(actual, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed)
                ? parsed
                : state.GetBool(condition.State!) ? 1.0 : 0.0;

        if (condition.AtLeast is { } atLeast)
        {
            return number >= atLeast;
        }

        if (condition.Below is { } below)
        {
            return number < below;
        }

        if (condition.Matches is { } equals)
        {
            return string.Equals(actual, equals, StringComparison.Ordinal);
        }

        if (condition.IsOneOf is { Count: > 0 } oneOf)
        {
            return oneOf.Contains(actual, StringComparer.Ordinal);
        }

        return false;
    }

    private bool Roll(RollDefinition roll, JudgementSet judgements)
    {
        double chance = roll.ChanceFrom is { } key
            ? judgements[key].Number ?? 0.0
            : roll.Chance ?? 0.0;

        if (roll.Scale is { } scale)
        {
            chance *= scale;

            // Scaling can push a chance past certainty, which would make the roll meaningless rather
            // than generous.
            chance = Math.Clamp(chance, 0.0, 1.0);
        }

        // The floor is checked before the dice are touched. Without it a hopeless attempt would still
        // succeed on a lucky roll, which reads as the world ignoring its own judgement.
        if (roll.AtLeast is { } floor && chance < floor)
        {
            return false;
        }

        return _dice.NextDouble() < chance;
    }

    private static bool ApplyEffect(EffectDefinition effect, WorldState state, List<string> narration)
    {
        switch (effect.Kind)
        {
            case EffectKinds.Set:
                state.Set(effect.State!, effect.To!);
                return true;

            case EffectKinds.Increment:
                state.Increment(effect.State!, effect.By!.Value);
                return true;

            case EffectKinds.Narrate:
                narration.Add(JudgementSet.Render(effect.Text!, action: string.Empty, stateDescription: state.Describe()));
                return false;

            case EffectKinds.Win:
                state.MarkWon();
                return true;

            case EffectKinds.Lose:
                state.MarkLost();
                return true;

            default:
                // The validator rejects unknown kinds at load time, so this is unreachable for a world
                // that loaded. Throwing rather than ignoring keeps a future effect kind from being
                // silently dropped if one is added to the schema and not here.
                throw new WorldLoadException($"Unknown effect kind '{effect.Kind}'.");
        }
    }
}
