// Program.cs
// Part of Jev.Sdk.Sample. A deliberately small console client for the library. It asks for an
// API key if one is not in the environment, offers the two endpoints, prompts for the state and
// a typed question, sends the call, and prints the raw answer.
//
// It is kept plain on purpose: no argument parsing, no framework, no output formatting beyond
// what makes the result readable. Its job is to exercise the library by hand.

using System.Globalization;
using Jev.Sdk;

namespace Jev.Sdk.Sample;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        using CancellationTokenSource cancellation = new();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
            Console.WriteLine();
            Console.WriteLine("Cancelling...");
        };

        PrintBanner();

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

        using JevClient client = new(new JevClientOptions
        {
            ApiKey = apiKey,

            // Retries are on by default. Kept explicit here so the sample shows the knob.
            MaxRetries = 2,
        });

        try
        {
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("Choose an endpoint:");
                Console.WriteLine("  1  GET  /v1/models          list available models");
                Console.WriteLine("  2  POST /v1/systemone       evaluate a state against a typed question");
                Console.WriteLine("  q  quit");
                Console.Write("> ");

                string? choice = Console.ReadLine()?.Trim();

                if (string.IsNullOrWhiteSpace(choice) || choice.Equals("q", StringComparison.OrdinalIgnoreCase))
                {
                    return 0;
                }

                Console.WriteLine();

                switch (choice)
                {
                    case "1":
                        await ListModelsAsync(client, cancellation.Token).ConfigureAwait(false);
                        break;

                    case "2":
                        await EvaluateAsync(client, cancellation.Token).ConfigureAwait(false);
                        break;

                    default:
                        Console.WriteLine("Unrecognised choice.");
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Cancelled.");
            return 130;
        }
        catch (JevException exception)
        {
            // One catch for everything the library raises, which is the point of a single
            // exception root.
            Console.WriteLine();
            Console.WriteLine($"Request failed: {exception.GetType().Name}");
            Console.WriteLine($"  {exception.Message}");

            if (exception is JevValidationException validation)
            {
                foreach (ErrorDetails detail in validation.Details)
                {
                    Console.WriteLine($"  - {detail}");
                }
            }

            return 1;
        }
    }

    private static string? ResolveApiKey(string[] args)
    {
        if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
        {
            return args[0].Trim();
        }

        string? fromEnvironment = Environment.GetEnvironmentVariable(JevEnvironment.ApiKeyVariable);

        return string.IsNullOrWhiteSpace(fromEnvironment) ? null : fromEnvironment;
    }

    private static async Task ListModelsAsync(JevClient client, CancellationToken cancellationToken)
    {
        Console.WriteLine("GET /v1/models");

        IReadOnlyList<ModelMetadata> models = await client.GetModelsAsync(cancellationToken).ConfigureAwait(false);

        if (models.Count == 0)
        {
            Console.WriteLine("No models were returned.");
            return;
        }

        foreach (ModelMetadata model in models)
        {
            Console.WriteLine($"  {model.Name}");
            Console.WriteLine($"    {model.Description}");

            if (model.ParsedReleaseDate is { } released)
            {
                Console.WriteLine($"    released {released:yyyy-MM-dd}");
            }
        }
    }

    private static async Task EvaluateAsync(JevClient client, CancellationToken cancellationToken)
    {
        Console.WriteLine("POST /v1/systemone");
        Console.WriteLine();

        Console.Write("Model (blank for the default): ");
        string? model = Console.ReadLine()?.Trim();
        model = string.IsNullOrWhiteSpace(model) ? null : model;

        Console.Write("Question kind (noul / choice / score): ");
        string kind = (Console.ReadLine()?.Trim() ?? string.Empty).ToLowerInvariant();

        Console.Write("Question instructions: ");
        string instructions = Console.ReadLine() ?? string.Empty;

        Dictionary<string, Question> questions = new(StringComparer.Ordinal);
        string questionId;

        switch (kind)
        {
            case "noul":
                questionId = "answer";
                questions[questionId] = Question.Noul(instructions);
                break;

            case "choice":
                {
                    Console.WriteLine("Enter options, one per line. A description may follow a '|'.");
                    Console.WriteLine("Finish with a blank line. At least one option is required.");

                    Dictionary<string, string?> options = new(StringComparer.Ordinal);

                    while (true)
                    {
                        Console.Write("option: ");
                        string? line = Console.ReadLine();

                        if (string.IsNullOrWhiteSpace(line))
                        {
                            break;
                        }

                        string[] parts = line.Split('|', 2);
                        string name = parts[0].Trim();

                        if (name.Length == 0)
                        {
                            continue;
                        }

                        options[name] = parts.Length > 1 ? parts[1].Trim() : null;
                    }

                    questionId = "answer";
                    questions[questionId] = Question.Choice(instructions, options);
                    break;
                }

            case "score":
                {
                    Console.WriteLine("Enter levels in ascending order, one per line.");
                    Console.WriteLine("Finish with a blank line. At least two levels are required.");

                    List<string> levels = [];

                    while (true)
                    {
                        Console.Write("level: ");
                        string? line = Console.ReadLine();

                        if (string.IsNullOrWhiteSpace(line))
                        {
                            break;
                        }

                        levels.Add(line.Trim());
                    }

                    questionId = "answer";
                    questions[questionId] = Question.Score(instructions, [.. levels]);
                    break;
                }

            default:
                Console.WriteLine($"Unrecognised question kind '{kind}'. Use noul, choice, or score.");
                return;
        }

        Console.WriteLine();
        Console.WriteLine("State to evaluate. Enter the text, then a blank line to send.");
        Console.WriteLine("Prefix a line with 'json:' to send structured JSON instead.");

        string? stateJson = null;
        List<string> stateLines = [];

        while (true)
        {
            Console.Write("state: ");
            string? line = Console.ReadLine();

            if (line is null || line.Length == 0)
            {
                break;
            }

            if (line.StartsWith("json:", StringComparison.OrdinalIgnoreCase))
            {
                stateJson = line["json:".Length..].Trim();
                continue;
            }

            stateLines.Add(line);
        }

        SystemOneResponse response = stateJson is not null
            ? await client.SystemOneAsync(stateJson, questions, model, cancellationToken).ConfigureAwait(false)
            : await client.SystemOneAsync(
                string.Join(Environment.NewLine, stateLines),
                questions,
                model,
                cancellationToken).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine($"model: {response.Model}");

        Answer answer = response[questionId];

        switch (answer)
        {
            case NoulAnswer noul:
                Console.WriteLine($"noul: {noul.Noul.ToString("0.####", CultureInfo.InvariantCulture)}");
                break;

            case ChoiceAnswer choice:
                Console.WriteLine($"choice: {choice.Choice}");
                Console.WriteLine($"confidence: {choice.Confidence!.Value.ToString("0.####", CultureInfo.InvariantCulture)}");

                foreach (KeyValuePair<string, double> probability in choice.Probabilities)
                {
                    Console.WriteLine($"  {probability.Key}: {probability.Value.ToString("0.####", CultureInfo.InvariantCulture)}");
                }

                break;

            case ScoreAnswer score:
                Console.WriteLine($"score: {score.Score.ToString("0.####", CultureInfo.InvariantCulture)}");
                Console.WriteLine($"confidence: {score.Confidence!.Value.ToString("0.####", CultureInfo.InvariantCulture)}");

                foreach (KeyValuePair<string, double> probability in score.Probabilities)
                {
                    score.Legend.TryGetValue(probability.Key, out StructuredValue? level);
                    Console.WriteLine(
                        $"  {probability.Key} ({level?.AsString() ?? level?.ToRawText() ?? "?"}): " +
                        probability.Value.ToString("0.####", CultureInfo.InvariantCulture));
                }

                break;

            case UnknownAnswer unknown:
                Console.WriteLine($"An answer kind this library does not model: '{unknown.Type}'");
                Console.WriteLine(unknown.RawJson.GetRawText());
                break;
        }

        if (response.Usage is { } usage)
        {
            Console.WriteLine($"usage: {usage.InputTokens} input, {usage.OutputTokens} output tokens");
        }
    }

    private static void PrintBanner()
    {
        Console.WriteLine("Jev.Sdk sample client");
        Console.WriteLine("Calls the TypeSafe AI System One API directly. Ctrl+C cancels a call in flight.");
        Console.WriteLine();
    }
}
