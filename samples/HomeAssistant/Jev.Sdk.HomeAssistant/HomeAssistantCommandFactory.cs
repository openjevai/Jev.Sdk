using System.Collections.Frozen;

namespace Jev.Sdk.HomeAssistant;

/// <summary>A Home Assistant entity available for command selection.</summary>
public sealed record HomeAssistantEntity(string EntityId, string State, string FriendlyName);

/// <summary>A validated Home Assistant service invocation.</summary>
public sealed record HomeAssistantCommandPlan(string Domain, string Service, string EntityId)
{
    private static readonly HomeAssistantCommandFactory s_defaultFactory = HomeAssistantCommandFactory.CreateDefault();

    /// <summary>
    /// Creates a plan only when the chosen entity and action are present in the supported command
    /// catalog. It never invents a domain, service name, or target entity.
    /// </summary>
    public static bool TryCreate(
        IReadOnlyDictionary<string, HomeAssistantEntity> entities,
        string entityId,
        string action,
        out HomeAssistantCommandPlan? plan) =>
        s_defaultFactory.TryCreate(entities, entityId, action, out plan);
}

/// <summary>
/// Maps a small, declarative catalog of user-facing actions to Home Assistant services. This is a
/// sample boundary: adding a capability is a catalog entry, not another dispatch branch.
/// </summary>
public sealed class HomeAssistantCommandFactory
{
    private readonly FrozenDictionary<string, HomeAssistantServiceDefinition> _definitions;

    /// <summary>Creates a factory from its explicitly supplied service catalog.</summary>
    public HomeAssistantCommandFactory(IEnumerable<HomeAssistantServiceDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        _definitions = definitions.ToFrozenDictionary(
            definition => definition.Action,
            StringComparer.Ordinal);
    }

    /// <summary>Domains with at least one safe, implemented action in this sample.</summary>
    public IReadOnlySet<string> SupportedDomains { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "light",
        "switch",
        "fan",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Creates the small built-in catalog used by the command-line sample.</summary>
    public static HomeAssistantCommandFactory CreateDefault() => new(
    [
        new HomeAssistantServiceDefinition(
            "turn_on",
            "turn_on",
            new HashSet<string>(["light", "switch", "fan"], StringComparer.Ordinal)),
        new HomeAssistantServiceDefinition(
            "turn_off",
            "turn_off",
            new HashSet<string>(["light", "switch", "fan"], StringComparer.Ordinal)),
    ]);

    /// <summary>Returns a service plan if the entity is known and its domain permits the action.</summary>
    public bool TryCreate(
        IReadOnlyDictionary<string, HomeAssistantEntity> entities,
        string entityId,
        string action,
        out HomeAssistantCommandPlan? plan)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        plan = null;

        if (!entities.ContainsKey(entityId)
            || !_definitions.TryGetValue(action, out HomeAssistantServiceDefinition? definition))
        {
            return false;
        }

        int separator = entityId.IndexOf('.', StringComparison.Ordinal);

        if (separator <= 0)
        {
            return false;
        }

        string domain = entityId[..separator];

        if (!definition.AllowedDomains.Contains(domain))
        {
            return false;
        }

        plan = new HomeAssistantCommandPlan(domain, definition.Service, entityId);
        return true;
    }
}

/// <summary>One permitted action and the entity domains to which it applies.</summary>
public sealed record HomeAssistantServiceDefinition(
    string Action,
    string Service,
    IReadOnlySet<string> AllowedDomains);
