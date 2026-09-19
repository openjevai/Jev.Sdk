// EnvironmentApiKeyProvider.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the authentication
// seam. See requirements/requirements.md, R10 and R12.
//
// Type: EnvironmentApiKeyProvider

namespace Jev.Sdk;

/// <summary>
/// Supplies an API key read from an environment variable.
/// </summary>
/// <remarks>
/// The environment is read on each call rather than captured at construction, so a process
/// that sets the variable after startup still works, and a test can vary it freely.
/// </remarks>
public sealed class EnvironmentApiKeyProvider : IApiKeyProvider
{
    private readonly string _variableName;
    private readonly Func<string, string?> _readVariable;

    /// <summary>
    /// Initialises a provider reading <see cref="JevEnvironment.ApiKeyVariable"/>
    /// (<c>TYPESAFE_API_KEY</c>).
    /// </summary>
    public EnvironmentApiKeyProvider()
        : this(JevEnvironment.ApiKeyVariable, Environment.GetEnvironmentVariable)
    {
    }

    /// <summary>Initialises a provider reading a named environment variable.</summary>
    /// <param name="variableName">The environment variable to read.</param>
    /// <exception cref="ArgumentException">The name is null, empty, or whitespace.</exception>
    public EnvironmentApiKeyProvider(string variableName)
        : this(variableName, Environment.GetEnvironmentVariable)
    {
    }

    /// <summary>
    /// Initialises a provider reading a named variable through a supplied reader. Intended
    /// for tests, which pass a dictionary lookup instead of touching the real environment.
    /// </summary>
    /// <param name="variableName">The environment variable name to report.</param>
    /// <param name="readVariable">Reads a variable by name. May return null.</param>
    /// <exception cref="ArgumentException">The name is null, empty, or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="readVariable"/> is null.</exception>
    public EnvironmentApiKeyProvider(string variableName, Func<string, string?> readVariable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variableName);
        ArgumentNullException.ThrowIfNull(readVariable);

        _variableName = variableName;
        _readVariable = readVariable;
    }

    /// <summary>The environment variable this provider reads.</summary>
    public string VariableName => _variableName;

    /// <inheritdoc />
    public Task<string?> GetApiKeyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string? value = _readVariable(_variableName);

        return Task.FromResult(string.IsNullOrWhiteSpace(value) ? null : value);
    }
}
