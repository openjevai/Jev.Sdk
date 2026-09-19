// Judgements.cs
// Part of Jev.Sdk.World. Turning a world's judgement declarations into API questions, and reading the
// answers back generically.
//
// This is the whole of the engine's contact with the model. It builds one question per declared
// judgement from the schema, and reads the response back into a form the rules can compare against
// without knowing what any particular judgement meant.
//
// Reading is tolerant on purpose. A judgement that comes back missing, of the wrong kind, or carrying
// a value the schema never declared is recorded as unusable and falls back to the world's declared
// fallback, so a bad reply produces a dull turn rather than an exception mid-run. The console is told
// which judgements were unusable, so a substituted value is never presented as the model's answer.

using System.Globalization;
using Jev.Sdk;

namespace Jev.Sdk.World;

/// <summary>
/// One judgement's answer, in the three forms a condition can compare it in.
/// </summary>
/// <param name="Key">The judgement's key.</param>
/// <param name="Kind">The judgement's kind.</param>
/// <param name="Number">The value for a noul or score judgement; null when it was unusable.</param>
/// <param name="Text">The chosen option, or the score's level label; null when it was unusable.</param>
/// <param name="Usable">Whether the model returned something the schema recognised.</param>
public sealed record JudgementResult(
    string Key,
    string Kind,
    double? Number,
    string? Text,
    bool Usable)
{
    /// <summary>
    /// A stable string form, for a condition comparing with equals or isOneOf.
    /// </summary>
    /// <returns>The chosen option or level for a choice or score, or the number as text otherwise.</returns>
    public string AsText() => Text ?? Number?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty;
}

/// <summary>
/// Every judgement for one turn, keyed by the schema's keys.
/// </summary>
public sealed class JudgementSet
{
    private readonly Dictionary<string, JudgementResult> _results = new(StringComparer.Ordinal);

    /// <summary>Adds a result.</summary>
    /// <param name="result">The result to add.</param>
    public void Add(JudgementResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        _results[result.Key] = result;
    }

    /// <summary>
    /// Returns a result by key.
    /// </summary>
    /// <param name="key">The judgement's key.</param>
    /// <returns>The result, or an unusable placeholder when the key is unknown.</returns>
    /// <remarks>
    /// Never null and never throws: a rule referring to a judgement the schema declares but the
    /// response omitted still has to evaluate, and the validator guarantees the key exists.
    /// </remarks>
    public JudgementResult this[string key] =>
        _results.TryGetValue(key, out JudgementResult? result)
            ? result
            : new JudgementResult(key, JudgementKinds.Noul, null, null, Usable: false);

    /// <summary>True when any judgement came back unusable.</summary>
    public bool AnyUnusable => _results.Values.Any(r => !r.Usable);

    /// <summary>The keys of the judgements that came back unusable.</summary>
    public IReadOnlyList<string> UnusableKeys =>
        [.. _results.Values.Where(r => !r.Usable).Select(r => r.Key)];

    /// <summary>
    /// Builds the question map for a turn from the world's declarations.
    /// </summary>
    /// <param name="world">The world being played.</param>
    /// <param name="action">What the player typed.</param>
    /// <param name="stateDescription">The rendered state description.</param>
    /// <returns>Questions keyed as the schema names them.</returns>
    /// <remarks>
    /// The wording comes from the schema, unmodified. That matters more than it looks: a question's
    /// phrasing is behaviour, not decoration. Two measured examples of a reworded question producing
    /// an unplayable world are recorded in README.md, which is why the wording lives in data an author
    /// can edit and a test can pin.
    /// </remarks>
    public static Dictionary<string, Question> BuildQuestions(
        WorldDefinition world,
        string action,
        string stateDescription)
    {
        ArgumentNullException.ThrowIfNull(world);

        Dictionary<string, Question> questions = new(world.Judgements.Count, StringComparer.Ordinal);

        foreach (JudgementDefinition judgement in world.Judgements)
        {
            string instruction = Render(judgement.Question, action, stateDescription);

            questions[judgement.Key] = judgement.Kind switch
            {
                JudgementKinds.Choice => Question.Choice(
                    instruction,
                    ToCriteria(judgement.Options)),

                JudgementKinds.Score => Question.Score(
                    instruction,
                    [.. judgement.Levels.Select(l => StructuredValue.FromString(l))]),

                _ => Question.Noul(instruction),
            };
        }

        return questions;
    }

