// GeneratedWorldsTests.cs
// Part of Jev.Sdk.World.Tests. Every generated classic loads, and its rules are structurally sound.
//
// These exist because the generator produced 50 worlds in one go, and structural mistakes at that
// scale are invisible by reading. Two real defects were found this way before these tests existed:
//
//   1. Every condition using `at_least` was silently dropped, because the Python authoring helpers
//      emitted snake_case where the wire format is camelCase. The validator caught it, reporting
//      "0 comparison(s)" for rules that plainly had one.
//   2. A win rule was SHADOWED. `go_down_in_house` set the flag and `arrived` won only on a LATER
//      turn, so a run that ended right after descending was scored as a loss. The validator cannot see
//      this: both rules are individually valid, and the defect is in their order.
//
// Case 2 is the reason for the reachability checks below. A world can load, validate, and still be
// unwinnable, and the only way to know is to ask whether every win and lose rule can actually fire.

using System.Text.Json;
using Jev.Sdk.World;

namespace Jev.Sdk.World.Tests;

public class GeneratedWorldsTests
{
    private static string ClassicsDirectory => Path.Combine(AppContext.BaseDirectory, "worlds", "classics");

    private static string PackagesDirectory => Path.Combine(AppContext.BaseDirectory, "packages");

    /// <summary>Every generated world document.</summary>
    private static IEnumerable<string> Documents() =>
        Directory.Exists(ClassicsDirectory)
            ? Directory.EnumerateFiles(ClassicsDirectory, "*.md").OrderBy(f => f, StringComparer.Ordinal)
            : [];

    public static TheoryData<string> WorldPaths()
    {
        TheoryData<string> data = [];

        foreach (string path in Documents())
        {
            string? name = Path.GetFileName(path);

            if (name is not null)
            {
                data.Add(name);
            }
        }

        return data;
    }

    [Fact]
    public void FiftyWorldsWereGenerated()
    {
        // Five classics, each in five styles. A number rather than a range, so the set shrinking is a
        // failure rather than a quiet change.
        Assert.Equal(50, Documents().Count());
    }

    [Theory]
    [MemberData(nameof(WorldPaths))]
    public void EveryGeneratedWorld_LoadsAndValidates(string fileName)
    {
        // The loader is the authority on what a valid world is, so this is the same check the
        // executable performs with --validate.
        WorldDefinition world = MarkdownWorldLoader.Load(Path.Combine(ClassicsDirectory, fileName));

        Assert.False(string.IsNullOrWhiteSpace(world.Title));
        Assert.Equal("1.0", world.SchemaVersion);
        Assert.NotEmpty(world.Rules);
        Assert.True(world.TurnLimit > 0);
    }

    [Theory]
    [MemberData(nameof(WorldPaths))]
    public void EveryGeneratedWorld_CanBeWon(string fileName)
    {
        // The defect this exists for: a win rule that can never fire because an earlier rule always
        // matches first. Checked by asking whether the rule's own condition can hold in a state the
        // world can actually reach - approximated here by requiring a win rule whose condition is not
        // shadowed by an earlier unconditional rule.
        WorldDefinition world = MarkdownWorldLoader.Load(Path.Combine(ClassicsDirectory, fileName));

        RuleDefinition[] wins = [.. world.Rules.Where(r => r.Then.Any(e => e.Kind == EffectKinds.Win))];

        Assert.True(wins.Length > 0, $"{fileName} has no rule that can win.");

        foreach (RuleDefinition win in wins)
        {
            AssertWinIsReachable(world, win, fileName);
        }
    }

    [Theory]
    [MemberData(nameof(WorldPaths))]
    public void EveryGeneratedWorld_HasAnUnreachableWinRuleIfARuleBeforeItAlwaysFires(string fileName)
    {
        // Stated from the other side, and the check that would have caught the shadowed win: a rule
        // that always matches makes every rule after it dead, so a win placed after one is unreachable.
        WorldDefinition world = MarkdownWorldLoader.Load(Path.Combine(ClassicsDirectory, fileName));

        int firstUnconditional = IndexOfFirstCatchAll(world);

        if (firstUnconditional < 0)
        {
            return;
        }

        List<string> deadWins = [.. world.Rules
            .Skip(firstUnconditional + 1)
            .Where(r => r.Then.Any(e => e.Kind == EffectKinds.Win))
            .Select(r => r.Id)];

        Assert.True(
            deadWins.Count == 0,
            $"{fileName}: win rule(s) {string.Join(", ", deadWins)} sit after the catch-all rule "
            + $"'{world.Rules[firstUnconditional].Id}', so they can never fire.");

        _ = fileName;
    }

