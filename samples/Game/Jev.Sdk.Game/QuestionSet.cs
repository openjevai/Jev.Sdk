// QuestionSet.cs
// Part of Jev.Sdk.Game. The five questions asked each turn, and the vocabulary their answers use.
//
// This is the part of the demo that actually demonstrates the API. One call carries five questions of
// three different kinds, all judged against the same state:
//
//   intent      Choice  what is the player trying to do
//   plausible   Noul    is that physically doable at all
//   wakes_guard Noul    does it disturb the guard
//   noise       Score   how loud is it, on an ordered scale
//   progress    Noul    would it actually work
//
// The last two judgements earn their place by deciding things no rule can. "progress" is the success
// chance for a move that has no certain outcome - slipping past an awake guard, or a creative action
// that maps to none of the named intents - which is the design's own point: probabilities are used to
// resolve what a deterministic rule cannot.
//
// What plausibility is NOT for, measured against the live model: enforcing inventory. "unlock the door
// with the key" scores 0.69-0.78 when the key is on a table and 0.90-0.94 when it is held. The model
// judges the motion possible either way, and moving the threshold to separate those would also start
// rejecting real actions. Missing inventory is handled where the design says it should be - in the
// game's own rules, which check the state directly before acting. Plausibility's job is the genuinely
// impossible: flying through the ceiling scores 0.02.
//
// See ../../Game.md for the design this implements.

using System.Text;
using Jev.Sdk;

namespace Jev.Sdk.Game;

/// <summary>
/// The action vocabulary the <c>Choice</c> question classifies into.
/// </summary>
public static class ActionIntent
{
    /// <summary>Take the key from the table.</summary>
    public const string TakeKey = "take_key";

    /// <summary>Unlock the door.</summary>
    public const string UnlockDoor = "unlock_door";

    /// <summary>Break the window.</summary>
    public const string BreakWindow = "break_window";

    /// <summary>Try to get past the guard unseen.</summary>
    public const string SneakPastGuard = "sneak_past_guard";

    /// <summary>Attack the guard.</summary>
    public const string AttackGuard = "attack_guard";

    /// <summary>Talk to the guard.</summary>
    public const string TalkToGuard = "talk_to_guard";

    /// <summary>Anything else, including text that describes no action at all.</summary>
    public const string Other = "other";

    /// <summary>Every intent the game understands, in the order offered to the model.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        TakeKey,
        UnlockDoor,
        BreakWindow,
        SneakPastGuard,
        AttackGuard,
        TalkToGuard,
        Other,
    ];

    /// <summary>
    /// Descriptions shown to the model alongside each option name.
    /// </summary>
    /// <remarks>
    /// The descriptions carry the vocabulary the player is likely to use — "grab", "pocket", "smash" —
    /// because the demo's whole claim is that the player never has to learn commands. A bare option
    /// name would classify far worse than a described one.
    /// </remarks>
    public static readonly Dictionary<string, string?> Descriptions =
        new(StringComparer.Ordinal)
        {
            [TakeKey] = "Pick up or pocket the key, however described: grab it, take it, swipe it, slip it off the table.",
            [UnlockDoor] = "Unlock the door, including opening it once it is already unlocked.",
            [BreakWindow] = "Break, smash, or force the window.",
            [SneakPastGuard] = "Try to get past the guard or out of the room without being noticed.",
            [AttackGuard] = "Attack, restrain, or otherwise physically overpower the guard.",
            [TalkToGuard] = "Speak to the guard, wake them deliberately, or try to persuade them.",
            [Other] = "Anything else: statements, questions, observations, thinking aloud, or actions that make no sense here. Prefer this whenever the text does not describe something the player is actively trying to do.",
        };
}

/// <summary>
/// The ordered noise scale the <c>Score</c> question rates against.
/// </summary>
public static class NoiseLevels
{
    /// <summary>Makes no sound at all.</summary>
    public const string Silent = "silent";

    /// <summary>Makes a small sound, easily missed.</summary>
    public const string Quiet = "quiet";

    /// <summary>Makes a clear sound that carries.</summary>
    public const string Loud = "loud";

    /// <summary>Makes a sound nobody in the building could miss.</summary>
    public const string ExtremelyLoud = "extremely loud";