    /// <summary>
    /// Reads a response into a judgement set, applying the world's declared fallbacks.
    /// </summary>
    /// <param name="world">The world being played.</param>
    /// <param name="response">The response to read.</param>
    /// <returns>Every declared judgement, with unusable ones marked.</returns>
    public static JudgementSet FromResponse(WorldDefinition world, SystemOneResponse response)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(response);

        IDictionary<string, Answer> answers = response.AnswersOrEmpty;
        JudgementSet set = new();

        foreach (JudgementDefinition judgement in world.Judgements)
        {
            set.Add(Read(world, judgement, answers));
        }

        return set;
    }

    /// <summary>
    /// Renders a template's placeholders for one turn.
    /// </summary>
    /// <param name="template">The template.</param>
    /// <param name="action">What the player typed.</param>
    /// <param name="stateDescription">The rendered state.</param>
    /// <returns>The rendered text.</returns>
    public static string Render(string template, string action, string stateDescription) =>
        Placeholders.Substitute(
            Placeholders.Substitute(template, "action", action),
            "state",
            stateDescription);

    private static JudgementResult Read(
        WorldDefinition world,
        JudgementDefinition judgement,
        IDictionary<string, Answer> answers)
    {
        if (!answers.TryGetValue(judgement.Key, out Answer? answer))
        {
            return Fallback(judgement);
        }

        return judgement.Kind switch
        {
            JudgementKinds.Choice => ReadChoice(judgement, answer),
            JudgementKinds.Score => ReadScore(judgement, answer),
            _ => ReadNoul(judgement, answer),
        };
    }

    private static JudgementResult ReadNoul(JudgementDefinition judgement, Answer answer)
    {
        if (answer is not NoulAnswer noul)
        {
            return Fallback(judgement);
        }

        // Clamped, not trusted: every rule compares against thresholds, so a value outside 0..1 would
        // silently change the game's behaviour rather than fail loudly.
        double value = Math.Clamp(noul.Noul, 0.0, 1.0);

        return new JudgementResult(judgement.Key, judgement.Kind, value, null, Usable: true);
    }

    private static JudgementResult ReadChoice(JudgementDefinition judgement, Answer answer)
    {
        if (answer is not ChoiceAnswer choice
            || !judgement.Options.Any(o => o.Key == choice.Choice))
        {
            // An option the schema never declared is unusable rather than passed through: a rule
            // comparing it would silently match nothing, which reads as the world ignoring the player.
            return Fallback(judgement);
        }

        return new JudgementResult(judgement.Key, judgement.Kind, choice.Confidence, choice.Choice, Usable: true);
    }

    private static JudgementResult ReadScore(JudgementDefinition judgement, Answer answer)
    {
        if (answer is not ScoreAnswer score)
        {
            return Fallback(judgement);
        }

        // A score is a weighted position, which can land between levels. Rounding to the nearest level
        // is the reading that lets a rule compare against a level label at all.
        int rounded = (int)Math.Round(score.Score, MidpointRounding.AwayFromZero);

        if (rounded < 0 || rounded >= judgement.Levels.Count)
        {
            return Fallback(judgement);
        }

        string label = judgement.Levels[rounded];

        return new JudgementResult(judgement.Key, judgement.Kind, score.Score, label, Usable: true);
    }

    private static JudgementResult Fallback(JudgementDefinition judgement)
    {
        string? fallback = judgement.Fallback;

        if (fallback is null)
        {
            // The validator requires a fallback for choice and score, and a parseable one for noul, so
            // this is reachable only for a noul with no fallback declared, which defaults to the value
            // that decides nothing.
            return new JudgementResult(judgement.Key, judgement.Kind, 0.5, null, Usable: false);
        }

        return judgement.Kind switch
        {
            JudgementKinds.Noul when double.TryParse(
                fallback,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double number) =>
                new JudgementResult(judgement.Key, judgement.Kind, Math.Clamp(number, 0.0, 1.0), null, Usable: false),

            JudgementKinds.Noul =>
                new JudgementResult(judgement.Key, judgement.Kind, 0.5, null, Usable: false),

            _ => new JudgementResult(judgement.Key, judgement.Kind, null, fallback, Usable: false),
        };
    }

    private static Dictionary<string, StructuredValue?> ToCriteria(IList<OptionDefinition> options)
    {
        Dictionary<string, StructuredValue?> criteria = new(options.Count, StringComparer.Ordinal);

        foreach (OptionDefinition option in options)
        {
            criteria[option.Key] = StructuredValue.FromString(option.Description);
        }

        return criteria;
    }
}
