// JevConfigurationException.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the exception hierarchy.
// See requirements/requirements.md, R9 and R10.
//
// Type: JevConfigurationException

namespace Jev.Sdk;

/// <summary>
/// The client is not usable as configured: a required setting is missing or a supplied value
/// is invalid.
/// </summary>
/// <remarks>
/// This is raised before any network call. Its most common cause is a missing API key, in
/// which case the message names every source that was checked.
/// </remarks>
public sealed class JevConfigurationException : JevException
{
    /// <summary>Initialises an exception.</summary>
    public JevConfigurationException()
    {
    }

    /// <summary>Initialises an exception with a message.</summary>
    /// <param name="message">The message.</param>
    public JevConfigurationException(string message)
        : base(message)
    {
    }

    /// <summary>Initialises an exception with a message and inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public JevConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
