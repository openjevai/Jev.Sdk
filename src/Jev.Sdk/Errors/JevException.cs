// JevException.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the exception hierarchy.
// See requirements/requirements.md, R7.
//
// Type: JevException

namespace Jev.Sdk;

/// <summary>
/// Base type for every exception this library raises.
/// </summary>
/// <remarks>
/// Catching this type catches everything the client itself threw. Anything else that escapes
/// a call came from the caller's own code or from cancellation.
/// </remarks>
public class JevException : Exception
{
    /// <summary>Initialises an exception.</summary>
    public JevException()
    {
    }

    /// <summary>Initialises an exception with a message.</summary>
    /// <param name="message">The message.</param>
    public JevException(string message)
        : base(message)
    {
    }

    /// <summary>Initialises an exception with a message and inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public JevException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
