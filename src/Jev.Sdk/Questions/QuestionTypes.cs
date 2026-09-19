// QuestionTypes.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the question and
// answer unions. See requirements/requirements.md, R3.
//
// Type: QuestionTypes

namespace Jev.Sdk;

/// <summary>
/// The wire values of the question and answer kinds. Question and answer kinds always
/// correspond: a <c>noul</c> question is answered by a <c>noul</c> answer.
/// </summary>
public static class QuestionTypes
{
    /// <summary>A yes/no question, answered with the probability that the statement is true.</summary>
    public const string Noul = "noul";

    /// <summary>A selection from a set of options.</summary>
    public const string Choice = "choice";

    /// <summary>A rating along ordered levels.</summary>
    public const string Score = "score";
}
