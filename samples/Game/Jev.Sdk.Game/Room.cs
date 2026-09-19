// Room.cs
// Part of Jev.Sdk.Game. The rules: given a judgement and the current state, what happens next.
//
// This is the deterministic half the design calls for. No model call happens in here, every outcome
// is a pure function of (state, judgement, dice), and the randomness is injected so a test can pin it.
// That separation is the demo's actual point: the model supplies judgement, the game supplies rules,
// and neither is asked to do the other's job.

namespace Jev.Sdk.Game;

/// <summary>What happened as a result of a turn, for the console to narrate.</summary>
/// <param name="Narration">Lines describing the outcome, in order.</param>
/// <param name="StateChanged">Whether anything about the room changed.</param>
public sealed record TurnOutcome(IReadOnlyList<string> Narration, bool StateChanged);

/// <summary>
/// Applies game rules to a judgement.
/// </summary>
public sealed class Room
{
    /// <summary>Plausibility at or above this means the action can be carried out.</summary>
    /// <remarks>
    /// <para>
    /// Thresholds are constants rather than configuration because the demo is about showing what a
    /// probability is for, and a value a reader can point at is clearer than a knob they have to find.
    /// </para>
    /// <para>
    /// 0.5 is not arbitrary. Measured against the live model with the wording in
    /// <see cref="QuestionSet"/>, actions the player can actually perform land between 0.52 and 0.95
    /// while impossible ones land between 0.02 and 0.46 — reaching for the key scores 0.77-0.95, using
    /// a key you are not holding scores 0.29-0.41, and flying through the ceiling scores 0.02. The
    /// threshold sits in the gap.
    /// </para>
    /// <para>
    /// The first version of the plausibility question asked whether the action was possible "from the
    /// position described in the state". That wording read as a precondition check rather than a
    /// physical one, and scored walking across the room to pick up the key at 0.55 and "grab the key"
    /// at 0.55 — so almost nothing the player typed could ever work, and the game was unplayable while
    /// every rule test still passed. The wording was the bug, not the threshold.
    /// </para>
    /// </remarks>
    public const double PlausibilityThreshold = 0.5;

    /// <remarks>
    /// The noise question's wording carries most of this rule, which is worth recording. The first
    /// version asked "How loud is the action described in the player's own words?" and returned
    /// roughly 0.17 for everything — including smashing a window — so the label came back "silent" for
    /// every action and this veto never fired. Anchoring the scale with examples in the instruction
    /// fixed it: 2.9 for breaking glass, 0.7-0.9 for quiet movement.
    /// </remarks>
    /// <summary>Risk of waking the guard at or above this means the guard wakes.</summary>
    public const double WakeThreshold = 0.5;

    /// <summary>Chance of slipping past an awake guard must be at least this to succeed.</summary>
    /// <remarks>
    /// Two independent judgements gate this: the action has to be physically possible at all, and the
    /// attempt has to be likely to work. The roll then has to fall below the lower of the two. That is
    /// the design's "another Noul like 'Would this attempt succeed?' compared against a random roll".
    /// </remarks>
    public const double SneakThreshold = 0.6;

    /// <summary>Noise at or above this level wakes the guard, whatever the model said about waking.</summary>
    public static readonly string[] LoudNoises = [NoiseLevels.ExtremelyLoud];

    private readonly Random _dice;

    /// <summary>Creates a room with an injected source of randomness.</summary>
    /// <param name="dice">
    /// The random source used for rolls. Injected so a test can pin success or failure rather than
    /// hope for it.
    /// </param>
    public Room(Random? dice = null) => _dice = dice ?? Random.Shared;

    /// <summary>
    /// Applies one turn.
    /// </summary>
    /// <param name="state">The current state. Mutated in place.</param>
    /// <param name="judgement">The model's judgement for this turn.</param>
    /// <returns>What happened, for narration.</returns>
    /// <remarks>
    /// The order of operations matters and is deliberate: plausibility is checked before anything else,
    /// because an implausible action cannot change the room however confident the model was about the
    /// intent. Noise can wake the guard even when the model judged waking unlikely, because a smashed
    /// window is loud in a way no probability should be able to talk the game out of.
    /// </remarks>
    public TurnOutcome Apply(RoomState state, TurnJudgement judgement)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(judgement);

        List<string> narration = [];
        bool changed = false;

        state.Turns++;

        if (judgement.Plausible < PlausibilityThreshold)
        {
            narration.Add("That does not work here.");

            // Even a failed, implausible attempt can be noisy, which is the one piece of realism that
            // makes the noise judgement matter on its own rather than only as a modifier.
            if (IsLoud(judgement))
            {
                narration.AddRange(Noise(state));
                changed = true;
            }

            return new TurnOutcome(narration, changed);
        }

