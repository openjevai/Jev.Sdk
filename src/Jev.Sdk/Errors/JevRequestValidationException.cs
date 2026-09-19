// JevRequestValidationException.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for error mapping.
// See requirements/requirements.md, R9.
//
// Type: JevRequestValidationException

namespace Jev.Sdk;

/// <summary>
/// The request was rejected locally before any network call was made.
/// </summary>
/// <remarks>
/// This is deliberately distinct from <see cref="JevValidationException"/>, which reports a
/// rejection by the server. A local rejection is a bug in the calling code that can be fixed
/// immediately; a server rejection means the local view of the contract is out of date.
/// </remarks>
public sealed class JevRequestValidationException : JevException
{
    /// <summary>Initialises an exception.</summary>
    /// <param name="message">The message.</param>
    public JevRequestValidationException(string message)
        : base(message)
    {
        Problems = [];
    }

    /// <summary>Initialises an exception listing every problem found.</summary>
    /// <param name="problems">One message per problem, in the order discovered.</param>
    public JevRequestValidationException(IReadOnlyList<string> problems)
        : base($"The request is not valid:{Environment.NewLine}- {string.Join($"{Environment.NewLine}- ", problems)}")
    {
        ArgumentNullException.ThrowIfNull(problems);
        Problems = problems;
    }

    /// <summary>Every problem found, in the order discovered. Never null.</summary>
    public IReadOnlyList<string> Problems { get; }
}
