// Program.cs
// Part of Jev.Sdk.World.Play. The executable: load a world document, then play it.
//
// Usage:
//   Jev.Sdk.World.Play <world.md | world.json>       play a world
//   Jev.Sdk.World.Play --validate <path>             check a world loads, without playing
//   Jev.Sdk.World.Play --list                        list worlds beside the executable
//
// A world document is markdown by convention, because a world is written by a person and prose
// explains the rules better than a comment does. JSON is accepted too, for a world that is generated
// rather than written.
//
// Configuration is the same as the rest of this repository: TYPESAFE_API_KEY, .env, or a settings
// file. Nothing here opens a file the library would not.

using Jev.Sdk;
using Jev.Sdk.DependencyInjection;
using Jev.Sdk.World;
using Microsoft.Extensions.Configuration;

using CancellationTokenSource cancellation = new();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
    Console.WriteLine();
    Console.WriteLine("Quitting.");
};

string[] arguments = args.Where(a => !string.IsNullOrWhiteSpace(a)).ToArray();

if (arguments.Length == 0 || arguments[0] is "-h" or "--help")
{
    Usage();
    return arguments.Length == 0 ? 1 : 0;
}

string worldsDirectory = Path.Combine(AppContext.BaseDirectory, "worlds");

if (arguments[0] == "--list")
{
    return ListWorlds(worldsDirectory);
}

bool validateOnly = arguments[0] == "--validate";
string worldPath = validateOnly
    ? arguments.ElementAtOrDefault(1) ?? string.Empty
    : arguments[0];

if (string.IsNullOrWhiteSpace(worldPath))
{
    Console.WriteLine("No world file given.");
    Usage();
    return 1;
}

// A bare name resolves against the worlds folder beside the executable, so the common case is a word
// rather than a path. A folder is a valid world source (an unpacked package), so existence is checked
// for either kind.
if (!Exists(worldPath) && !Path.IsPathRooted(worldPath))
{
    string candidate = Path.Combine(worldsDirectory, worldPath);

    if (!Exists(candidate) && !HasKnownExtension(worldPath))
    {
        candidate += ".md";
    }

    if (Exists(candidate))
    {
        worldPath = candidate;
    }
}

if (!Exists(worldPath))
{
    Console.WriteLine($"No such world file: {worldPath}");

    if (Directory.Exists(worldsDirectory))
    {
        Console.WriteLine($"Worlds beside this executable: {string.Join(", ", WorldFileNames(worldsDirectory))}");
    }

    return 1;
}

WorldDefinition world;
OpenedWorld? package = null;

try
{
    // One entry point for every source: a zip, a folder, a markdown document, or JSON. Detection is by
    // content, so a package that was renamed is still opened.
    package = WorldPackage.Open(worldPath);
    world = package.World;
}
catch (WorldLoadException exception)
{
    // A world that will not load is the most likely failure here, and the message names the offending
    // key, so it is printed plainly rather than as an unexpected error.
    Console.WriteLine($"The world could not be loaded.");
    Console.WriteLine($"  {exception.Message}");
    return 1;
}

// The unpacked folder lives as long as the run, and is removed on the way out.
using OpenedWorld owned = package;

if (validateOnly)
{
    Console.WriteLine($"{Path.GetFileName(worldPath)}: ok");
    Console.WriteLine($"  {world.Title} ({world.Id}), schema {world.SchemaVersion}");
    Console.WriteLine($"  {world.State.Count} state field(s), {world.Judgements.Count} judgement(s), {world.Rules.Count} rule(s), limit {world.TurnLimit}");
    return 0;
}

LoadDotEnv();

string? apiKey = ResolveApiKey(arguments.Skip(1));

if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.WriteLine($"No API key was supplied and {JevEnvironment.ApiKeyVariable} is not set.");
    Console.WriteLine("Set the environment variable, pass the key after the world file, or paste it below.");
    Console.Write("API key: ");

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

try
{
    bool won = await WorldConsole.PlayAsync(world, client, cancellation.Token);

    // Exit code reflects the outcome, so a world is usable in a script.
    return won ? 0 : 1;
}
catch (OperationCanceledException)
{
    Console.WriteLine();
    Console.WriteLine("Cancelled.");

    return 1;
}

// A world source is a file or a folder; both are legitimate.
static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

static bool HasKnownExtension(string path) =>
    path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
    || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
    || path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
    || path.EndsWith(".game", StringComparison.OrdinalIgnoreCase);

static IEnumerable<string> WorldFileNames(string directory)
{
    IEnumerable<string> files = Directory.EnumerateFiles(directory)
        .Where(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            || f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            || f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        .Select(Path.GetFileName)
        .Where(n => n is not null)
        .Select(n => n!);

    // A folder is a world package too. Subfolders are listed only when they hold a world document
    // directly, so a "worlds/" full of unrelated directories does not list noise.
    IEnumerable<string> folders = Directory.EnumerateDirectories(directory)
        .Select(Path.GetFileName)
        .Where(n => !string.IsNullOrEmpty(n))
        .Select(n => n!)
        .Where(n => Directory.EnumerateFiles(Path.Combine(directory, n), "*.md").Any()
            || Directory.EnumerateFiles(Path.Combine(directory, n), "*.json").Any());

    return files.Concat(folders).OrderBy(n => n, StringComparer.Ordinal);
}

static int ListWorlds(string directory)
{
    if (!Directory.Exists(directory))
    {
        Console.WriteLine($"No worlds folder beside this executable ({directory}).");

        return 1;
    }

    List<string> names = [.. WorldFileNames(directory)];

    if (names.Count == 0)
    {
        Console.WriteLine("No worlds found.");

        return 1;
    }

    Console.WriteLine("Worlds:");

    foreach (string name in names)
    {
        string title = name;

        try
        {
            using OpenedWorld opened = WorldPackage.Open(Path.Combine(directory, name));
            title = $"{name}  —  {opened.World.Title}";
        }
        catch (WorldLoadException exception)
        {
            // A broken world is listed with why, rather than omitted: silently hiding it would make
            // "my world is not here" the only symptom.
            title = $"{name}  —  WILL NOT LOAD: {exception.Message}";
        }

        Console.WriteLine($"  {title}");
    }

    return 0;
}

static void Usage()
{
    Console.WriteLine("Jev.Sdk.World.Play — play a world driven by the Jev API.");
    Console.WriteLine();
    Console.WriteLine("  play <world>             play a world: a .zip package, a folder, a .md, or a .json");
    Console.WriteLine("  play --validate <world>  check a world loads and summarise it, without playing");
    Console.WriteLine("  play --list              list the worlds beside this executable");
    Console.WriteLine();
    Console.WriteLine("A world is a document. Its state, questions and rules are read from it; nothing about a");
    Console.WriteLine("particular world is compiled in. A .zip is unpacked to a temporary folder and played from");
    Console.WriteLine("there, and a JSON file may hold a definition, a list of them, or markdown inside a string.");
}

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

static string? ResolveApiKey(IEnumerable<string> arguments)
{
    string? supplied = arguments.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a));

    if (supplied is not null)
    {
        return supplied.Trim();
    }

    string? fromEnvironment = Environment.GetEnvironmentVariable(JevEnvironment.ApiKeyVariable);

    if (!string.IsNullOrWhiteSpace(fromEnvironment))
    {
        return fromEnvironment;
    }

    try
    {
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

static bool IsPlaceholder(string? value) =>
    string.IsNullOrWhiteSpace(value)
    || value.Trim().Equals("REPLACE ME", StringComparison.OrdinalIgnoreCase);
