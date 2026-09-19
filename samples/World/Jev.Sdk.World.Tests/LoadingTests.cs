// LoadingTests.cs
// Part of Jev.Sdk.World.Tests. Loading, validating, and the mistakes a validator has to catch.
//
// The engine is driven entirely by data, so a loader that accepts a broken world produces a game that
// runs and behaves stupidly rather than one that fails. These tests are therefore mostly about what
// must be REJECTED, and they use the real world documents as their fixtures so a change to one is
// caught here rather than during play.

using System.Text.Json;

namespace Jev.Sdk.World.Tests;

public class LoadingTests
{
    private static string WorldsDirectory => Path.Combine(AppContext.BaseDirectory, "worlds");

    private static string World(string name) => Path.Combine(WorldsDirectory, name);

    [Fact]
    public void TheShippedMarkdownWorld_Loads()
    {
        WorldDefinition world = MarkdownWorldLoader.Load(World("escape-the-room.md"));

        Assert.Equal("escape-the-room", world.Id);
        Assert.Equal("Escape the Room", world.Title);
        Assert.Equal(4, world.State.Count);
        Assert.Equal(5, world.Judgements.Count);
        Assert.Equal(16, world.Rules.Count);
        Assert.Equal(25, world.TurnLimit);
    }

    [Fact]
    public void TheShippedJsonWorld_Loads()
    {
        WorldDefinition world = WorldLoader.Load(World("the-lighthouse.json"));

        Assert.Equal("the-lighthouse", world.Id);
        Assert.Equal(8, world.State.Count);
        Assert.Equal(5, world.Judgements.Count);
    }

    [Fact]
    public void TheMarkdownAndJsonCopiesOfOneWorld_Agree()
    {
        // Both forms of Escape the Room ship, so the pair is a check on the markdown loader: if it
        // drifts, one of these two numbers moves and this fails.
        WorldDefinition markdown = MarkdownWorldLoader.Load(World("escape-the-room.md"));
        WorldDefinition json = WorldLoader.Load(World("escape-the-room.json"));

        Assert.Equal(json.Id, markdown.Id);
        Assert.Equal(json.Title, markdown.Title);
        Assert.Equal(json.State.Count, markdown.State.Count);
        Assert.Equal(json.Judgements.Count, markdown.Judgements.Count);
        Assert.Equal(json.Rules.Count, markdown.Rules.Count);
        Assert.Equal(json.TurnLimit, markdown.TurnLimit);
        Assert.Equal(json.Goal, markdown.Goal);
    }

    [Fact]
    public void TheMarkdownLoader_ReadsFrontMatterHeadingsAndFencedBlocks()
    {
        string document = """
            ---
            id: test-world
            title: Test World
            turnLimit: 5
            ---

            # Test World

            A goal sentence.

            ## Examples

            - do a thing
            - "do another thing"

            ## State

            ```json
            [ { "key": "flag", "type": "bool", "initial": false, "describe": { "true": "It is set.", "false": "It is not set." } } ]
            ```

            ## State template

            ```text
            A room. {{flag}}
            ```

            ## Judgements

            ```json
            [ { "key": "do", "kind": "noul", "fallback": "0.5", "question": "The player typed \"{{action}}\". Would it work?" } ]
            ```

            ## Rules

            ```json
            [ { "id": "always", "narrate": [ "Nothing." ], "then": [ { "kind": "narrate", "text": "x" } ] } ]
            ```
            """;

        WorldDefinition world = MarkdownWorldLoader.Parse(document);

        Assert.Equal("test-world", world.Id);
        Assert.Equal("Test World", world.Title);
        Assert.Equal("A goal sentence.", world.Goal);
        Assert.Equal(5, world.TurnLimit);
        Assert.Equal(2, world.Examples.Count);
        Assert.Equal(["do a thing", "do another thing"], world.Examples);
        Assert.Single(world.State);
        Assert.Equal("A room. {{flag}}", world.StateTemplate);
    }