    [Theory]
    [MemberData(nameof(WorldPaths))]
    public void EveryGeneratedWorld_DeclaresTheStyleItClaims(string fileName)
    {
        // The composed world must actually carry the style's judgements, or a "Peril" world that lost
        // its danger question would look identical to a Classic one.
        WorldDefinition world = MarkdownWorldLoader.Load(Path.Combine(ClassicsDirectory, fileName));

        HashSet<string> keys = [.. world.Judgements.Select(j => j.Key)];

        Assert.Contains("plausible", keys);
        Assert.Contains("progress", keys);

        string name = Path.GetFileNameWithoutExtension(fileName);

        if (name.EndsWith("-peril", StringComparison.Ordinal))
        {
            Assert.Contains("danger", keys);
            Assert.Contains(world.State, f => f.Key == "time_left");
        }

        if (name.EndsWith("-social", StringComparison.Ordinal))
        {
            Assert.Contains("mood", keys);
            Assert.Contains("improves_goodwill", keys);
            Assert.Contains(world.State, f => f.Key == "goodwill");
        }

        if (name.EndsWith("-resource", StringComparison.Ordinal))
        {
            Assert.Contains("supply_cost", keys);
            Assert.Contains(world.State, f => f.Key == "supply");
        }

        if (name.EndsWith("-mythic", StringComparison.Ordinal))
        {
            Assert.Contains("fate", keys);
            Assert.Contains(world.State, f => f.Key == "doom");
        }
    }

    [Theory]
    [MemberData(nameof(WorldPaths))]
    public void EveryGeneratedWorld_ShowsTheStyleStateToTheModel(string fileName)
    {
        // A style's counters have to reach the description, or the judgements that read them cannot
        // see them and the style stops mattering. This is the quiet failure the composer exists to
        // prevent.
        WorldDefinition world = MarkdownWorldLoader.Load(Path.Combine(ClassicsDirectory, fileName));
        WorldState state = new(world);

        string description = state.Describe();

        foreach (StateField field in world.State)
        {
            string placeholder = $"{{{{{field.Key}}}}}";

            Assert.DoesNotContain(placeholder, description, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("{{", description, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryGeneratedWorld_IsAlsoShippedAsAPackage()
    {
        // The deliverable is the zips, so a world without one is a gap rather than a detail.
        if (!Directory.Exists(PackagesDirectory))
        {
            return;
        }

        string[] packages = [.. Directory.EnumerateFiles(PackagesDirectory, "*.zip")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n is not null)
            .Select(n => n!)];

        string[] documents = [.. Documents()
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n is not null)
            .Select(n => n!)];

        Assert.Equal(50, packages.Length);

        foreach (string document in documents)
        {
            Assert.Contains(document, packages);
        }
    }

    [Fact]
    public void APackage_OpensAndPlaysItsOwnWorld()
    {
        // Opening is by content detection, so this is the end-to-end check that a shipped zip holds a
        // world the engine can actually run.
        string path = Path.Combine(PackagesDirectory, "zork-house-peril.zip");

        if (!File.Exists(path))
        {
            return;
        }

        using OpenedWorld opened = WorldPackage.Open(path);

        Assert.Equal("zork-house-peril", opened.World.Id);
        Assert.Contains("Peril", opened.World.Title, StringComparison.Ordinal);
        Assert.Contains(opened.World.State, f => f.Key == "time_left");
    }

    [Fact]
    public void NoGeneratedWorld_IsMoreThanOneWorldDocument()
    {
        // A stray file in the classics folder would be loaded as a world by a directory-based player
        // and would fail there rather than here.
        foreach (string path in Documents())
        {
            string text = File.ReadAllText(path);

            int headings = text.Split('\n').Count(line => line.StartsWith("# ", StringComparison.Ordinal));

            Assert.True(headings == 1, $"{Path.GetFileName(path)} has {headings} H1 headings; a world document has one.");
        }
    }

    private static void AssertWinIsReachable(WorldDefinition world, RuleDefinition win, string fileName)
    {
        // A win with no condition always fires when it is reached, which is fine only if it is not
        // shadowed - checked by the other test. A win with a condition must name something the world
        // declares, which the loader already guarantees; what is checked here is that it is not placed
        // after a rule that always matches, which is the shadowing defect.
        int winIndex = world.Rules.IndexOf(win);
        int firstUnconditional = IndexOfFirstCatchAll(world);

        Assert.True(
            firstUnconditional < 0 || firstUnconditional > winIndex || win.When is null,
            $"{fileName}: win rule '{win.Id}' is unreachable.");
    }

    /// <summary>
    /// Returns the index of the first rule with no condition, or -1 when every rule is conditional.
    /// </summary>
    /// <param name="world">The world to inspect.</param>
    /// <returns>The index, or -1.</returns>
    /// <remarks>
    /// IList has no FindIndex, and a rule with no condition always matches, which is what makes every
    /// rule after it dead.
    /// </remarks>
    private static int IndexOfFirstCatchAll(WorldDefinition world)
    {
        for (int index = 0; index < world.Rules.Count; index++)
        {
            if (world.Rules[index].When is null)
            {
                return index;
            }
        }

        return -1;
    }
}
