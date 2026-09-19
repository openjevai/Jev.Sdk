// WorldConsole.cs
// Part of Jev.Sdk.World. The console interface: banner, judgement display, state, and the turn loop.
//
// Kept apart from the runner so the engine has no opinion about how it is driven. Every line the game
// prints lives here.

using Jev.Sdk;

namespace Jev.Sdk.World;

/// <summary>
/// Runs a world interactively in a console.
/// </summary>
public static class WorldConsole
{
    /// <summary>
    /// Plays a world until it ends, the player quits, or input runs out.
    /// </summary>
    /// <param name="world">The world to play.</param>
    /// <param name="client">The client used to ask the model.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>True when the player won.</returns>
    public static async Task<bool> PlayAsync(
        WorldDefinition world,
        JevClient client,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(client);

        WorldRunner runner = new(world);

        Banner(world);
        State(runner.State);

        while (!runner.IsFinished)
        {
            Console.Write("> ");

            string? input = Console.ReadLine();

            if (input is null)
            {
                // End of input, which is what a piped transcript does. Not an error.
                break;
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                continue;
            }

            string trimmed = input.Trim();

            if (trimmed.Equals("quit", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            TurnReport report;

            try
            {
                report = await runner.TurnAsync(client, trimmed, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine();
                Console.WriteLine("Cancelled.");
                break;
            }

            if (!report.Succeeded)
            {
                // A failed turn must not end the run: the player retries and the transcript continues.
                Error(report.Error!);
                continue;
            }

            Judgements(report.Judgements, world);
            Outcome(report.Result);
            Console.WriteLine();
            State(runner.State);
        }

        Ending(runner.State, world);

        return runner.State.Won;
    }

    /// <summary>Prints the opening banner.</summary>
    /// <param name="world">The world being played.</param>
    public static void Banner(WorldDefinition world)
    {
        ArgumentNullException.ThrowIfNull(world);

        Console.WriteLine(world.Title);
        Console.WriteLine(world.Goal);
        Console.WriteLine();

        if (world.Examples.Count > 0)
        {
            Console.WriteLine("Try:");

            foreach (string example in world.Examples)
            {
                Console.WriteLine($"  \"{example}\"");
            }

            Console.WriteLine();
        }
    }

    /// <summary>Prints the state summary.</summary>
    /// <param name="state">The current state.</param>
    public static void State(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        Console.WriteLine($"  {state.Summarise()}");
        Console.WriteLine();
    }

    /// <summary>
    /// Prints the model's judgement for a turn, one line per declared judgement.
    /// </summary>
    /// <param name="judgements">The turn's judgements.</param>
    /// <param name="world">The world, for the declared ordering.</param>
    public static void Judgements(JudgementSet judgements, WorldDefinition world)
    {
        ArgumentNullException.ThrowIfNull(judgements);
        ArgumentNullException.ThrowIfNull(world);

        Console.WriteLine();
        Console.WriteLine("Jev:");

        foreach (JudgementDefinition definition in world.Judgements)
        {
            JudgementResult result = judgements[definition.Key];

            string shown = result.Kind switch
            {
                JudgementKinds.Choice => result.Text is { } choice
                    ? $"{choice}{(result.Number is { } confidence ? $"  {confidence * 100:0}%" : string.Empty)}"
                    : "(none)",
                JudgementKinds.Score => result.Text ?? "(none)",
                _ => result.Number is { } value ? Percent(value) : "(none)",
            };

            Console.WriteLine($"  {definition.Key,-14} {shown}");
        }

        if (judgements.AnyUnusable)
        {
            // Said out loud rather than hidden: the world substituted a declared fallback, and a player
            // should not be told the model answered when it did not.
            Console.WriteLine($"  (unusable: {string.Join(", ", judgements.UnusableKeys)}; declared fallbacks were used)");
        }

        Console.WriteLine();
    }

    /// <summary>Prints what the rules did.</summary>
    /// <param name="result">The turn's result.</param>
    public static void Outcome(TurnResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        foreach (string line in result.Narration)
        {
            Console.WriteLine(line);
        }
    }

    /// <summary>Prints the closing message.</summary>
    /// <param name="state">The final state.</param>
    /// <param name="world">The world, for its ending text and turn limit.</param>
    public static void Ending(WorldState state, WorldDefinition world)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(world);

        Console.WriteLine();

        if (state.Won)
        {
            Console.WriteLine($"{world.Ending.Won} ({state.Turns} turn{(state.Turns == 1 ? string.Empty : "s")}.)");
            return;
        }

        Console.WriteLine(world.Ending.Lost);
        Console.WriteLine($"({world.TurnLimit} turns is the limit.)");
    }

    /// <summary>Prints a failed turn without ending the run.</summary>
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
