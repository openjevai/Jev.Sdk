// JevClient.Validation.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the client.
// See requirements/requirements.md, R9.
//
// Type: JevClient
//
// Local validation. Everything here runs before any network call, so a caller's mistake is
// reported immediately and specifically rather than as a server rejection several seconds
// later. Every problem found is reported at once, so a caller does not fix one issue only to
// discover the next.

namespace Jev.Sdk;

/// <summary>
/// Local request validation.
/// </summary>
public sealed partial class JevClient
{
    private static void ValidateSystemOneRequest(
        StructuredValue state,
        IDictionary<string, Question> questions,
        string model)
    {
        List<string> problems = [];

        // The model is not validated here: JevClientOptions rejects a blank default at
        // construction, and a blank per-call model already falls back to that default before
        // this runs. Checking again would be unreachable code.
        _ = model;

        if (state is null || state.IsNull)
        {
            // A null state is accepted by the server, because the specification types the member
            // permissively, but questions evaluated against nothing return noise.
            problems.Add("A state is required, because the questions are evaluated against it.");
        }

        if (questions.Count == 0)
        {
            problems.Add("At least one question is required, since a request with no questions returns nothing.");
        }

        foreach (KeyValuePair<string, Question> entry in questions)
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
            {
                problems.Add("A question id cannot be null, empty, or whitespace.");
                continue;
            }

            if (entry.Value is null)
            {
                problems.Add($"Question '{entry.Key}' is null.");
                continue;
            }

            ValidateQuestion(entry.Key, entry.Value, problems);
        }

        if (problems.Count > 0)
        {
            throw new JevRequestValidationException(problems);
        }
    }

    /// <summary>
    /// True when an instruction member is absent, JSON null, or whitespace-only text.
    /// </summary>
    /// <remarks>
    /// The whitespace case matters because the question factories route a string through
    /// <c>StructuredValue.FromString</c>, so <c>Noul("   ")</c> produces a text-shaped value that a
    /// null check does not catch. Without this the request is sent and rejected by the server, which
    /// is precisely the round trip local validation exists to prevent. Structured instructions are
    /// never blank, because a JSON object or array is content even when its text is empty, which is
    /// why this checks <c>AsString()</c> rather than the raw text.
    /// </remarks>
    private static bool IsBlank(StructuredValue? instructions)
    {
        if (instructions is null || instructions.IsNull)
        {
            return true;
        }

        return instructions.AsString() is { } text && string.IsNullOrWhiteSpace(text);
    }

    private static void ValidateQuestion(string questionId, Question question, List<string> problems)
    {
        switch (question)
        {
            case NoulQuestion noul:
                if (IsBlank(noul.Instructions))
                {
                    // The API accepts a question with no instructions, because the specification
                    // marks the member optional, but a question that asks nothing produces a
                    // meaningless answer. Requiring it here turns a confusing result into a
                    // clear error.
                    problems.Add($"Noul question '{questionId}' needs instructions saying what to evaluate.");
                }

                break;

            case ChoiceQuestion choice:
                if (IsBlank(choice.Instructions))
                {
                    problems.Add($"Choice question '{questionId}' needs instructions saying what to decide.");
                }

                if (choice.Criteria.Count == 0)
                {
                    problems.Add($"Choice question '{questionId}' needs at least one option in Criteria.");
                }

                foreach (string option in choice.Criteria.Keys)
                {
                    if (string.IsNullOrWhiteSpace(option))
                    {
                        problems.Add($"Choice question '{questionId}' has an option with an empty name.");
                    }
                }

                break;

            case ScoreQuestion score:
                if (IsBlank(score.Instructions))
                {
                    problems.Add($"Score question '{questionId}' needs instructions saying what to rate.");
                }

                // The specification permits a single level, but a one-level scale returns a
                // constant and is always a mistake.
                if (score.Criteria.Count < 2)
                {
                    problems.Add(
                        $"Score question '{questionId}' needs at least two levels in Criteria; " +
                        $"found {score.Criteria.Count}. A single level cannot express a range.");
                }

                foreach (StructuredValue level in score.Criteria)
                {
                    if (level is null || level.IsNull)
                    {
                        problems.Add($"Score question '{questionId}' has a null level description.");
                    }
                }

                break;

            case RawQuestion raw:
                problems.Add(
                    $"Question '{questionId}' is a raw question of kind '{raw.Type}', which this library " +
                    "cannot send because it does not model that kind.");
                break;

            default:
                problems.Add($"Question '{questionId}' is of an unsupported type '{question.GetType()}'.");
                break;
        }
    }
}
