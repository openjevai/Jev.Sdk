// RoomTests.cs
// Part of Jev.Sdk.Game.Tests. The game rules, and the escape path end to end.
//
// These tests are why the rules live in their own class: the game is verifiable without a key, so a
// change to the rules is caught here rather than by playing the game and guessing.

namespace Jev.Sdk.Game.Tests;

public class RoomTests
{
    [Fact]
    public void TakingTheKey_HoldsIt()
    {
        RoomState state = new();
        Room room = new(new FixedDice(0.5));

        room.Apply(state, Judge.Make(ActionIntent.TakeKey));

        Assert.True(state.HasKey);
    }

    [Fact]
    public void TakingTheKeyTwice_SaysSoAndChangesNothing()
    {
        RoomState state = new() { HasKey = true };
        Room room = new(new FixedDice(0.5));

        TurnOutcome outcome = room.Apply(state, Judge.Make(ActionIntent.TakeKey));

        Assert.False(outcome.StateChanged);
        Assert.Contains(outcome.Narration, line => line.Contains("already have", StringComparison.Ordinal));
    }

    [Fact]
    public void UnlockingTheDoorWithoutTheKey_DoesNotUnlockIt()
    {
        RoomState state = new();
        Room room = new(new FixedDice(0.5));

        room.Apply(state, Judge.Make(ActionIntent.UnlockDoor));

        Assert.False(state.DoorUnlocked);
    }

    [Fact]
    public void UnlockingTheDoorWithTheKey_UnlocksIt()
    {
        RoomState state = new() { HasKey = true };
        Room room = new(new FixedDice(0.5));

        room.Apply(state, Judge.Make(ActionIntent.UnlockDoor));

        Assert.True(state.DoorUnlocked);
    }

    [Fact]
    public void BreakingTheWindow_BreaksIt()
    {
        RoomState state = new();
        Room room = new(new FixedDice(0.5));

        room.Apply(state, Judge.Make(ActionIntent.BreakWindow, noise: NoiseLevels.ExtremelyLoud));

        Assert.True(state.WindowBroken);
    }

    [Fact]
    public void AnImplausibleAction_ChangesNothing()
    {
        // The design's rule: plausibility is checked before intent, because an impossible action
        // cannot change the room however confident the model was about what the player meant.
        RoomState state = new();
        Room room = new(new FixedDice(0.5));

        TurnOutcome outcome = room.Apply(state, Judge.Make(ActionIntent.TakeKey, plausible: 0.1));

        Assert.False(state.HasKey);
        Assert.False(outcome.StateChanged);
    }

    [Fact]
    public void AnImplausibleButLoudAction_StillWakesTheGuard()
    {
        // Noise survives a failed attempt, which is what makes the noise question matter on its own
        // rather than only as a modifier of success.
        RoomState state = new();
        Room room = new(new FixedDice(0.5));

        room.Apply(state, Judge.Make(
            ActionIntent.BreakWindow,
            plausible: 0.05,
            noise: NoiseLevels.ExtremelyLoud));

        Assert.False(state.WindowBroken);
        Assert.True(state.GuardAwake);
    }

    [Fact]
    public void AnExtremelyLoudAction_WakesTheGuardEvenWhenTheModelSaidItWouldNot()
    {
        // The rules keep a veto. A smashed window is loud in a way no probability should be able to
        // talk the game out of.
        RoomState state = new();
        Room room = new(new FixedDice(0.5));

        room.Apply(state, Judge.Make(
            ActionIntent.BreakWindow,
            wakesGuard: 0.01,
            noise: NoiseLevels.ExtremelyLoud));

        Assert.True(state.GuardAwake);
    }

    [Fact]
    public void AttackingTheGuard_WakesThem()
    {
        RoomState state = new();
        Room room = new(new FixedDice(0.5));

        room.Apply(state, Judge.Make(ActionIntent.AttackGuard));

        Assert.True(state.GuardAwake);
    }

    [Fact]
    public void TalkingToASleepingGuard_WakesThem()
    {
        RoomState state = new();
        Room room = new(new FixedDice(0.5));

        room.Apply(state, Judge.Make(ActionIntent.TalkToGuard));

        Assert.True(state.GuardAwake);
    }

    [Fact]
    public void SneakingPastASleepingGuard_LeavesTheRoomWhenTheDoorIsOpen()
    {
        RoomState state = new() { DoorUnlocked = true };
        Room room = new(new FixedDice(0.5));

        room.Apply(state, Judge.Make(ActionIntent.SneakPastGuard));

        Assert.True(state.Escaped);
    }

    [Fact]
    public void SneakingPastASleepingGuard_WithALockedDoor_DoesNotEscape()
    {
        // Getting past the guard is not the same as getting out. The door still has to open.
        RoomState state = new();
        Room room = new(new FixedDice(0.5));

        TurnOutcome outcome = room.Apply(state, Judge.Make(ActionIntent.SneakPastGuard));

        Assert.False(state.Escaped);
        Assert.Contains(outcome.Narration, line => line.Contains("locked", StringComparison.Ordinal));
    }

