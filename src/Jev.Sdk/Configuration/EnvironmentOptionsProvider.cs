// EnvironmentOptionsProvider.cs
// Part of Jev.Sdk. This file is one of the partial-class/file set for the configuration surface.
// See requirements/requirements.md, R10.
//
// Type: EnvironmentOptionsProvider
//
// The vendor's SDKs read TYPESAFE_BASE_URL and TYPESAFE_DEFAULT_MODEL in addition to the API key
// variable, so a caller who has already configured those for the Python or JavaScript client gets
// the same behaviour here without editing code. This type applies them to an options object,
// leaving an explicitly-set option alone: the environment supplies a default, it does not override
// a decision the caller made in code.
//
// OpenJEV support (https://openjev.sh) is additive: TypeSafe stays the default. The OpenJEV
// gateway is selected only when JEV_PROVIDER=openjev is set, or when OPENJEV_API_KEY is present and
// no TypeSafe key is. A caller with a TypeSafe key sees no change.

namespace Jev.Sdk;

/// <summary>
/// Reads client settings from the environment.
/// </summary>
public static class EnvironmentOptionsProvider
{
    /// <summary>
    /// Applies environment-supplied settings to <paramref name="options"/>.
    /// </summary>
    /// <param name="options">The options to fill in. Not replaced.</param>
    /// <param name="readVariable">
    /// Reads a variable by name. Defaults to the real environment; supply a lookup in tests.
    /// </param>
    /// <returns>The same options instance, for chaining.</returns>
    /// <remarks>
    /// Only values that are still at their default are taken from the environment, so a caller who
    /// set a base address or a model explicitly keeps it. An unparseable base URL is ignored rather
    /// than throwing here, because construction-time option validation reports it with a better
    /// message than this method could.
    /// </remarks>
    public static JevClientOptions Apply(
        JevClientOptions options,
        Func<string, string?>? readVariable = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        Func<string, string?> read = readVariable ?? Environment.GetEnvironmentVariable;

        // OpenJEV provider selection (additive; TypeSafe stays the default). When OpenJEV is
        // selected, the base address and model switch to the OpenJEV defaults — but only when the
        // caller has not already set them, so an explicit choice in code is always honoured.
        if (ShouldUseOpenjev(read))
        {
            if (options.BaseAddress == JevClientOptions.DefaultBaseAddress)
            {
                options.BaseAddress = JevClientOptions.OpenjevBaseAddress;
            }

            if (options.DefaultModel == JevClientOptions.DefaultModelName)
            {
                options.DefaultModel = JevClientOptions.OpenjevModelName;
            }
        }

        if (options.BaseAddress == JevClientOptions.DefaultBaseAddress)
        {
            string? baseUrl = read(JevEnvironment.BaseUrlVariable);

            if (!string.IsNullOrWhiteSpace(baseUrl) &&
                Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? parsed))
            {
                options.BaseAddress = parsed;
            }
        }

        if (options.DefaultModel == JevClientOptions.DefaultModelName)
        {
            string? model = read(JevEnvironment.DefaultModelVariable);

            if (!string.IsNullOrWhiteSpace(model))
            {
                options.DefaultModel = model;
            }
        }

        return options;
    }

    /// <summary>
    /// Decides whether the OpenJEV gateway should be used, following the documented selection
    /// rule: an explicit <c>JEV_PROVIDER=openjev</c> wins; otherwise TypeSafe is the default when
    /// its key is set; otherwise OpenJEV is used when only <c>OPENJEV_API_KEY</c> is present.
    /// </summary>
    private static bool ShouldUseOpenjev(Func<string, string?> read)
    {
        string? provider = read(JevEnvironment.ProviderVariable);

        if (string.Equals(provider, "openjev", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // TypeSafe stays the default when its key is set.
        string? typesafeKey = read(JevEnvironment.ApiKeyVariable);

        if (!string.IsNullOrWhiteSpace(typesafeKey))
        {
            return false;
        }

        // Otherwise OpenJEV is used only when its key is present.
        string? openjevKey = read(JevEnvironment.OpenjevApiKeyVariable);

        return !string.IsNullOrWhiteSpace(openjevKey);
    }
}
