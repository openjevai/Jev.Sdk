// Judgement.cs (tests)
// Part of Jev.Sdk.Game.Tests. Builds judgements and responses by hand.
//
// The game's rules are pure functions of (state, judgement), so the tests need no API key and no
// stub client: a judgement is just data. The one place a real response shape matters — reading the
// answers — is tested separately against a real deserialized payload, because a judgement built by
// hand would not catch a mistake in how the response is read.

namespace Jev.Sdk.Game.Tests;

/// <summary>Helpers for constructing judgements in tests.</summary>
internal static class Judge
{
    /// <summary>
    /// A plausible, quiet, useful action: the neutral case tests vary from.
    /// </summary>
    /// <param name="intent">The classified intent.</param>
    /// <param name="plausible">Plausibility.</param>
    /// <param name="wakesGuard">Chance of waking the guard.</param>
    /// <param name="noise">Noise level.</param>
    /// <param name="progress">Progress toward escaping.</param>
    /// <returns>The judgement.</returns>
    public static TurnJudgement Make(
        string intent,
        double plausible = 0.9,
        double wakesGuard = 0.1,
        string noise = NoiseLevels.Quiet,
        double progress = 0.9)
    {
        return new TurnJudgement
        {
            Intent = intent,
            IntentConfidence = 0.9,
            Plausible = plausible,
            WakesGuard = wakesGuard,
            Noise = noise,
            Progress = progress,
            HadIncompleteAnswer = false,
        };
    }
}

/// <summary>
/// A random source with a fixed answer, so a roll can be pinned rather than hoped for.
/// </summary>
/// <remarks>
/// <see cref="Random"/> cannot be seeded to a chosen value portably, and a test that relied on a
/// particular seed would be brittle across runtimes. Overriding <c>NextDouble</c> is the way to make
/// the outcome certain.
/// </remarks>
internal sealed class FixedDice(double value) : Random
{
    private readonly double _value = value;

    /// <inheritdoc />
    public override double NextDouble() => _value;

    /// <inheritdoc />
    public override int Next(int maxValue) => (int)(_value * maxValue);
}