    [Fact]
    public void SneakingPastAnAwakeGuard_FailsWhenTheRollIsAgainstYou()
    {
        RoomState state = new() { DoorUnlocked = true, GuardAwake = true };

        // A chance of 0.9 clears the threshold, and a roll of 0.95 is above it, so this fails.
        Room room = new(new FixedDice(0.95));

        room.Apply(state, Judge.Make(ActionIntent.SneakPastGuard, plausible: 0.9, progress: 0.9));

        Assert.False(state.Escaped);
    }

    [Fact]
    public void SneakingPastAnAwakeGuard_SucceedsWhenTheRollFavoursYou()
    {
        RoomState state = new() { DoorUnlocked = true, GuardAwake = true };
        Room room = new(new FixedDice(0.1));

        room.Apply(state, Judge.Make(ActionIntent.SneakPastGuard, plausible: 0.9, progress: 0.9));

        Assert.True(state.Escaped);
    }

    [Fact]
    public void SneakingPastAnAwakeGuard_FailsWhenTheOddsAreTooLowEvenOnAGoodRoll()
    {
        // The threshold is a floor on the model's own judgement, not only on the dice: an action it
        // judged hopeless does not become possible because the roll happened to be low.
        RoomState state = new() { DoorUnlocked = true, GuardAwake = true };
        Room room = new(new FixedDice(0.0));

        room.Apply(state, Judge.Make(ActionIntent.SneakPastGuard, plausible: 0.2, progress: 0.2));

        Assert.False(state.Escaped);
    }

    [Fact]
    public void ACreativeAttempt_CanDistractAnAwakeGuard()
    {
        // The design's reason for the progress question: text that maps to no known action still does
        // something when the model judged it a step forward.
        RoomState state = new() { GuardAwake = true };
        Room room = new(new FixedDice(0.1));

        TurnOutcome outcome = room.Apply(state, Judge.Make(
            ActionIntent.Other,
            progress: 0.9));

        Assert.False(state.GuardAwake);
        Assert.Contains(outcome.Narration, line => line.Contains("distracted", StringComparison.Ordinal));
    }

    [Fact]
    public void ACreativeAttempt_ThatTheModelJudgedPointless_DoesNothing()
    {
        RoomState state = new();
        Room room = new(new FixedDice(0.1));

        TurnOutcome outcome = room.Apply(state, Judge.Make(
            ActionIntent.Other,
            progress: 0.2));

        Assert.False(outcome.StateChanged);
        Assert.Contains(outcome.Narration, line => line.Contains("Nothing comes of it", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryTurn_Counts()
    {
        RoomState state = new();
        Room room = new(new FixedDice(0.5));

        room.Apply(state, Judge.Make(ActionIntent.Other));
        room.Apply(state, Judge.Make(ActionIntent.Other));

        Assert.Equal(2, state.Turns);
    }

    [Fact]
    public void TheWholeGame_CanBeWonByTakingTheKeyAndUnlockingTheDoor()
    {
        // The escape path the design describes, played out: take the key, unlock the door, leave. This
        // is the test that would fail if any single rule regressed.
        RoomState state = new();
        Room room = new(new FixedDice(0.5));

        room.Apply(state, Judge.Make(ActionIntent.TakeKey));
        Assert.True(state.HasKey);
        Assert.False(state.Escaped);

        room.Apply(state, Judge.Make(ActionIntent.UnlockDoor));
        Assert.True(state.DoorUnlocked);
        Assert.False(state.Escaped);

        room.Apply(state, Judge.Make(ActionIntent.SneakPastGuard));
        Assert.True(state.Escaped);
        Assert.Equal(3, state.Turns);
    }

    [Fact]
    public void TheRoomDescription_ReflectsTheState()
    {
        // The description is the model's whole view of the world, so a stale one would mean the model
        // judged against the wrong room.
        RoomState state = new();

        Assert.Contains("key is on a table", state.Describe(), StringComparison.Ordinal);
        Assert.Contains("door is locked", state.Describe(), StringComparison.Ordinal);
        Assert.Contains("window is intact", state.Describe(), StringComparison.Ordinal);
        Assert.Contains("guard is asleep", state.Describe(), StringComparison.Ordinal);

        state.HasKey = true;
        state.DoorUnlocked = true;
        state.WindowBroken = true;
        state.GuardAwake = true;

        Assert.Contains("key is in your pocket", state.Describe(), StringComparison.Ordinal);
        Assert.Contains("door is unlocked", state.Describe(), StringComparison.Ordinal);
        Assert.Contains("window is broken", state.Describe(), StringComparison.Ordinal);
        Assert.Contains("guard is awake", state.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void NullArguments_AreRejected()
    {
        Room room = new(new FixedDice(0.5));

        Assert.Throws<ArgumentNullException>(() => room.Apply(null!, Judge.Make(ActionIntent.Other)));
        Assert.Throws<ArgumentNullException>(() => room.Apply(new RoomState(), null!));
        Assert.Throws<ArgumentNullException>(() => Room.IsLoud(null!));
    }
}
