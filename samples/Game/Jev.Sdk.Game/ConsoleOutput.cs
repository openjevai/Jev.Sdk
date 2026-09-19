// ConsoleOutput.cs
// Part of Jev.Sdk.Game. Every line the game prints.
//
// Output lives here rather than inline so the transcript in the design document can be reproduced by
// reading one file, and so the game's narration and the model's judgement are visibly separate: the
// judgement is printed under a "Jev:" heading exactly as the design shows, then the room narrates
// what actually happened. Keeping them apart is the demonstration.

using Jev.Sdk;

namespace Jev.Sdk.Game;

/// <summary>
/// Writes the game's output.
/// </summary>
public static class ConsoleOutput
{
    /// <summary>Prints the opening banner and the rules.</summary>
    public static void Banner()
    {
        Console.WriteLine("Escape the Room");
        Console.WriteLine("A demo game for the Jev.Sdk client. Type anything you like. Ctrl+C quits.");
        Console.WriteLine();
        Console.WriteLine("You are in a small locked room. There is a key on a table, a window,");
        Console.WriteLine("and a guard asleep in the corner. Get out.");
        Console.WriteLine();
        Console.WriteLine("Try: \"grab the key\", \"unlock the door\", \"throw the chair through the window\",");
        Console.WriteLine("     \"sneak past the guard\", \"yell at the guard to wake up\".");
        Console.WriteLine();
    }

    /// <summary>Prints the room as it stands.</summary>
    /// <param name="state">The current state.</param>
    public static void State(RoomState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        Console.WriteLine(
            $"  key: {(state.HasKey ? "held" : "on the table")}   "
            + $"door: {(state.DoorUnlocked ? "unlocked" : "locked")}   "
            + $"window: {(state.WindowBroken ? "broken" : "intact")}   "
            + $"guard: {(state.GuardAwake ? "awake" : "asleep")}");
        Console.WriteLine();
    }

    /// <summary>
    /// Prints the model's judgement for a turn, in the shape the design document shows.
    /// </summary>
    /// <param name="judgement">The judgement to print.</param>
    public static void Judgement(TurnJudgement judgement)
    {
        ArgumentNullException.ThrowIfNull(judgement);

        Console.WriteLine();
        Console.WriteLine("Jev:");

        string confidence = judgement.IntentConfidence is { } value
            ? $"  {value * 100:0}%"
            : "  -";

        Console.WriteLine($"  intent:      {judgement.Intent,-16}{confidence}");
        Console.WriteLine($"  plausible:   {Percent(judgement.Plausible)}");
        Console.WriteLine($"  wakes_guard: {Percent(judgement.WakesGuard)}");
        Console.WriteLine($"  noise:       {judgement.Noise}");
        Console.WriteLine($"  progress:    {Percent(judgement.Progress)}");

        if (judgement.HadIncompleteAnswer)
        {
            // Said out loud rather than hidden: the game substituted a neutral value, and a player
            // (or a reader of this demo) should not be told the model answered when it did not.
            Console.WriteLine("  (some of that was not returned; neutral values were used)");
        }

        Console.WriteLine();
    }

    /// <summary>Prints the outcome narration.</summary>
    /// <param name="outcome">The outcome to print.</param>
    public static void Outcome(TurnOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        foreach (string line in outcome.Narration)
        {
            Console.WriteLine(line);
        }
    }

    /// <summary>Prints the closing message for a finished game.</summary>
    /// <param name="state">The final state.</param>
    /// <param name="turnLimit">The limit that ended the game, when it ended on turns rather than escape.</param>
    public static void Ending(RoomState state, int turnLimit)
    {
        ArgumentNullException.ThrowIfNull(state);

        Console.WriteLine();

        if (state.Escaped)
        {
            Console.WriteLine($"You escaped in {state.Turns} turn{(state.Turns == 1 ? string.Empty : "s")}.");
        }
        else
        {
            Console.WriteLine($"You did not get out. The guard is still there.");
            Console.WriteLine($"({turnLimit} turns is the limit for one game.)");
        }
    }

    /// <summary>Prints a turn's error without ending the game.</summary>
    /// <param name="exception">The failure.</param>
    public static void Error(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        Console.WriteLine();

        if (exception is JevException jev)
        {
            Console.WriteLine($"The call failed: {jev.GetType().Name}");
            Console.WriteLine($"  {jev.Message}");
        }
        else
        {
            Console.WriteLine($"The call failed: {exception.GetType().Name}");
            Console.WriteLine($"  {exception.Message}");
        }

        Console.WriteLine("The turn was skipped. Try again.");
    }

    private static string Percent(double value) => $"{value * 100:0}%";
}