    [Fact]
    public void TheHeadingSuppliesTheTitle_WhenFrontMatterOmitsIt()
    {
        string document = """
            ---
            id: no-title
            ---

            # The Heading Wins

            Goal here.

            ## State

            ```json
            [ { "key": "flag", "type": "bool", "initial": false, "describe": { "true": "y", "false": "n" } } ]
            ```

            ## State template

            ```text
            {{flag}}
            ```

            ## Judgements

            ```json
            [ { "key": "do", "kind": "noul", "fallback": "0.5", "question": "{{action}}?" } ]
            ```

            ## Rules

            ```json
            [ { "id": "r", "narrate": [ "x" ], "then": [ { "kind": "narrate", "text": "x" } ] } ]
            ```
            """;

        Assert.Equal("The Heading Wins", MarkdownWorldLoader.Parse(document).Title);
    }

    [Fact]
    public void AMarkdownWorld_MissingASection_IsRejectedWithTheSectionNamed()
    {
        string document = """
            ---
            id: broken
            title: Broken
            ---

            ## State

            ```json
            [ { "key": "flag", "type": "bool", "initial": false, "describe": { "true": "y", "false": "n" } } ]
            ```
            """;

        WorldLoadException exception = Assert.Throws<WorldLoadException>(() => MarkdownWorldLoader.Parse(document));

        Assert.Contains("Rules", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Judgements", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AWorldWithoutACatchAllRule_Loads_AndSaysSoWhenNothingMatches()
    {
        // No catch-all is legal, and the engine reports it rather than inventing an outcome.
        string json = """
            {
              "schemaVersion": "1.0",
              "id": "no-catch-all",
              "title": "No Catch All",
              "stateTemplate": "{{flag}}",
              "state": [ { "key": "flag", "type": "bool", "initial": false, "describe": { "true": "y", "false": "n" } } ],
              "judgements": [ { "key": "do", "kind": "noul", "fallback": "0.5", "question": "{{action}}?" } ],
              "rules": [ { "id": "never", "when": { "state": "flag", "equals": "true" }, "narrate": [ "x" ], "then": [ { "kind": "narrate", "text": "x" } ] } ]
            }
            """;

        WorldDefinition world = WorldLoader.Parse(json);
        WorldRunner runner = new(world);
        RuleEngine engine = new(world);

        TurnResult result = engine.Apply(runner.State, new JudgementSet());

        Assert.Contains("no rule matched", result.RuleId, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "schemaVersion": "9.9", "id": "x", "title": "x", "stateTemplate": "t", "state": [], "judgements": [], "rules": [] }""", "schemaVersion")]
    [InlineData("""{ "schemaVersion": "1.0", "id": "", "title": "x", "stateTemplate": "t", "state": [], "judgements": [], "rules": [] }""", "id")]
    [InlineData("""{ "schemaVersion": "1.0", "id": "x", "title": "", "stateTemplate": "t", "state": [], "judgements": [], "rules": [] }""", "title")]
    [InlineData("""{ "schemaVersion": "1.0", "id": "x", "title": "x", "stateTemplate": "", "state": [ { "key": "flag", "type": "bool", "initial": false, "describe": { "true": "y", "false": "n" } } ], "judgements": [ { "key": "do", "kind": "noul", "fallback": "0.5", "question": "{{action}}?" } ], "rules": [ { "id": "r", "narrate": [ "x" ], "then": [ { "kind": "narrate", "text": "x" } ] } ] }""", "stateTemplate")]
    public void AMalformedWorld_IsRejectedNamingTheProblem(string json, string expectedInMessage)
    {
        WorldLoadException exception = Assert.Throws<WorldLoadException>(() => WorldLoader.Parse(json));

        Assert.Contains(expectedInMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ARuleReferencingAnUndeclaredStateField_IsRejected()
    {
        // The highest-value check in the validator: this mistake produces a rule that silently never
        // fires, which is indistinguishable from a design choice when playing.
        string json = """
            {
              "schemaVersion": "1.0",
              "id": "typo", "title": "Typo", "stateTemplate": "{{flag}}",
              "state": [ { "key": "flag", "type": "bool", "initial": false, "describe": { "true": "y", "false": "n" } } ],
              "judgements": [ { "key": "do", "kind": "noul", "fallback": "0.5", "question": "{{action}}?" } ],
              "rules": [ { "id": "r", "when": { "state": "flagg", "equals": "true" }, "narrate": [ "x" ], "then": [ { "kind": "set", "state": "flagg", "to": "true" } ] } ]
            }
            """;

        WorldLoadException exception = Assert.Throws<WorldLoadException>(() => WorldLoader.Parse(json));

        Assert.Contains("flagg", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARuleReferencingAnUndeclaredJudgement_IsRejected()
    {
        string json = """
            {
              "schemaVersion": "1.0",
              "id": "typo", "title": "Typo", "stateTemplate": "{{flag}}",
              "state": [ { "key": "flag", "type": "bool", "initial": false, "describe": { "true": "y", "false": "n" } } ],
              "judgements": [ { "key": "do", "kind": "noul", "fallback": "0.5", "question": "{{action}}?" } ],
              "rules": [ { "id": "r", "when": { "judgement": "duz", "atLeast": 0.5 }, "narrate": [ "x" ], "then": [ { "kind": "narrate", "text": "x" } ] } ]
            }
            """;

        WorldLoadException exception = Assert.Throws<WorldLoadException>(() => WorldLoader.Parse(json));

        Assert.Contains("duz", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AStateTemplateNamingAnUndeclaredField_IsRejected()
    {
        // Otherwise the model is sent a literal "{{has_keys}}" and judges the situation from nonsense.
        string json = """
            {
              "schemaVersion": "1.0",
              "id": "typo", "title": "Typo", "stateTemplate": "A room. {{has_keys}}",
              "state": [ { "key": "has_key", "type": "bool", "initial": false, "describe": { "true": "y", "false": "n" } } ],
              "judgements": [ { "key": "do", "kind": "noul", "fallback": "0.5", "question": "{{action}}?" } ],
              "rules": [ { "id": "r", "narrate": [ "x" ], "then": [ { "kind": "narrate", "text": "x" } ] } ]
            }
            """;

        WorldLoadException exception = Assert.Throws<WorldLoadException>(() => WorldLoader.Parse(json));

        Assert.Contains("has_keys", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AQuestionThatDoesNotQuoteThePlayer_IsRejected()
    {
        // A question that never mentions {{action}} is judging nothing, which is a quiet way to build
        // a game that ignores its player.
        string json = """
            {
              "schemaVersion": "1.0",
              "id": "x", "title": "x", "stateTemplate": "{{flag}}",
              "state": [ { "key": "flag", "type": "bool", "initial": false, "describe": { "true": "y", "false": "n" } } ],
              "judgements": [ { "key": "do", "kind": "noul", "fallback": "0.5", "question": "Would that work?" } ],
              "rules": [ { "id": "r", "narrate": [ "x" ], "then": [ { "kind": "narrate", "text": "x" } ] } ]
            }
            """;

        WorldLoadException exception = Assert.Throws<WorldLoadException>(() => WorldLoader.Parse(json));

        Assert.Contains("{{action}}", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AChoiceFallbackThatMatchesNoOption_IsRejected()
    {
        string json = """
            {
              "schemaVersion": "1.0",
              "id": "x", "title": "x", "stateTemplate": "{{flag}}",
              "state": [ { "key": "flag", "type": "bool", "initial": false, "describe": { "true": "y", "false": "n" } } ],
              "judgements": [ { "key": "do", "fallback": "nonsense", "kind": "choice",
                "question": "{{action}}?",
                "options": [ { "key": "a", "description": "first" }, { "key": "b", "description": "second" } ] } ],
              "rules": [ { "id": "r", "narrate": [ "x" ], "then": [ { "kind": "narrate", "text": "x" } ] } ]
            }
            """;

        WorldLoadException exception = Assert.Throws<WorldLoadException>(() => WorldLoader.Parse(json));

        Assert.Contains("nonsense", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABoolStateField_WithoutBothDescriptions_IsRejected()
    {
        // Without both, the state description cannot say what the situation is, which is the one thing
        // the model needs.
        string json = """
            {
              "schemaVersion": "1.0",
              "id": "x", "title": "x", "stateTemplate": "A room.",
              "state": [ { "key": "flag", "type": "bool", "initial": false, "describe": { "true": "y" } } ],
              "judgements": [ { "key": "do", "kind": "noul", "fallback": "0.5", "question": "{{action}}?" } ],
              "rules": [ { "id": "r", "narrate": [ "x" ], "then": [ { "kind": "narrate", "text": "x" } ] } ]
            }
            """;

        WorldLoadException exception = Assert.Throws<WorldLoadException>(() => WorldLoader.Parse(json));

        Assert.Contains("false", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AConditionMixingForms_IsRejected()
    {
        // A condition is one comparison or one combinator. Mixing them would need an implied precedence
        // rule that a world author would have to guess at.
        string json = """
            {
              "schemaVersion": "1.0",
              "id": "x", "title": "x", "stateTemplate": "{{flag}}",
              "state": [ { "key": "flag", "type": "bool", "initial": false, "describe": { "true": "y", "false": "n" } } ],
              "judgements": [ { "key": "do", "kind": "noul", "fallback": "0.5", "question": "{{action}}?" } ],
              "rules": [ { "id": "r",
                "when": { "state": "flag", "equals": "true", "all": [ { "state": "flag", "equals": "true" } ] },
                "narrate": [ "x" ], "then": [ { "kind": "narrate", "text": "x" } ] } ]
            }
            """;

        WorldLoadException exception = Assert.Throws<WorldLoadException>(() => WorldLoader.Parse(json));

        Assert.Contains("mixing", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnUnknownEffectKind_IsRejected()
    {
        string json = """
            {
              "schemaVersion": "1.0",
              "id": "x", "title": "x", "stateTemplate": "{{flag}}",
              "state": [ { "key": "flag", "type": "bool", "initial": false, "describe": { "true": "y", "false": "n" } } ],
              "judgements": [ { "key": "do", "kind": "noul", "fallback": "0.5", "question": "{{action}}?" } ],
              "rules": [ { "id": "r", "narrate": [ "x" ], "then": [ { "kind": "explode" } ] } ]
            }
            """;

        WorldLoadException exception = Assert.Throws<WorldLoadException>(() => WorldLoader.Parse(json));

        Assert.Contains("explode", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CommentsAndTrailingCommas_AreAccepted()
    {
        // A world is written by a person, and being able to explain yourself in place is the point.
        string json = """
            {
              // the version
              "schemaVersion": "1.0",
              "id": "x", "title": "x", "stateTemplate": "{{flag}}",
              "state": [ { "key": "flag", "type": "bool", "initial": false, "describe": { "true": "y", "false": "n" } } ],
              "judgements": [ { "key": "do", "kind": "noul", "fallback": "0.5", "question": "{{action}}?" } ],
              "rules": [ { "id": "r", "narrate": [ "x" ], "then": [ { "kind": "narrate", "text": "x" } ] } ],
            }
            """;

        Assert.Equal("x", WorldLoader.Parse(json).Id);
    }

    [Fact]
    public void PlaceholdersIn_TakesTheBareName_TheValidatorComparesAgainst()
    {
        // A regression: the validator once compared the bare name against the braced constant, so every
        // world failed to load with a message naming {{action}} as an unknown field.
        string[] found = [.. Placeholders.In("A room. {{flag}} and {{action}}")];

        Assert.Equal(["flag", "action"], found);
        Assert.Contains("{{action}}", Placeholders.Action, StringComparison.Ordinal);
        Assert.Equal("action", Placeholders.ActionName);
    }
}
