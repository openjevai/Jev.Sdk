// WorldRunner.cs
// Part of Jev.Sdk.World. One turn, and one whole run, as a function.
//
// The runner deliberately does no I/O: it takes a line of player text and returns what happened. That
// makes a complete run replayable in a test with a stubbed model, which is the only way to cover a
// world's rules end to end without spending real calls.
//
// It is the seam between the engine and any interface. A console, a test, or something else entirely
// drives the same runner.

using Jev.Sdk;

namespace Jev.Sdk.World;

/// <summary>Everything that happened in one turn, for an interface to present.</summary>
/// <param name="Action">What the player typed.</param>
/// <param name="Judgements">The model's answers.</param>
/// <param name="Result">What the rules did.</param>
/// <param name="StateSummary">The state after the turn.</param>
/// <param name="StateDescription">The rendered state that was sent to the model.</param>
/// <param name="Error">Set when the call failed and the turn was skipped.</param>
public sealed record TurnReport(
    string Action,
    JudgementSet Judgements,
    TurnResult Result,
    string StateSummary,
    string StateDescription,
    Exception? Error)
{
    /// <summary>True when the turn produced a rule outcome rather than a call failure.</summary>
    public bool Succeeded => Error is null;
}

/// <summary>
/// Plays a world, one turn at a time.
/// </summary>
public sealed class WorldRunner
{
    private readonly WorldDefinition _world;
    private readonly RuleEngine _engine;

    /// <summary>Creates a runner for a world.</summary>
    /// <param name="world">The validated world.</param>
    /// <param name="dice">Source of randomness for rolls. Injected so a test can pin them.</param>
    public WorldRunner(WorldDefinition world, Random? dice = null)
    {
        ArgumentNullException.ThrowIfNull(world);

        _world = world;

        State = new WorldState(world);
        _engine = new RuleEngine(world, dice);
    }

    /// <summary>The world being played.</summary>
    public WorldDefinition World => _world;

    /// <summary>The live state.</summary>
    public WorldState State { get; }

    /// <summary>True when the run is finished, by escape or by rule.</summary>
    public bool IsFinished => State.IsOver || State.Turns >= _world.TurnLimit;

    /// <summary>
    /// Plays one turn.
    /// </summary>
    /// <param name="client">The client used to ask the model.</param>
    /// <param name="playerAction">What the player typed.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>What happened, including the failure when the call did not succeed.</returns>
    /// <remarks>
    /// A failed call is reported rather than thrown, because a world is a session: one bad turn should
    /// not end the run. The rules are not consulted on a failure, so a skipped turn cannot change the
    /// state — which also means it does not count against the turn limit.
    /// </remarks>
    public async Task<TurnReport> TurnAsync(
        JevClient client,
        string playerAction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        string action = playerAction.Trim();
        string description = State.Describe();

        SystemOneResponse response;

        try
        {
            response = await client.SystemOneAsync(
                description,
                JudgementSet.BuildQuestions(_world, action, description),
                model: null,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new TurnReport(action, new JudgementSet(), new TurnResult([], "(not run)", false), State.Summarise(), description, exception);
        }

        JudgementSet judgements = JudgementSet.FromResponse(_world, response);
        TurnResult result = _engine.Apply(State, judgements);

        return new TurnReport(action, judgements, result, State.Summarise(), description, Error: null);
    }
}
