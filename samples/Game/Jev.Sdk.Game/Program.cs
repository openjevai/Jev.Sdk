// Program.cs
// Part of Jev.Sdk.Game. The turn loop: read a line, ask Jev, apply the rules, narrate.
//
// The loop is deliberately this thin. Everything interesting is in QuestionSet (what is asked),
// TurnJudgement (how the answers are read), and Room (what happens). This file only wires them
// together, so the game can be tested without a console and the API surface can be read in one place.
//
// See ../../Game.md for the design this implements.

using Jev.Sdk;
using Jev.Sdk.DependencyInjection;
using Jev.Sdk.Game;
using Microsoft.Extensions.Configuration;

using CancellationTokenSource cancellation = new();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
    Console.WriteLine();
    Console.WriteLine("Quitting.");
};

const int TurnLimit = 25;

LoadDotEnv();

string? apiKey = ResolveApiKey(args);

if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.WriteLine($"No API key was supplied and {JevEnvironment.ApiKeyVariable} is not set.");
    Console.WriteLine("Set the environment variable, pass the key as the first argument, or paste it below.");
    Console.Write("API key (or blank to use the environment): ");

    string? entered = Console.ReadLine();

    if (!string.IsNullOrWhiteSpace(entered))
    {
        apiKey = entered.Trim();
    }
}

if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.WriteLine("An API key is required: every turn asks the model what you are trying to do.");
    return 1;
}

using JevClient client = new(new JevClientOptions
{
    ApiKey = apiKey,
    MaxRetries = 2,
});

RoomState state = new();
Room room = new();

ConsoleOutput.Banner();
ConsoleOutput.State(state);

try
{
    while (!state.Escaped && state.Turns < TurnLimit)
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

        if (input.Trim().Equals("quit", StringComparison.OrdinalIgnoreCase)
            || input.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
        {
            break;
        }

        Dictionary<string, Question> questions = QuestionSet.Build(input);

        SystemOneResponse response;

        try
        {
            // One call, five questions, three kinds. The room's own description is the state, so the
            // model judges the action against the situation rather than in the abstract.
            response = await client.SystemOneAsync(
                state.Describe(),
                questions,
                model: null,
                cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine();
            Console.WriteLine("Cancelled.");
            break;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A failed turn must not end the game: the player retries, and the transcript keeps going.
            ConsoleOutput.Error(exception);
            continue;
        }

        TurnJudgement judgement = TurnJudgement.FromResponse(response);

        ConsoleOutput.Judgement(judgement);

        TurnOutcome outcome = room.Apply(state, judgement);

        ConsoleOutput.Outcome(outcome);
        Console.WriteLine();

        ConsoleOutput.State(state);
    }
}
catch (OperationCanceledException)
{
    // Cancellation is reported where it is raised for a turn. This catches the case where it lands
    // outside that block, which is otherwise silent.
    Console.WriteLine();
    Console.WriteLine("Cancelled.");
}

ConsoleOutput.Ending(state, TurnLimit);

// Exit code reflects the outcome, so the game is usable in a script.
return state.Escaped ? 0 : 1;

// Loads a .env file into the process environment, if one is present beside the executable.
//
// .NET does not read .env files, so the shipped example would otherwise be decorative. A missing or
// unreadable file is ignored: the key can still come from the environment or a prompt.
static void LoadDotEnv()
{
    string candidate = Path.Combine(AppContext.BaseDirectory, ".env");

    if (!File.Exists(candidate))
    {
        return;
    }

    try
    {
        DotNetEnv.Env.Load(candidate);
        Console.WriteLine($"Loaded environment from {candidate}");
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
    {
        Console.WriteLine($"Ignoring {candidate}: {exception.Message}");
    }
}

// Resolves the API key from the argument, the environment, or the settings files, in that order.
//
// Returns the key, or null when none is configured.
static string? ResolveApiKey(string[] arguments)
{
    if (arguments.Length > 0 && !string.IsNullOrWhiteSpace(arguments[0]))
    {
        return arguments[0].Trim();
    }

    string? fromEnvironment = Environment.GetEnvironmentVariable(JevEnvironment.ApiKeyVariable);

    if (!string.IsNullOrWhiteSpace(fromEnvironment))
    {
        return fromEnvironment;
    }

    try
    {
        // The library's own loader, so this resolves configuration the way a real host does.
        IConfigurationRoot configuration = JevConfigurationLoader.Build(AppContext.BaseDirectory);
        string? fromFile = JevOptionsBinding.FromConfiguration(configuration).ApiKey;

        return IsPlaceholder(fromFile) ? null : fromFile;
    }
    catch (JevConfigurationException exception)
    {
        Console.WriteLine($"Ignoring settings files: {exception.Message}");

        return null;
    }
}

// True when a configured value is still the placeholder shipped in the example files.
//
// Top-level statements cannot carry XML documentation on their local functions, which is why these
// three helpers use plain comments.
static bool IsPlaceholder(string? value) =>
    string.IsNullOrWhiteSpace(value)
    || value.Trim().Equals("REPLACE ME", StringComparison.OrdinalIgnoreCase);
