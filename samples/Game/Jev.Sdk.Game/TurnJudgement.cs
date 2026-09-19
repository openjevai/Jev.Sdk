// TurnJudgement.cs
// Part of Jev.Sdk.Game. The model's judgement for one turn, and how it is read off the response.
//
// The design (../../Game.md) asks five questions in one call and keeps the game deterministic
// afterwards. This type is the boundary between those halves: the model answers, the game reads the
// answers into named fields, and nothing downstream ever touches a raw Answer again.
//
// Reading is defensive on purpose. Every accessor falls back to a neutral value when an answer is
// missing or of the wrong kind, so a malformed reply produces a dull turn rather than an exception
// in the middle of a game. The fallbacks are named constants so the tests can assert them, and so
// the console can tell the player when the model gave nothing usable instead of pretending.

using Jev.Sdk;

namespace Jev.Sdk.Game;

/// <summary>
/// One turn's worth of model judgement, read from a single <c>system_one</c> response.
/// </summary>
public sealed class TurnJudgement
{
    /// <summary>Used when the model did not return a usable probability.</summary>
    public const double DefaultProbability = 0.5;

    /// <summary>Used when the model did not return a usable noise level.</summary>
    public const string DefaultNoise = "quiet";

    /// <summary>Used when the model did not return a usable intent.</summary>
    public const string DefaultIntent = ActionIntent.Other;

    /// <summary>The action the model thinks the player attempted.</summary>
    public required string Intent { get; init; }

    /// <summary>The model's confidence in that classification, or null when it gave none.</summary>
    public required double? IntentConfidence { get; init; }

    /// <summary>How plausible the action is given the current state.</summary>
    public required double Plausible { get; init; }

    /// <summary>How likely the action is to wake the guard.</summary>
    public required double WakesGuard { get; init; }

    /// <summary>How noisy the action is, as one of <see cref="NoiseLevels"/>.</summary>
    public required string Noise { get; init; }

    /// <summary>How much the action moves the player toward escaping.</summary>
    public required double Progress { get; init; }

    /// <summary>Whether the response was missing anything the game needed.</summary>
    public required bool HadIncompleteAnswer { get; init; }

    /// <summary>
    /// Reads the judgement out of a response produced by <see cref="QuestionSet"/>.
    /// </summary>
    /// <param name="response">The response to read.</param>
    /// <returns>The judgement, with neutral fallbacks for anything missing.</returns>
    public static TurnJudgement FromResponse(SystemOneResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        IDictionary<string, Answer> answers = response.AnswersOrEmpty;

        bool complete = true;

        string intent = ActionIntent.Other;
        double? confidence = null;

        if (answers.TryGetValue(QuestionSet.IntentKey, out Answer? intentAnswer) && intentAnswer is ChoiceAnswer choice)
        {
            intent = choice.Choice;
            confidence = choice.Confidence;

            // A choice that names an option the game does not know about is treated as "other": the
            // game must not act on a string it cannot map, and the caller is told the answer was not
            // fully usable.
            if (!ActionIntent.All.Contains(intent, StringComparer.Ordinal))
            {
                complete = false;
                intent = ActionIntent.Other;
            }
        }
        else
        {
            complete = false;
        }

        double plausible = ReadProbability(answers, QuestionSet.PlausibleKey, ref complete);
        double wakesGuard = ReadProbability(answers, QuestionSet.WakesGuardKey, ref complete);
        double progress = ReadProbability(answers, QuestionSet.ProgressKey, ref complete);

        string noise = DefaultNoise;

        if (answers.TryGetValue(QuestionSet.NoiseKey, out Answer? noiseAnswer) && noiseAnswer is ScoreAnswer score
            && score.Legend.TryGetValue(FormatScore(score.Score), out StructuredValue? label)
            && label.AsString() is { } text
            && NoiseLevels.All.Contains(text, StringComparer.Ordinal))
        {
            noise = text;
        }
        else
        {
            complete = false;
        }

        return new TurnJudgement
        {
            Intent = intent,
            IntentConfidence = confidence,
            Plausible = plausible,
            WakesGuard = wakesGuard,
            Noise = noise,
            Progress = progress,
            HadIncompleteAnswer = !complete,
        };
    }

    /// <summary>
    /// Reads a noul answer as a probability, clamping to the documented range.
    /// </summary>
    /// <param name="answers">The response's answers.</param>
    /// <param name="key">The question key to read.</param>
    /// <param name="complete">Set to false when the answer was missing or unusable.</param>
    /// <returns>The probability, or <see cref="DefaultProbability"/> when none was usable.</returns>
    /// <remarks>
    /// Clamped rather than trusted: the game's decisions compare against thresholds, so a value
    /// outside [0,1] would silently change the rules. A server sending 1.4 is a bug on its side, but
    /// the game should behave predictably anyway.
    /// </remarks>
    private static double ReadProbability(IDictionary<string, Answer> answers, string key, ref bool complete)
    {
        if (answers.TryGetValue(key, out Answer? answer) && answer is NoulAnswer noul)
        {
            return Math.Clamp(noul.Noul, 0.0, 1.0);
        }

        complete = false;

        return DefaultProbability;
    }

    /// <summary>
    /// Formats a score the way the legend is keyed: position as an integer string.
    /// </summary>
    /// <param name="score">The score the model returned.</param>
    /// <returns>The legend key for that position.</returns>
    /// <remarks>
    /// The legend maps level positions ("0", "1", …) to their labels, and a score is a weighted
    /// position that can land between levels. Rounding is the sensible reading: 2.4 is closer to the
    /// third level than the second, and the game only uses the label for display and for matching
    /// <see cref="NoiseLevels"/>.
    /// </remarks>
    private static string FormatScore(double score) =>
        Math.Round(score, MidpointRounding.AwayFromZero)
            .ToString("0", System.Globalization.CultureInfo.InvariantCulture);
}
