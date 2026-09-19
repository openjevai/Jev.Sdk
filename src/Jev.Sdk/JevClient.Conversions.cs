// JevClient.Conversions.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the client.
// See requirements/requirements.md, R1 and R3.
//
// Type: SystemOneResponseExtensions
//
// Convenience readers for the answer union. The typed readers live on Answer itself; these
// exist so that calling code reads as a decision rather than a chain of casts, which is the
// shape the API is designed for.

namespace Jev.Sdk;

/// <summary>
/// Convenience readers for typed answers.
/// </summary>
public static class SystemOneResponseExtensions
{
    /// <summary>
    /// Returns the answer to <paramref name="questionId"/> as a yes/no probability.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <param name="questionId">The question key supplied in the request.</param>
    /// <returns>The probability that the statement is true, from 0 to 1.</returns>
    /// <exception cref="KeyNotFoundException">No answer was returned under that name.</exception>
    /// <exception cref="InvalidCastException">The question with that name was not a Noul question.</exception>
    public static double Noul(this SystemOneResponse response, string questionId)
    {
        ArgumentNullException.ThrowIfNull(response);
        return response[questionId].AsNoul().Noul;
    }

    /// <summary>
    /// Returns the answer to <paramref name="questionId"/> as the selected option name.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <param name="questionId">The question key supplied in the request.</param>
    /// <returns>The name of the highest-probability option.</returns>
    /// <exception cref="KeyNotFoundException">No answer was returned under that name.</exception>
    /// <exception cref="InvalidCastException">The question with that name was not a Choice question.</exception>
    public static string Choice(this SystemOneResponse response, string questionId)
    {
        ArgumentNullException.ThrowIfNull(response);
        return response[questionId].AsChoice().Choice;
    }

    /// <summary>
    /// Returns the answer to <paramref name="questionId"/> as a score.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <param name="questionId">The question key supplied in the request.</param>
    /// <returns>The probability-weighted position on the question's scale.</returns>
    /// <exception cref="KeyNotFoundException">No answer was returned under that name.</exception>
    /// <exception cref="InvalidCastException">The question with that name was not a Score question.</exception>
    public static double Score(this SystemOneResponse response, string questionId)
    {
        ArgumentNullException.ThrowIfNull(response);
        return response[questionId].AsScore().Score;
    }

    /// <summary>
    /// Returns the confidence reported for <paramref name="questionId"/>, or null for a Noul
    /// answer, which reports none.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <param name="questionId">The question key supplied in the request.</param>
    /// <returns>The reported confidence from 0 to 1, or null when the API reported none.</returns>
    /// <exception cref="KeyNotFoundException">No answer was returned under that name.</exception>
    public static double? Confidence(this SystemOneResponse response, string questionId)
    {
        ArgumentNullException.ThrowIfNull(response);
        return response[questionId].Confidence;
    }
}