        switch (judgement.Intent)
        {
            case ActionIntent.TakeKey:
                changed |= TakeKey(state, narration);
                break;

            case ActionIntent.UnlockDoor:
                changed |= UnlockDoor(state, narration);
                break;

            case ActionIntent.BreakWindow:
                changed |= BreakWindow(state, narration);
                break;

            case ActionIntent.SneakPastGuard:
                changed |= SneakPastGuard(state, judgement, narration);
                break;

            case ActionIntent.AttackGuard:
                changed |= AttackGuard(state, narration);
                break;

            case ActionIntent.TalkToGuard:
                changed |= TalkToGuard(state, narration);
                break;

            default:
                // "other" is not a failure. The design calls it out as the case where the progress
                // judgement earns its place: a creative command that maps to nothing still does
                // something if the model judged it a step toward escape.
                changed |= CreativeAttempt(state, judgement, narration);
                break;
        }

        if (IsLoud(judgement))
        {
            narration.AddRange(Noise(state));
            changed = true;
        }

        return new TurnOutcome(narration, changed);
    }

    /// <summary>
    /// Whether the action is loud enough to wake the guard on noise alone.
    /// </summary>
    /// <param name="judgement">The judgement to inspect.</param>
    /// <returns>True when the noise level is one of <see cref="LoudNoises"/>.</returns>
    public static bool IsLoud(TurnJudgement judgement)
    {
        ArgumentNullException.ThrowIfNull(judgement);

        return LoudNoises.Contains(judgement.Noise, StringComparer.Ordinal);
    }

    private static bool TakeKey(RoomState state, List<string> narration)
    {
        if (state.HasKey)
        {
            narration.Add("You already have the key.");
            return false;
        }

        state.HasKey = true;
        narration.Add("You take the key.");

        return true;
    }

    private static bool UnlockDoor(RoomState state, List<string> narration)
    {
        if (!state.HasKey)
        {
            narration.Add("The door is locked and you do not have the key.");
            return false;
        }

        if (!state.DoorUnlocked)
        {
            state.DoorUnlocked = true;
            narration.Add("The key turns. The door is unlocked.");
            return true;
        }

        // Unlocking an already-unlocked door is the opening attempt, not a second unlock.
        return TryLeave(state, narration);
    }

    private static bool BreakWindow(RoomState state, List<string> narration)
    {
        if (state.WindowBroken)
        {
            narration.Add("The window is already broken.");
            return false;
        }

        state.WindowBroken = true;
        narration.Add("CRASH!");
        narration.Add("The window breaks.");

        return true;
    }

    private bool SneakPastGuard(RoomState state, TurnJudgement judgement, List<string> narration)
    {
        if (!state.GuardAwake)
        {
            narration.Add("You slip past the sleeping guard.");

            return TryLeave(state, narration);
        }

        // An awake guard makes this a contest, which is where the probabilities become visible: two
        // independent judgements against one roll.
        double successChance = Math.Min(judgement.Plausible, judgement.Progress);
        double roll = _dice.NextDouble();

        if (successChance >= SneakThreshold && roll < successChance)
        {
            narration.Add("You slip past the guard.");
            return TryLeave(state, narration);
        }

        narration.Add("The guard sees you move.");
        return false;
    }

    private static bool AttackGuard(RoomState state, List<string> narration)
    {
        // The guard cannot lose: this is a demo about judgement, not combat, and the design keeps one
        // guard who stays a threat so sneaking and talking remain meaningful choices.
        state.GuardAwake = true;

        narration.Add("You lunge at the guard. It does not go well.");
        narration.Add("The guard is awake, and now they are watching you closely.");

        return true;
    }

    private static bool TalkToGuard(RoomState state, List<string> narration)
    {
        if (!state.GuardAwake)
        {
            state.GuardAwake = true;

            narration.Add("You speak. The guard stirs and opens their eyes.");
            return true;
        }

        narration.Add("The guard hears you out, unmoved.");
        return false;
    }

    private bool CreativeAttempt(RoomState state, TurnJudgement judgement, List<string> narration)
    {
        // The design's point for the progress question: text that maps to no known action can still do
        // something, decided by one probability compared against a roll.
        if (judgement.Progress >= PlausibilityThreshold && _dice.NextDouble() < judgement.Progress)
        {
            narration.Add("It works, more or less.");

            if (!state.GuardAwake)
            {
                return false;
            }

            narration.Add("The guard is distracted.");
            state.GuardAwake = false;

            return true;
        }

        narration.Add("Nothing comes of it.");
        return false;
    }

    private static bool TryLeave(RoomState state, List<string> narration)
    {
        if (!state.DoorUnlocked)
        {
            // Slipping past the guard only helps once the door will open.
            narration.Add("The door is still locked, so there is nowhere to go.");
            return false;
        }

        state.Escaped = true;

        narration.Add("You open the door and step out into the corridor.");
        narration.Add("You are out.");

        return true;
    }

    private static IEnumerable<string> Noise(RoomState state)
    {
        if (state.GuardAwake)
        {
            yield return "The noise carries.";
            yield break;
        }

        state.GuardAwake = true;

        yield return "The noise carries.";
        yield return "The guard wakes up.";
    }
}
