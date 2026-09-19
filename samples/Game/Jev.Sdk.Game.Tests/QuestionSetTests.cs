// QuestionSetTests.cs
// Part of Jev.Sdk.Game.Tests. The questions asked each turn.
//
// The question set is the demo's actual subject, so it is worth asserting precisely: five questions of
// three kinds, keyed by names the reading code depends on, with a vocabulary that matches what the
// rules switch on. A rename in one place and not the other would silently produce dull turns.

using Jev.Sdk;

namespace Jev.Sdk.Game.Tests;

public class QuestionSetTests
{
    [Fact]
    public void EveryTurn_AsksFiveQuestions()
    {
        Dictionary<string, Question> questions = QuestionSet.Build("grab the key");

        Assert.Equal(5, questions.Count);
    }

    [Fact]
    public void EveryQuestionKey_IsOneTheReadingCodeKnows()
    {
        // The contract between asking and reading. Keys are constants for this reason.
        Dictionary<string, Question> questions = QuestionSet.Build("grab the key");

        Assert.Equal(QuestionSet.Keys.OrderBy(k => k, StringComparer.Ordinal), questions.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void OneCall_CarriesAllThreeQuestionKinds()
    {
        // The demonstration: a single call producing independent judgements of different types.
        Dictionary<string, Question> questions = QuestionSet.Build("smash the window");

        Assert.IsType<ChoiceQuestion>(questions[QuestionSet.IntentKey]);
        Assert.IsType<NoulQuestion>(questions[QuestionSet.PlausibleKey]);
        Assert.IsType<NoulQuestion>(questions[QuestionSet.WakesGuardKey]);
        Assert.IsType<ScoreQuestion>(questions[QuestionSet.NoiseKey]);
        Assert.IsType<NoulQuestion>(questions[QuestionSet.ProgressKey]);
    }

    [Fact]
    public void TheIntentQuestion_OffersEveryActionTheRulesHandle()
    {
        // A vocabulary mismatch here is the bug that would make the game unplayable in a quiet way: the
        // model would classify into an option the rules have no case for.
        ChoiceQuestion intent = (ChoiceQuestion)QuestionSet.Build("anything")[QuestionSet.IntentKey];

        Assert.Equal(
            ActionIntent.All.OrderBy(a => a, StringComparer.Ordinal),
            intent.Criteria.Keys.OrderBy(a => a, StringComparer.Ordinal));
    }

    [Fact]
    public void TheNoiseQuestion_UsesTheScaleTheRulesCompareAgainst()
    {
        ScoreQuestion noise = (ScoreQuestion)QuestionSet.Build("anything")[QuestionSet.NoiseKey];

        Assert.Equal(NoiseLevels.All.Count, noise.Criteria.Count);

        foreach (string level in NoiseLevels.All)
        {
            Assert.Contains(noise.Criteria, c => c.AsString() == level);
        }
    }

    [Fact]
    public void TheIntentQuestion_DescribesEachOption()
    {
        // The demo's central claim is that the player never learns commands. Bare option names classify
        // far worse than described ones, so a missing description is a real regression.
        ChoiceQuestion intent = (ChoiceQuestion)QuestionSet.Build("anything")[QuestionSet.IntentKey];

        foreach (string action in ActionIntent.All)
        {
            Assert.True(intent.Criteria.TryGetValue(action, out StructuredValue? description), $"no criteria for '{action}'");
            Assert.False(string.IsNullOrWhiteSpace(description?.AsString()), $"'{action}' has no description");
        }
    }

    [Fact]
    public void EveryQuestion_QuotesThePlayersOwnWords()
    {
        // The instruction must actually carry the action, or the model is judging nothing.
        Dictionary<string, Question> questions = QuestionSet.Build("pocket the little brass thing");

        Assert.Contains("pocket the little brass thing", ((NoulQuestion)questions[QuestionSet.PlausibleKey]).Instructions!.AsString()!, StringComparison.Ordinal);
        Assert.Contains("pocket the little brass thing", ((NoulQuestion)questions[QuestionSet.WakesGuardKey]).Instructions!.AsString()!, StringComparison.Ordinal);
        Assert.Contains("pocket the little brass thing", ((NoulQuestion)questions[QuestionSet.ProgressKey]).Instructions!.AsString()!, StringComparison.Ordinal);
        Assert.Contains("pocket the little brass thing", ((ChoiceQuestion)questions[QuestionSet.IntentKey]).Instructions!.AsString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void AVeryLongAction_IsCappedRatherThanSentWhole()
    {
        // A pasted document should not crowd out the question itself.
        string wall = new('x', 5000);

        Dictionary<string, Question> questions = QuestionSet.Build(wall);
        string instruction = ((NoulQuestion)questions[QuestionSet.PlausibleKey]).Instructions!.AsString()!;

        Assert.True(instruction.Length < 1000, $"instruction was {instruction.Length} characters");
        Assert.Contains("…", instruction, StringComparison.Ordinal);
    }

    [Fact]
    public void ControlCharacters_AreFlattened()
    {
        // An instruction is a sentence; a multi-line paste would break that shape.
        Dictionary<string, Question> questions = QuestionSet.Build("grab\nthe\ttab\u0000key");
        string instruction = ((NoulQuestion)questions[QuestionSet.PlausibleKey]).Instructions!.AsString()!;

        Assert.DoesNotContain('\n', instruction);
        Assert.DoesNotContain('\t', instruction);
        Assert.DoesNotContain('\u0000', instruction);
    }

    [Fact]
    public void AnEmptyAction_IsStillSentAsSomethingReadable()
    {
        Dictionary<string, Question> questions = QuestionSet.Build("   ");
        string instruction = ((NoulQuestion)questions[QuestionSet.PlausibleKey]).Instructions!.AsString()!;

        Assert.Contains("(nothing)", instruction, StringComparison.Ordinal);
    }

    [Fact]
    public void ANullAction_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => QuestionSet.Build(null!));
    }
}
