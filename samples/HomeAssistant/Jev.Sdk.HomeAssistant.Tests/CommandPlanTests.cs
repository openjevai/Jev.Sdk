using Jev.Sdk.HomeAssistant;

namespace Jev.Sdk.HomeAssistant.Tests;

public sealed class CommandPlanTests
{
    [Theory]
    [InlineData("light.kitchen", "turn_on", "light", "turn_on")]
    [InlineData("switch.coffee_machine", "turn_off", "switch", "turn_off")]
    [InlineData("fan.office", "turn_on", "fan", "turn_on")]
    public void TryCreate_accepts_supported_entity_and_action(
        string entityId,
        string action,
        string expectedDomain,
        string expectedService)
    {
        IReadOnlyDictionary<string, HomeAssistantEntity> entities = new Dictionary<string, HomeAssistantEntity>
        {
            [entityId] = new(entityId, "off", "Test entity"),
        };

        bool created = HomeAssistantCommandPlan.TryCreate(entities, entityId, action, out HomeAssistantCommandPlan? plan);

        Assert.True(created);
        Assert.NotNull(plan);
        Assert.Equal(expectedDomain, plan.Domain);
        Assert.Equal(expectedService, plan.Service);
        Assert.Equal(entityId, plan.EntityId);
    }

    [Theory]
    [InlineData("light.kitchen", "set_temperature")]
    [InlineData("climate.living_room", "turn_on")]
    [InlineData("light.missing", "turn_off")]
    public void TryCreate_rejects_unavailable_command_shapes(string entityId, string action)
    {
        IReadOnlyDictionary<string, HomeAssistantEntity> entities = new Dictionary<string, HomeAssistantEntity>
        {
            ["light.kitchen"] = new("light.kitchen", "off", "Kitchen"),
            ["climate.living_room"] = new("climate.living_room", "heat", "Living room"),
        };

        bool created = HomeAssistantCommandPlan.TryCreate(entities, entityId, action, out HomeAssistantCommandPlan? plan);

        Assert.False(created);
        Assert.Null(plan);
    }
}
