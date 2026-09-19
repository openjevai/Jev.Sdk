// GamePromptTests.cs
// Part of Jev.Sdk.IntegrationTests. The demo game's question wording, checked against the live model.
//
// These exist because two bugs were found here that no unit test could have caught. Both were prompt
// wording, both made the game unplayable, and both left every rule test passing:
//
//   1. The plausibility question asked whether an action was possible "from the position described in
//      the state". The model read that as a precondition check rather than a physical one and scored
//      walking across the room to pick up the key at 0.55 and "grab the key" at 0.55, so almost
//      nothing the player typed could ever work.
//   2. The noise question asked "How loud is the action described in the player's own words?" and
//      returned roughly 0.17 for everything, including smashing a window, so the label came back
//      "silent" and the loud-action veto in the rules never fired.
//
// Both were fixed by rewording. Nothing about the rules changed. That is worth a test: the game's
// behaviour depends on prompt text as much as on code, so a future rewording should fail here rather
// than in someone's play session. The thresholds asserted below are the ones Room actually uses.

using Jev.Sdk;
using Jev.Sdk.Game;

namespace Jev.Sdk.IntegrationTests;

/// <summary>
/// Live checks on the game's prompt wording, which the rules depend on.
/// </summary>
public class GamePromptTests
{
    private const string StateWithoutKey = """
        You are in a small locked room. A heavy door is the only way out.
        The key is on a table across the room.
        The door is locked.
        The window is intact.
        The guard is asleep in the corner.
        """;

    private const string StateWithKey = """
        You are in a small locked room. A heavy door is the only way out.
        The key is in your pocket.
        The door is locked.
        The window is intact.
        The guard is asleep in the corner.
        """;

    [LiveTheory]
    [InlineData("I quietly creep over and slip the key off the table.")]
    [InlineData("grab key")]
    [InlineData("pocket the little brass thing")]
    [InlineData("carefully take whatever opens the door")]
    public async Task AReachableAction_IsJudgedPlausible(string action)
    {
        // Reaching for a visible object has to clear the threshold the rules use. This is the
        // assertion that failed at 0.52-0.55 under the original wording.
        double plausible = await AskPlausibleAsync(action, StateWithoutKey);

        Assert.True(
            plausible >= Room.PlausibilityThreshold,
            $"'{action}' scored {plausible:0.00}, below the {Room.PlausibilityThreshold} threshold, so the game would refuse it");
    }

    [LiveTheory]
    [InlineData("fly through the ceiling")]
    [InlineData("walk through the wall")]
    [InlineData("teleport to the other side of the door")]
    public async Task APhysicallyImpossibleAction_IsJudgedImplausible(string action)
    {
        // What plausibility is for. A person cannot fly or walk through a wall, so these must fall
        // below the threshold or the game lets the player ignore the room entirely.
        double plausible = await AskPlausibleAsync(action, StateWithoutKey);

        Assert.True(
            plausible < Room.PlausibilityThreshold,
            $"'{action}' scored {plausible:0.00}, at or above the {Room.PlausibilityThreshold} threshold, so the game would allow it");
    }

    [LiveFact]
    public async Task MissingInventory_IsNotWhatPlausibilityEnforces()
    {
        // Recorded because the first version of this suite asserted the opposite, and was wrong.
        //
        // "unlock the door with the key" scores 0.69-0.78 with the key on a table and 0.90-0.94 with
        // it held, consistently across repeated calls: the model judges the motion possible either
        // way. This question is not where inventory is enforced, and moving the threshold to separate
        // those two would start rejecting real actions instead.
        //
        // The design puts inventory in the game's own rules, which read the state directly before
        // acting - covered by the game's unit tests, not by this suite.
        double without = await AskPlausibleAsync("unlock the door with the key", StateWithoutKey);

        Assert.True(
            without >= Room.PlausibilityThreshold,
            $"plausibility scored {without:0.00} for using a key that is on a table; the game's rules handle inventory, not this judgement");
    }

    [LiveFact]
    public async Task HoldingTheKey_RaisesPlausibilityForUsingIt()
    {
        // The shared state still matters: the same words judged against two states must score
        // differently, which is why the room description is sent at all.
        double without = await AskPlausibleAsync("unlock the door with the key", StateWithoutKey);
        double with = await AskPlausibleAsync("unlock the door with the key", StateWithKey);

        Assert.True(
            with > without,
            $"holding the key did not raise plausibility: {without:0.00} without, {with:0.00} with");
    }

    [LiveTheory]
    [InlineData("I hurl the chair through the window.")]
    [InlineData("smash the window")]
    [InlineData("yell at the guard to wake up")]
    public async Task ALoudAction_IsRatedLoud(string action)
    {
        // The rules veto on the "extremely loud" label, so the label has to be earned. Under the
        // original wording all of these came back "silent".
        string noise = await AskNoiseAsync(action);

        Assert.Contains(noise, Room.LoudNoises, StringComparer.Ordinal);
    }

