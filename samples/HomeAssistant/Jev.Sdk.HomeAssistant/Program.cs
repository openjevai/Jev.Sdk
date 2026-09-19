using System.Text.Json;
using Jev.Sdk;
using Jev.Sdk.HomeAssistant;

using CancellationTokenSource cancellation = new();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

Console.WriteLine("Jev.Sdk Home Assistant sample");
Console.WriteLine("This sample discovers Home Assistant entities with raw HTTP calls.");
Console.WriteLine("It sends only a reviewed, confirmed turn-on or turn-off plan to Home Assistant.");
Console.WriteLine();

Uri homeAssistantUrl = ReadHomeAssistantUrl();
string homeAssistantToken = ReadRequiredSecret("Home Assistant long-lived access token");
string jevApiKey = ReadRequiredSecret("Jev API key");

using HttpClient httpClient = new();
HomeAssistantCommandFactory commandFactory = HomeAssistantCommandFactory.CreateDefault();
HomeAssistantApiClient homeAssistant = new(httpClient, homeAssistantUrl, homeAssistantToken);
using JevClient jev = new(new JevClientOptions { ApiKey = jevApiKey });

try
{
    IReadOnlyDictionary<string, HomeAssistantEntity> entities = await homeAssistant
        .GetSupportedEntitiesAsync(commandFactory.SupportedDomains, cancellation.Token)
        .ConfigureAwait(false);

    if (entities.Count == 0)
    {
        Console.WriteLine("Home Assistant returned no supported light, switch, or fan entities.");
        return 1;
    }

    Console.WriteLine($"Discovered {entities.Count} controllable entity or entities.");

    while (!cancellation.IsCancellationRequested)
    {
        Console.WriteLine();
        Console.Write("Command (or blank to quit): ");
        string? command = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(command))
        {
            break;
        }

        SystemOneResponse judgement = await jev.SystemOneAsync(
            BuildJevState(command, entities.Values),
            BuildQuestions(entities),
            model: null,
            cancellation.Token).ConfigureAwait(false);

        if (judgement.Noul("is_supported") < 0.80)
        {
            Console.WriteLine("Jev did not judge that as a supported single-device turn-on/turn-off command. Nothing was sent.");
            continue;
        }

        ChoiceAnswer entityAnswer = judgement["entity"].AsChoice();
        ChoiceAnswer actionAnswer = judgement["action"].AsChoice();

        if (entityAnswer.Confidence is < 0.80 || actionAnswer.Confidence is < 0.80)
        {
            Console.WriteLine("Jev was not confident enough to select an entity and action. Nothing was sent.");
            continue;
        }

        if (!HomeAssistantCommandPlan.TryCreate(entities, entityAnswer.Choice, actionAnswer.Choice, out HomeAssistantCommandPlan? plan)
            || plan is null)
        {
            Console.WriteLine("Jev selected a command outside this sample's supported catalog. Nothing was sent.");
            continue;
        }

        HomeAssistantEntity target = entities[plan.EntityId];
        Console.WriteLine($"Proposed: {plan.Service} {target.FriendlyName} ({plan.EntityId}).");
        Console.Write("Type CONFIRM to send this exact Home Assistant service call: ");

        if (!string.Equals(Console.ReadLine(), "CONFIRM", StringComparison.Ordinal))
        {
            Console.WriteLine("Not sent.");
            continue;
        }

        await homeAssistant.ExecuteAsync(plan, cancellation.Token).ConfigureAwait(false);
        string? state = await homeAssistant.GetStateAsync(plan.EntityId, cancellation.Token).ConfigureAwait(false);
        string expectedState = plan.Service == "turn_on" ? "on" : "off";

        Console.WriteLine(string.Equals(state, expectedState, StringComparison.Ordinal)
            ? $"Confirmed: {target.FriendlyName} is {state}."
            : $"Home Assistant accepted the service call, but post-action state was '{state ?? "missing"}', not '{expectedState}'.");
    }

    return 0;
}
catch (OperationCanceledException)
{
    Console.WriteLine("Cancelled.");
    return 130;
}
catch (HttpRequestException exception)
{
    Console.WriteLine($"Home Assistant request failed: {exception.Message}");
    return 1;
}
catch (JevException exception)
{
    Console.WriteLine($"Jev request failed: {exception.Message}");
    return 1;
}

static Uri ReadHomeAssistantUrl()
{
    while (true)
    {
        Console.Write("Home Assistant URL (for example, http://homeassistant.local:8123): ");
        string? input = Console.ReadLine()?.Trim();

        if (Uri.TryCreate(input, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return new Uri(uri.ToString().TrimEnd('/') + "/", UriKind.Absolute);
        }

        Console.WriteLine("Enter an absolute http:// or https:// URL.");
    }
}

static string ReadRequiredSecret(string label)
{
    while (true)
    {
        Console.Write($"{label}: ");
        string? value = Console.ReadLine()?.Trim();

        if (!string.IsNullOrEmpty(value))
        {
            return value;
        }

        Console.WriteLine("A value is required.");
    }
}

static Dictionary<string, Question> BuildQuestions(IReadOnlyDictionary<string, HomeAssistantEntity> entities) =>
    new(StringComparer.Ordinal)
    {
        ["is_supported"] = Question.Noul(
            "Is the user asking for exactly one listed light, switch, or fan to be turned on or turned off? "
            + "Answer no for questions, multiple devices, areas, groups, scenes, numeric settings, or any unsupported action."),
        ["entity"] = Question.Choice(
            "Which listed entity is the single target of the user's command?",
            entities.ToDictionary(
                pair => pair.Key,
                pair => (string?)$"{pair.Value.FriendlyName}; current state: {pair.Value.State}",
                StringComparer.Ordinal)),
        ["action"] = Question.Choice(
            "Which supported action does the user request?",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["turn_on"] = "Turn the selected entity on.",
                ["turn_off"] = "Turn the selected entity off.",
            }),
    };

static string BuildJevState(string command, IEnumerable<HomeAssistantEntity> entities) =>
    JsonSerializer.Serialize(new
    {
        user_command = command,
        available_entities = entities.Select(entity => new
        {
            entity_id = entity.EntityId,
            friendly_name = entity.FriendlyName,
            state = entity.State,
        }),
    });
