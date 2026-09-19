// JudgementReadingTests.cs
// Part of Jev.Sdk.Game.Tests. Reading the model's answers off a real payload.
//
// RoomTests prove the rules. These prove the boundary between the model and the rules: that a real
// response is read correctly, and that a malformed or partial one produces a dull turn rather than an
// exception in the middle of a game.
//
// The payloads here are deserialized through the library's own context rather than constructed by
// hand, because the thing under test is the reading code — a hand-built response would skip exactly
// the step that could be wrong.

using System.Text.Json;
using Jev.Sdk;

namespace Jev.Sdk.Game.Tests;

public class JudgementReadingTests
{
    [Fact]
    public void ACompleteResponse_IsReadInFull()
    {
        TurnJudgement judgement = Read(
            """
            {
              "model": "test",
              "answers": {
                "intent": { "type": "choice", "choice": "take_key", "probabilities": { "take_key": 0.92 }, "confidence": 0.92 },
                "plausible": { "type": "noul", "noul": 0.99 },
                "wakes_guard": { "type": "noul", "noul": 0.14 },
                "noise": { "type": "score", "score": 1.0, "legend": { "0": "silent", "1": "quiet", "2": "loud", "3": "extremely loud" }, "probabilities": { "1": 0.8 }, "confidence": 0.8 },
                "progress": { "type": "noul", "noul": 0.8 }
              },
              "usage": { "input_tokens": 1, "output_tokens": 1 }
            }
            """);

        Assert.Equal("take_key", judgement.Intent);
        Assert.Equal(0.92, judgement.IntentConfidence);
        Assert.Equal(0.99, judgement.Plausible, 4);
        Assert.Equal(0.14, judgement.WakesGuard, 4);
        Assert.Equal(NoiseLevels.Quiet, judgement.Noise);
        Assert.Equal(0.8, judgement.Progress, 4);
        Assert.False(judgement.HadIncompleteAnswer);
    }

    [Fact]
    public void TheDesignDocumentsOwnExample_ReadsCorrectly()
    {
        // The transcript from ../../Game.md, which is the thing the demo has to reproduce.
        TurnJudgement judgement = Read(
            """
            {
              "model": "test",
              "answers": {
                "intent": { "type": "choice", "choice": "take_key", "confidence": 0.92 },
                "plausible": { "type": "noul", "noul": 0.99 },
                "wakes_guard": { "type": "noul", "noul": 0.14 },
                "noise": { "type": "score", "score": 1.0, "legend": { "1": "quiet" } },
                "progress": { "type": "noul", "noul": 0.7 }
              },
              "usage": { "input_tokens": 1, "output_tokens": 1 }
            }
            """);

        Assert.Equal("take_key", judgement.Intent);
        Assert.Equal(0.99, judgement.Plausible, 4);
        Assert.Equal(0.14, judgement.WakesGuard, 4);
        Assert.Equal(NoiseLevels.Quiet, judgement.Noise);
    }

    [Fact]
    public void AnEmptyAnswerMap_DegradesToNeutralRatherThanThrowing()
    {
        // The property that keeps a bad reply from ending the game. Every accessor falls back, and the
        // caller is told the answer was incomplete rather than being told nothing.
        TurnJudgement judgement = Read(
            """{ "model": "test", "answers": {}, "usage": { "input_tokens": 1, "output_tokens": 1 } }""");

        Assert.Equal(TurnJudgement.DefaultIntent, judgement.Intent);
        Assert.Equal(TurnJudgement.DefaultProbability, judgement.Plausible);
        Assert.Equal(TurnJudgement.DefaultProbability, judgement.WakesGuard);
        Assert.Equal(TurnJudgement.DefaultProbability, judgement.Progress);
        Assert.Equal(TurnJudgement.DefaultNoise, judgement.Noise);
        Assert.Null(judgement.IntentConfidence);
        Assert.True(judgement.HadIncompleteAnswer);
    }

    [Fact]
    public void AnAnswerOfTheWrongKind_DegradesRatherThanCasting()
    {
        // The service is not supposed to send a noul where a choice was asked for. If it ever did, the
        // game must not throw and must not read a number as an intent name.
        TurnJudgement judgement = Read(
            """
            {
              "model": "test",
              "answers": {
                "intent": { "type": "noul", "noul": 0.5 },
                "plausible": { "type": "choice", "choice": "yes" },
                "noise": { "type": "noul", "noul": 0.5 }
              },
              "usage": { "input_tokens": 1, "output_tokens": 1 }
            }
            """);

        Assert.Equal(TurnJudgement.DefaultIntent, judgement.Intent);
        Assert.Equal(TurnJudgement.DefaultProbability, judgement.Plausible);
        Assert.Equal(TurnJudgement.DefaultNoise, judgement.Noise);
        Assert.True(judgement.HadIncompleteAnswer);
    }