    [LiveTheory]
    [InlineData("I quietly creep over and slip the key off the table.")]
    [InlineData("grab the key")]
    [InlineData("tiptoe out while he snores")]
    public async Task AQuietAction_IsRatedQuiet(string action)
    {
        // The other half: the label must discriminate, or the veto fires on everything.
        string noise = await AskNoiseAsync(action);

        Assert.DoesNotContain(noise, Room.LoudNoises, StringComparer.Ordinal);
    }

    [LiveFact]
    public async Task TheNoiseScale_IsUsedAcrossItsRange()
    {
        // A scale that always returns the same value is not a judgement. Ordering is what makes the
        // Score kind worth demonstrating, so the loudest and quietest cases must not coincide.
        string loud = await AskNoiseAsync("I hurl the chair through the window.");
        string quiet = await AskNoiseAsync("I quietly slip the key off the table.");

        int loudIndex = NoiseLevels.All.ToList().IndexOf(loud);
        int quietIndex = NoiseLevels.All.ToList().IndexOf(quiet);

        Assert.True(loudIndex > quietIndex, $"breaking glass rated '{loud}' and quiet movement rated '{quiet}'");
    }

    [LiveTheory]
    [InlineData("grab the key", ActionIntent.TakeKey)]
    [InlineData("pocket the little brass thing", ActionIntent.TakeKey)]
    [InlineData("I quietly creep over and slip the key off the table.", ActionIntent.TakeKey)]
    [InlineData("unlock the door", ActionIntent.UnlockDoor)]
    [InlineData("I hurl the chair through the window.", ActionIntent.BreakWindow)]
    [InlineData("throw the chair through the window", ActionIntent.BreakWindow)]
    [InlineData("yell at the guard to wake up", ActionIntent.TalkToGuard)]
    [InlineData("sneak past the guard", ActionIntent.SneakPastGuard)]
    public async Task ThePlayersOwnWording_ClassifiesToTheRightIntent(string action, string expected)
    {
        // The demo's central claim: the player never learns commands. Each of these is a different way
        // of saying the same thing, and all have to land on the same intent.
        TurnJudgement judgement = await JudgeAsync(action, StateWithoutKey);

        Assert.Equal(expected, judgement.Intent);
    }

    [LiveTheory]
    [InlineData("what time is it", ActionIntent.Other)]
    [InlineData("hello", ActionIntent.Other)]
    [InlineData("I look around the room", ActionIntent.Other)]
    public async Task AnActionThatIsNotAnAction_ClassifiesAsOther(string action, string expected)
    {
        // "other" is a real answer, not a failure. The game uses the progress judgement to decide what
        // comes of it, so a statement must not be forced into a physical action.
        TurnJudgement judgement = await JudgeAsync(action, StateWithoutKey);

        Assert.Equal(expected, judgement.Intent);
    }

    [LiveFact]
    public async Task AFullTurn_ComesBackCompleteEnoughToPlay()
    {
        // Every question answered with the right kind. A partial response still plays — the game
        // substitutes neutral values — but it means the prompt is not doing its job.
        TurnJudgement judgement = await JudgeAsync("grab the key", StateWithoutKey);

        Assert.False(
            judgement.HadIncompleteAnswer,
            "a turn came back with something missing or unusable, so the game would substitute a neutral value");

        Assert.InRange(judgement.Plausible, 0.0, 1.0);
        Assert.InRange(judgement.WakesGuard, 0.0, 1.0);
        Assert.InRange(judgement.Progress, 0.0, 1.0);
        Assert.Contains(judgement.Noise, NoiseLevels.All, StringComparer.Ordinal);
    }

    [LiveFact]
    public async Task ACreativeAction_IsJudgedOnWhetherItWouldWork()
    {
        // The progress judgement is the success chance for moves that map to no named intent, so it has
        // to be able to score high for a creative attempt that is actually a good idea.
        TurnJudgement judgement = await JudgeAsync(
            "I drape my coat over the window so the room looks empty and then ease the door open",
            StateWithKey);

        Assert.InRange(judgement.Progress, 0.0, 1.0);
    }

    private static async Task<double> AskPlausibleAsync(string action, string state)
    {
        Dictionary<string, Question> questions = QuestionSet.Build(action);

        SystemOneResponse response = await LiveClient.Shared.SystemOneAsync(
            state,
            questions,
            model: null,
            CancellationToken.None);

        return TurnJudgement.FromResponse(response).Plausible;
    }

    private static async Task<string> AskNoiseAsync(string action)
    {
        Dictionary<string, Question> questions = QuestionSet.Build(action);

        SystemOneResponse response = await LiveClient.Shared.SystemOneAsync(
            StateWithoutKey,
            questions,
            model: null,
            CancellationToken.None);

        return TurnJudgement.FromResponse(response).Noise;
    }

    private static async Task<TurnJudgement> JudgeAsync(string action, string state)
    {
        Dictionary<string, Question> questions = QuestionSet.Build(action);

        SystemOneResponse response = await LiveClient.Shared.SystemOneAsync(
            state,
            questions,
            model: null,
            CancellationToken.None);

        return TurnJudgement.FromResponse(response);
    }
}
