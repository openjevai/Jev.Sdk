// EngineTests.cs
// Part of Jev.Sdk.World.Tests. The rules, the state, the condition forms, and package opening.
//
// No API key and no network: the engine is a pure function of (state, judgements, dice), so a world can
// be played end to end here. The model's half is covered by the live prompt tests in
// tests/Jev.Sdk.IntegrationTests/GamePromptTests.cs, which is the only place the wording can be checked.

using System.IO.Compression;
using System.Text.Json;
using Jev.Sdk;

namespace Jev.Sdk.World.Tests;

public class EngineTests
{
    // ------------------------------------------------------------------ state

    [Fact]
    public void ANewState_StartsFromItsSchema()
    {
        WorldDefinition world = WorldLoader.Load(WorldPath("escape-the-room.json"));
        WorldState state = new(world);

        Assert.False(state.GetBool("has_key"));
        Assert.False(state.GetBool("guard_awake"));
        Assert.Equal(0, state.Turns);
        Assert.False(state.IsOver);
    }

    [Fact]
    public void TheStateDescription_IsProseFromTheSchema()
    {
        // The one thing the model is given about the situation, so it must read as a place rather than
        // as a row of booleans.
        WorldDefinition world = WorldLoader.Load(WorldPath("escape-the-room.json"));
        WorldState state = new(world);

        string description = state.Describe();

        Assert.Contains("The key is on a table across the room.", description, StringComparison.Ordinal);
        Assert.Contains("The door is locked.", description, StringComparison.Ordinal);
        Assert.Contains("The guard is asleep in the corner.", description, StringComparison.Ordinal);

        state.Set("has_key", "true");
        state.Set("guard_awake", "true");

        Assert.Contains("The key is in your pocket.", state.Describe(), StringComparison.Ordinal);
        Assert.Contains("The guard is awake and watching you.", state.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void AnIntegerField_FallsBackToItsWildcardDescription()
    {
        // A counter reaching a value the author did not enumerate must still read as a sentence.
        WorldDefinition world = WorldLoader.Load(WorldPath("the-lighthouse.json"));
        WorldState state = new(world);

        state.Set("spare_parts", "0");

        Assert.Contains("out of spare parts", state.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void SettingAValueOfTheWrongType_IsRejected()
    {
        WorldDefinition world = WorldLoader.Load(WorldPath("escape-the-room.json"));
        WorldState state = new(world);

        Assert.Throws<WorldLoadException>(() => state.Set("has_key", "perhaps"));
    }

    [Fact]
    public void IncrementingABool_IsRejected()
    {
        WorldDefinition world = WorldLoader.Load(WorldPath("escape-the-room.json"));
        WorldState state = new(world);

        Assert.Throws<WorldLoadException>(() => state.Increment("has_key", 1));
    }

    // ------------------------------------------------------------------ rules

    [Fact]
    public void TakingTheKey_HoldsIt()
    {
        WorldDefinition world = WorldLoader.Load(WorldPath("escape-the-room.json"));
        WorldState state = new(world);
        RuleEngine engine = new(world, new FixedDice(0.0));

        TurnResult result = engine.Apply(state, Judge(world, ("intent", "take_key")));

        Assert.True(state.GetBool("has_key"));
        Assert.Equal("take_key", result.RuleId);
    }

    [Fact]
    public void AnImplausibleAction_ChangesNothing_HoweverConfidentTheIntent()
    {
        // The design's ordering, and the reason it matters: an impossible action cannot change the room
        // no matter how sure the classification was.
        WorldDefinition world = WorldLoader.Load(WorldPath("escape-the-room.json"));
        WorldState state = new(world);
        RuleEngine engine = new(world, new FixedDice(0.0));

        TurnResult result = engine.Apply(state, Judge(world, ("intent", "take_key"), ("plausible", "0.1")));

        Assert.False(state.GetBool("has_key"));
        Assert.Equal("implausible", result.RuleId);
    }

    [Fact]
    public void UnlockingWithoutTheKey_DoesNotUnlock()
    {
        WorldDefinition world = WorldLoader.Load(WorldPath("escape-the-room.json"));
        WorldState state = new(world);
        RuleEngine engine = new(world, new FixedDice(0.0));

        TurnResult result = engine.Apply(state, Judge(world, ("intent", "unlock_door")));

        Assert.False(state.GetBool("door_unlocked"));
        Assert.Equal("unlock_without_key", result.RuleId);
    }

    [Fact]
    public void TheWholeEscape_WorksEndToEnd()
    {
        // The design the world encodes, played through the engine with no network at all.
        WorldDefinition world = WorldLoader.Load(WorldPath("escape-the-room.json"));
        WorldState state = new(world);
        RuleEngine engine = new(world, new FixedDice(0.0));

        engine.Apply(state, Judge(world, ("intent", "take_key")));
        Assert.True(state.GetBool("has_key"));

        engine.Apply(state, Judge(world, ("intent", "unlock_door")));
        Assert.True(state.GetBool("door_unlocked"));

        // The door being unlocked does not by itself win: this world requires stepping through, which
        // is the next rule. What changed is that the win no longer depends on a LATER turn noticing the
        // state - an ending rule is evaluated on the same turn it becomes true.
        engine.Apply(state, Judge(world, ("intent", "sneak_past_guard")));

        Assert.True(state.Won);
        Assert.Equal(3, state.Turns);
    }

    [Fact]
    public void ACreativeAction_CanDistractAnAwakeGuard()
    {
        // The design's reason for the progress judgement, and the only place a probability does work no
        // rule could: text matching no named action.
        WorldDefinition world = WorldLoader.Load(WorldPath("escape-the-room.json"));
        WorldState state = new(world) ;
        state.Set("guard_awake", "true");

        RuleEngine engine = new(world, new FixedDice(0.1));

        TurnResult result = engine.Apply(state, Judge(world, ("intent", "other"), ("progress", "0.9")));

        Assert.False(state.GetBool("guard_awake"));
        Assert.Equal("creative_success", result.RuleId);
    }

    [Fact]
    public void ARollBelowItsFloor_DoesNotEvenRoll()
    {
        // Without the floor a hopeless attempt would succeed on a lucky roll, which reads as the world
        // ignoring its own judgement.
        WorldDefinition world = WorldLoader.Load(WorldPath("escape-the-room.json"));
        WorldState state = new(world);
        state.Set("guard_awake", "true");
        state.Set("door_unlocked", "true");

        // A dice that would pass every roll, and a judgement below the rule's floor.
        RuleEngine engine = new(world, new FixedDice(0.0));

        TurnResult result = engine.Apply(state, Judge(world, ("intent", "sneak_past_guard"), ("progress", "0.1")));

        Assert.False(state.Won);
        Assert.Equal("sneak_awake_guard_failure", result.RuleId);
    }

    [Fact]
    public void ARollAboveItsFloor_SucceedsOnALowRollAndFailsOnAHighOne()
    {
        WorldDefinition world = WorldLoader.Load(WorldPath("escape-the-room.json"));

        Assert.True(RunSneak(world, dice: 0.1).Won);
        Assert.False(RunSneak(world, dice: 0.95).Won);

        static WorldState RunSneak(WorldDefinition world, double dice)
        {
            WorldState state = new(world);
            state.Set("guard_awake", "true");
            state.Set("door_unlocked", "true");

            new RuleEngine(world, new FixedDice(dice))
                .Apply(state, Judge(world, ("intent", "sneak_past_guard"), ("progress", "0.9")));

            return state;
        }
    }

    [Fact]
    public void AWin_FiresBeforeTheRulesAfterIt()
    {
        // First-match-wins means the narrate effects of later rules never run.
        WorldDefinition world = WorldLoader.Load(WorldPath("escape-the-room.json"));
        WorldState state = new(world);
        state.Set("has_key", "true");
        state.Set("door_unlocked", "true");

        RuleEngine engine = new(world, new FixedDice(0.0));

        TurnResult result = engine.Apply(state, Judge(world, ("intent", "unlock_door")));

        Assert.True(state.Won);
        Assert.Equal("open_unlocked_door", result.RuleId);
        Assert.Contains(result.Narration, line => line.Contains("You are out.", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ conditions

    [Fact]
    public void EveryConditionForm_Evaluates()
    {
        WorldDefinition world = WorldLoader.Load(WorldPath("escape-the-room.json"));
        WorldState state = new(world);
        RuleEngine engine = new(world, new FixedDice(0.5));

        JudgementSet judgements = JudgeWithScore(world, [("intent", "take_key"), ("plausible", "0.8")], "noise", "quiet", 1.0);

        // A null condition is the documented catch-all.
        Assert.True(engine.Holds(null, state, judgements));

        Assert.True(engine.Holds(new ConditionDefinition { Judgement = "plausible", AtLeast = 0.5 }, state, judgements));
        Assert.False(engine.Holds(new ConditionDefinition { Judgement = "plausible", Below = 0.5 }, state, judgements));
        Assert.True(engine.Holds(new ConditionDefinition { Judgement = "intent", Matches = "take_key" }, state, judgements));
        Assert.True(engine.Holds(new ConditionDefinition { Judgement = "intent", IsOneOf = ["take_key", "unlock_door"] }, state, judgements));
        Assert.True(engine.Holds(new ConditionDefinition { State = "has_key", Matches = "false" }, state, judgements));

        Assert.True(engine.Holds(new ConditionDefinition
        {
            All = [new ConditionDefinition { Judgement = "intent", Matches = "take_key" }, new ConditionDefinition { State = "has_key", Matches = "false" }],
        }, state, judgements));

        Assert.True(engine.Holds(new ConditionDefinition
        {
            Any = [new ConditionDefinition { Judgement = "intent", Matches = "nope" }, new ConditionDefinition { Judgement = "intent", Matches = "take_key" }],
        }, state, judgements));

        Assert.True(engine.Holds(new ConditionDefinition
        {
            Not = new ConditionDefinition { Judgement = "intent", Matches = "nope" },
        }, state, judgements));
    }

    [Fact]
    public void AScoreJudgement_IsComparedByItsLevelLabel()
    {
        // A score comes back as a number, but a rule almost always wants the level. The engine rounds
        // to the nearest declared level so a condition can name one.
        WorldDefinition world = EscapeWorld();

        JudgementSet quiet = JudgeWithScore(world, [], "noise", "quiet", 1.0);
        JudgementSet loud = JudgeWithScore(world, [], "noise", "extremely loud", 2.9);

        Assert.Equal("quiet", quiet["noise"].AsText());
        Assert.Equal("extremely loud", loud["noise"].AsText());
    }

    // ------------------------------------------------------------------ reading answers

    [Fact]
    public void ACompleteResponse_IsReadInFull()
    {
        WorldDefinition world = WorldLoader.Load(WorldPath("escape-the-room.json"));

        JudgementSet judgements = Read(world, """
            {
              "model": "test",
              "answers": {
                "intent": { "type": "choice", "choice": "take_key", "confidence": 0.92 },
                "plausible": { "type": "noul", "noul": 0.95 },
                "wakes_guard": { "type": "noul", "noul": 0.2 },
                "noise": { "type": "score", "score": 1.0, "legend": { "1": "quiet" } },
                "progress": { "type": "noul", "noul": 0.8 }
              },
              "usage": { "input_tokens": 1, "output_tokens": 1 }
            }
            """);

        Assert.Equal("take_key", judgements["intent"].Text);
        Assert.Equal(0.95, judgements["plausible"].Number);
        Assert.Equal("quiet", judgements["noise"].Text);
        Assert.False(judgements.AnyUnusable);
    }

    [Fact]
    public void AMissingAnswer_UsesTheWorldsDeclaredFallback_AndIsReportedUnusable()
    {
        // A bad reply must produce a dull turn, not an exception mid-run, and the caller must be told
        // the value was substituted rather than the model's.
        WorldDefinition world = WorldLoader.Load(WorldPath("escape-the-room.json"));

        JudgementSet judgements = Read(world, """{ "model": "test", "answers": {}, "usage": { "input_tokens": 1, "output_tokens": 1 } }""");

        Assert.Equal("other", judgements["intent"].Text);
        Assert.Equal(0.5, judgements["plausible"].Number);
        Assert.Equal("quiet", judgements["noise"].Text);
        Assert.True(judgements.AnyUnusable);
        Assert.Equal(5, judgements.UnusableKeys.Count);
    }

    [Fact]
    public void AnInventedChoiceOption_IsUnusableRatherThanPassedThrough()
    {
        // A rule comparing an option the schema never declared would silently match nothing.
        WorldDefinition world = WorldLoader.Load(WorldPath("escape-the-room.json"));

        JudgementSet judgements = Read(world, """
            {
              "model": "test",
              "answers": { "intent": { "type": "choice", "choice": "teleport_away", "confidence": 0.9 } },
              "usage": { "input_tokens": 1, "output_tokens": 1 }
            }
            """);

        Assert.Equal("other", judgements["intent"].Text);
        Assert.Contains("intent", judgements.UnusableKeys);
    }

    [Fact]
    public void AScoreOutsideTheDeclaredLevels_IsUnusable()
    {
        WorldDefinition world = WorldLoader.Load(WorldPath("escape-the-room.json"));

        JudgementSet judgements = Read(world, """
            {
              "model": "test",
              "answers": { "noise": { "type": "score", "score": 9.0, "legend": { "9": "deafening" } } },
              "usage": { "input_tokens": 1, "output_tokens": 1 }
            }
            """);

        Assert.Equal("quiet", judgements["noise"].Text);
        Assert.Contains("noise", judgements.UnusableKeys);
    }

    [Fact]
    public void OutOfRangeProbabilities_AreClamped()
    {
        WorldDefinition world = WorldLoader.Load(WorldPath("escape-the-room.json"));

        JudgementSet judgements = Read(world, """
            {
              "model": "test",
              "answers": { "plausible": { "type": "noul", "noul": 1.7 } },
              "usage": { "input_tokens": 1, "output_tokens": 1 }
            }
            """);

        Assert.Equal(1.0, judgements["plausible"].Number);
    }

    [Fact]
    public void TheQuestions_AreBuiltFromTheSchema()
    {
        // The engine's whole contact with the model: one question per declared judgement, of the
        // declared kind, with the player's words substituted in.
        WorldDefinition world = WorldLoader.Load(WorldPath("escape-the-room.json"));

        Dictionary<string, Question> questions = JudgementSet.BuildQuestions(world, "grab the key", "A room.");

        Assert.Equal(5, questions.Count);
        Assert.IsType<ChoiceQuestion>(questions["intent"]);
        Assert.IsType<NoulQuestion>(questions["plausible"]);
        Assert.IsType<ScoreQuestion>(questions["noise"]);

        ChoiceQuestion intent = (ChoiceQuestion)questions["intent"];
        Assert.Equal(7, intent.Criteria.Count);
        Assert.Contains("grab the key", intent.Instructions!.AsString()!, StringComparison.Ordinal);

        ScoreQuestion noise = (ScoreQuestion)questions["noise"];
        Assert.Equal(4, noise.Criteria.Count);
    }

    // ------------------------------------------------------------------ packages

    [Fact]
    public void AZipPackage_IsDetectedUnpackedAndPlayed()
    {
        string zip = Path.Combine(Path.GetTempPath(), $"world-{Guid.NewGuid():N}.zip");
        string staging = Path.Combine(Path.GetTempPath(), $"stage-{Guid.NewGuid():N}");

        Directory.CreateDirectory(staging);
        File.Copy(WorldPath("escape-the-room.md"), Path.Combine(staging, "world.md"));
        File.WriteAllText(Path.Combine(staging, "README.md"), "# not a world");

        try
        {
            ZipFile.CreateFromDirectory(staging, zip);

            using OpenedWorld opened = WorldPackage.Open(zip);

            Assert.Equal("escape-the-room", opened.World.Id);
            Assert.NotNull(opened.WorkingDirectory);
            Assert.True(Directory.Exists(opened.WorkingDirectory));
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
            File.Delete(zip);
        }
    }

    [Fact]
    public void OpeningAPackage_LeavesNoTemporaryFolderBehind()
    {
        string zip = Path.Combine(Path.GetTempPath(), $"world-{Guid.NewGuid():N}.zip");
        string staging = Path.Combine(Path.GetTempPath(), $"stage-{Guid.NewGuid():N}");

        Directory.CreateDirectory(staging);
        File.Copy(WorldPath("escape-the-room.md"), Path.Combine(staging, "world.md"));
        ZipFile.CreateFromDirectory(staging, zip);

        string? working;

        using (OpenedWorld opened = WorldPackage.Open(zip))
        {
            working = opened.WorkingDirectory;

            Assert.NotNull(working);
            Assert.True(Directory.Exists(working));
        }

        try
        {
            Assert.False(Directory.Exists(working));
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
            File.Delete(zip);
        }
    }

    [Fact]
    public void AFolderPackage_IsOpened_AndAPreferredNameWins()
    {
        // README.md sorts before world.md, so the preferred-name order is what makes this work rather
        // than alphabetical luck.
        string staging = Path.Combine(Path.GetTempPath(), $"folder-{Guid.NewGuid():N}");

        Directory.CreateDirectory(staging);
        File.Copy(WorldPath("escape-the-room.md"), Path.Combine(staging, "world.md"));
        File.WriteAllText(Path.Combine(staging, "aaa-not-a-world.md"), "# just prose");

        try
        {
            using OpenedWorld opened = WorldPackage.Open(staging);

            Assert.Equal("escape-the-room", opened.World.Id);
            Assert.Null(opened.WorkingDirectory);
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }

    [Fact]
    public void MarkdownEmbeddedInAJsonString_IsDetected()
    {
        // The "json property with json content" case: a generator hands back a wrapper with the
        // document inside it.
        string markdown = File.ReadAllText(WorldPath("escape-the-room.md"));
        string wrapped = JsonSerializer.Serialize(new { title = "generated", world = markdown });

        WorldDefinition world = WorldPackage.LoadFromJsonText(wrapped, "wrapped.json");

        Assert.Equal("escape-the-room", world.Id);
        Assert.Equal(16, world.Rules.Count);
    }

    [Fact]
    public void AJsonArrayOfCandidateWorlds_UsesTheFirstThatLoads()
    {
        string definition = File.ReadAllText(WorldPath("escape-the-room.json"));
        string array = $"[ {{ \"not\": \"a world\" }}, {definition} ]";

        WorldDefinition world = WorldPackage.LoadFromJsonText(array, "list.json");

        Assert.Equal("escape-the-room", world.Id);
    }

    [Fact]
    public void AJsonArrayWhoseEntriesAreAllBroken_ReportsTheLastFailure()
    {
        // A real message beats a generic one, because the author needs to know what was wrong.
        WorldLoadException exception = Assert.Throws<WorldLoadException>(
            () => WorldPackage.LoadFromJsonText("[ {\"a\":1}, {\"b\":2} ]", "list.json"));

        Assert.Contains("no 'rules' and 'state'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void JSONThatIsNotAWorldAtAll_IsRejectedWithSomethingUseful()
    {
        WorldLoadException exception = Assert.Throws<WorldLoadException>(
            () => WorldPackage.LoadFromJsonText("123", "number.json"));

        Assert.Contains("no world", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MalformedJson_IsRejected()
    {
        WorldLoadException exception = Assert.Throws<WorldLoadException>(
            () => WorldPackage.LoadFromJsonText("{ not json", "bad.json"));

        Assert.Contains("not valid JSON", exception.Message, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ helpers

    private static string WorldPath(string name) => Path.Combine(AppContext.BaseDirectory, "worlds", name);

    /// <summary>
    /// Builds a judgement set from key/value pairs.
    /// </summary>
    /// <param name="values">Each pair is a judgement key and the value the model "returned".</param>
    /// <returns>A judgement set.</returns>
    /// <remarks>
    /// The kind is inferred from the value: a numeric string is a probability, and anything else is
    /// treated as a chosen option or a level label. That keeps the rule tests readable without each one
    /// having to know how a judgement is represented, and a three-argument form carries an explicit
    /// score for the cases where the number and the label differ.
    /// </remarks>
    private static JudgementSet Judge(params (string Key, string Value)[] values) =>
        Judge(EscapeWorld(), values);

    /// <summary>
    /// Builds a judgement set for a world, using the world's declared fallbacks for anything not given.
    /// </summary>
    /// <param name="world">The world whose judgements are being answered.</param>
    /// <param name="values">The judgements the model "returned".</param>
    /// <returns>A complete judgement set.</returns>
    /// <remarks>
    /// Unspecified judgements take the world's declared fallback, exactly as production does when the
    /// model omits one. That matters: an earlier version of this helper left them at zero, which made
    /// every rule test fire the implausibility rule and hid what was actually being tested.
    /// </remarks>
    private static JudgementSet Judge(WorldDefinition world, params (string Key, string Value)[] values)
    {
        Dictionary<string, string> given = new(StringComparer.Ordinal);

        foreach ((string key, string value) in values)
        {
            given[key] = value;
        }

        JudgementSet set = new();

        foreach (JudgementDefinition definition in world.Judgements)
        {
            if (given.TryGetValue(definition.Key, out string? value))
            {
                set.Add(definition.Kind == JudgementKinds.Score
                    ? new JudgementResult(definition.Key, definition.Kind, null, value, Usable: true)
                    : Make(definition, value));

                continue;
            }

            // Nothing given for this one: the world's declared fallback, marked unusable.
            set.Add(definition.Kind == JudgementKinds.Noul
                ? new JudgementResult(definition.Key, definition.Kind, 0.5, null, Usable: false)
                : new JudgementResult(definition.Key, definition.Kind, null, definition.Fallback, Usable: false));
        }

        return set;
    }

    private static JudgementResult Make(JudgementDefinition definition, string value) =>
        double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double number)
            ? new JudgementResult(definition.Key, definition.Kind, number, null, Usable: true)
            : new JudgementResult(definition.Key, definition.Kind, null, value, Usable: true);

    private static WorldDefinition EscapeWorld() => WorldLoader.Load(WorldPath("escape-the-room.json"));

    /// <summary>
    /// Builds a judgement set with one score judgement carrying both a number and its level label.
    /// </summary>
    /// <param name="world">The world whose judgements are being answered.</param>
    /// <param name="values">Ordinary key/value pairs.</param>
    /// <param name="scoreKey">The score judgement's key.</param>
    /// <param name="scoreLabel">Its level label.</param>
    /// <param name="score">Its raw number.</param>
    /// <returns>A judgement set.</returns>
    private static JudgementSet JudgeWithScore(
        WorldDefinition world,
        (string Key, string Value)[] values,
        string scoreKey,
        string scoreLabel,
        double score)
    {
        JudgementSet set = Judge(world, values);

        set.Add(new JudgementResult(scoreKey, JudgementKinds.Score, score, scoreLabel, Usable: true));

        return set;
    }

    private static JudgementSet Read(WorldDefinition world, string json)
    {
        SystemOneResponse response = JsonSerializer.Deserialize<SystemOneResponse>(
            json,
            JevJsonContext.Default.Options)!;

        return JudgementSet.FromResponse(world, response);
    }
}

/// <summary>A random source with a fixed answer, so a roll can be pinned rather than hoped for.</summary>
internal sealed class FixedDice(double value) : Random
{
    private readonly double _value = value;

    /// <inheritdoc />
    public override double NextDouble() => _value;

    /// <inheritdoc />
    public override int Next(int maxValue) => (int)(_value * maxValue);
}