    [Fact]
    public void AnUnknownIntent_DegradesToOtherAndIsReportedIncomplete()
    {
        // The game must not act on an option name it cannot map, so an unknown one becomes "other" --
        // and is flagged, because the answer was not usable as given.
        TurnJudgement judgement = Read(
            """
            {
              "model": "test",
              "answers": {
                "intent": { "type": "choice", "choice": "teleport_through_wall", "confidence": 0.99 }
              },
              "usage": { "input_tokens": 1, "output_tokens": 1 }
            }
            """);

        Assert.Equal(ActionIntent.Other, judgement.Intent);
        Assert.True(judgement.HadIncompleteAnswer);
    }

    [Theory]
    [InlineData(1.4, 1.0)]
    [InlineData(-0.3, 0.0)]
    [InlineData(0.42, 0.42)]
    public void OutOfRangeProbabilities_AreClamped(double reported, double expected)
    {
        // Decisions compare against thresholds, so a value outside [0,1] would silently change the
        // rules. Clamped rather than trusted.
        TurnJudgement judgement = Read(
            $$"""
            {
              "model": "test",
              "answers": {
                "plausible": { "type": "noul", "noul": {{reported.ToString(System.Globalization.CultureInfo.InvariantCulture)}} }
              },
              "usage": { "input_tokens": 1, "output_tokens": 1 }
            }
            """);

        Assert.Equal(expected, judgement.Plausible, 4);
    }

    [Fact]
    public void AScoreBetweenLevels_RoundsToTheNearestLabel()
    {
        // A score is a weighted position, so it can land between levels. 1.6 is nearer the second level
        // than the first, and the label is what the game matches against the noise vocabulary.
        TurnJudgement judgement = Read(
            """
            {
              "model": "test",
              "answers": {
                "noise": {
                  "type": "score",
                  "score": 1.6,
                  "legend": { "0": "silent", "1": "quiet", "2": "loud", "3": "extremely loud" }
                }
              },
              "usage": { "input_tokens": 1, "output_tokens": 1 }
            }
            """);

        Assert.Equal(NoiseLevels.Loud, judgement.Noise);
    }

    [Fact]
    public void ANoiseLabelOutsideTheVocabulary_IsTreatedAsUnusable()
    {
        // If the model invents a level, the game must not adopt it: Room.IsLoud compares against a known
        // level, so an invented one would silently mean "not loud".
        TurnJudgement judgement = Read(
            """
            {
              "model": "test",
              "answers": {
                "noise": { "type": "score", "score": 2.0, "legend": { "2": "deafening" } }
              },
              "usage": { "input_tokens": 1, "output_tokens": 1 }
            }
            """);

        Assert.Equal(TurnJudgement.DefaultNoise, judgement.Noise);
        Assert.True(judgement.HadIncompleteAnswer);
    }

    [Fact]
    public void ANullAnswersMember_DegradesInsteadOfThrowing()
    {
        // The payload that produced a NullReferenceException in the library once. The game reads through
        // the never-null accessor, so a null here is a dull turn rather than a crash.
        TurnJudgement judgement = Read(
            """{ "model": "test", "answers": null, "usage": { "input_tokens": 1, "output_tokens": 1 } }""");

        Assert.True(judgement.HadIncompleteAnswer);
        Assert.Equal(TurnJudgement.DefaultIntent, judgement.Intent);
    }

    [Fact]
    public void ANullResponse_IsRejected()
    {
        // The one case that should throw: a programming mistake in the caller, not a bad reply.
        Assert.Throws<ArgumentNullException>(() => TurnJudgement.FromResponse(null!));
    }

    /// <summary>
    /// Deserializes a payload and reads a judgement from it, using the library's own serializer.
    /// </summary>
    /// <param name="json">The response body.</param>
    /// <returns>The judgement read from it.</returns>
    private static TurnJudgement Read(string json)
    {
        SystemOneResponse response = JsonSerializer.Deserialize<SystemOneResponse>(
            json,
            JevJsonContext.Default.Options)!;

        return TurnJudgement.FromResponse(response);
    }
}