    /// <summary>Every level, ascending. Position is the level number, starting at zero.</summary>
    public static readonly IReadOnlyList<string> All = [Silent, Quiet, Loud, ExtremelyLoud];
}

/// <summary>
/// Builds the question map for one turn.
/// </summary>
public static class QuestionSet
{
    /// <summary>Question key for the classified intent.</summary>
    public const string IntentKey = "intent";

    /// <summary>Question key for plausibility.</summary>
    public const string PlausibleKey = "plausible";

    /// <summary>Question key for whether the guard is disturbed.</summary>
    public const string WakesGuardKey = "wakes_guard";

    /// <summary>Question key for the noise rating.</summary>
    public const string NoiseKey = "noise";

    /// <summary>Question key for whether the attempt would actually work.</summary>
    public const string ProgressKey = "progress";

    /// <summary>Every question key this set produces.</summary>
    public static readonly IReadOnlyList<string> Keys =
        [IntentKey, PlausibleKey, WakesGuardKey, NoiseKey, ProgressKey];

    /// <summary>
    /// Builds the five questions for a turn.
    /// </summary>
    /// <param name="playerAction">The player's own words, quoted into the instructions.</param>
    /// <returns>A question map keyed by the constants above.</returns>
    /// <remarks>
    /// Each instruction restates the player's action, because the questions are judged independently
    /// and none of them can see the others. The quoted text is trimmed and length-capped so a pasted
    /// wall of text cannot dominate the request; the game's own state sentence is supplied separately
    /// as the call's <c>state</c>, so it is not repeated five times here.
    /// </remarks>
    public static Dictionary<string, Question> Build(string playerAction)
    {
        ArgumentNullException.ThrowIfNull(playerAction);

        string action = Quote(playerAction);

        return new Dictionary<string, Question>(StringComparer.Ordinal)
        {
            [IntentKey] = Question.Choice(
                $"The player typed: \"{action}\". Which single action are they attempting? "
                + "Choose 'other' unless the text describes an action they are actually trying to "
                + "carry out - simply mentioning an object is not an attempt to do something to it.",
                ActionIntent.Descriptions),

            [PlausibleKey] = Question.Noul(
                $"The player typed: \"{action}\". "
                + "Could a person standing in this room physically carry that out? "
                + "Judge only whether the body could do it, not whether it will succeed and not "
                + "whether it is a good idea. Reaching for a visible object on a table is possible, "
                + "including walking over to it first. Flying through the ceiling is not."),

            [WakesGuardKey] = Question.Noul(
                $"The player typed: \"{action}\". "
                + "Would carrying that out disturb or wake the guard, described in the state? "
                + "Answer with the probability that the guard is disturbed by it."),

            [NoiseKey] = Question.Score(
                $"The player typed: \"{action}\". How much noise would carrying that out make in "
                + "this room? Shouting and breaking glass are extremely loud. Picking something up "
                + "quietly is silent or quiet.",
                [.. NoiseLevels.All]),

            [ProgressKey] = Question.Noul(
                $"The player typed: \"{action}\". "
                + "If they attempt that, how likely is it to actually work - to move them closer to "
                + "getting out of the room rather than being a distraction or a wasted move? "
                + "Answer with the probability that the attempt succeeds at getting them closer to "
                + "escape."),
        };
    }

    /// <summary>
    /// Quotes the player's words for an instruction, trimmed and capped.
    /// </summary>
    /// <param name="playerAction">What the player typed.</param>
    /// <returns>A single-line, length-capped rendering of it.</returns>
    /// <remarks>
    /// Newlines are collapsed because an instruction is a sentence and a multi-line paste would break
    /// that shape. The cap is generous enough for a real sentence and small enough that a pasted
    /// document cannot crowd out the question itself.
    /// </remarks>
    private static string Quote(string playerAction)
    {
        const int MaxLength = 400;

        StringBuilder builder = new(Math.Min(playerAction.Length, MaxLength));

        foreach (char character in playerAction)
        {
            if (builder.Length >= MaxLength)
            {
                builder.Append('…');
                break;
            }

            builder.Append(char.IsControl(character) ? ' ' : character);
        }

        string collapsed = builder.ToString().Trim();

        return collapsed.Length == 0 ? "(nothing)" : collapsed;
    }
}
