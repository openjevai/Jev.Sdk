// ConfigurationWiringTests.cs
// Part of Jev.Sdk.IntegrationTests. Proves the key reaches the client through the library's own
// configuration path, and that the local settings file works the way the workflow claims.
//
// These tests do NOT call the API, so they run whether or not a key is configured. They are the ones
// that answer "did my appSettings.Local.json actually get read?" — the question a silent fallback to
// a prompt makes impossible to answer by observation.

using Jev.Sdk;
using Jev.Sdk.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Jev.Sdk.IntegrationTests;

/// <summary>
/// Verifies the configuration plumbing the live tests depend on, without spending a call.
/// </summary>
public class ConfigurationWiringTests
{
    [Fact]
    public void TheMachineTokenUsedHere_ResolvesToTheLocalFileName()
    {
        // This is the mechanism in one assertion: the library's documented machine override produces
        // the file name, rather than the project hard-coding it in two places.
        Assert.Equal("appSettings.Local.json", LiveSettings.LocalFileName);
    }

    [Fact]
    public void TheLocalFile_IsAmongTheCandidateNamesTheLoaderWillResolve()
    {
        IReadOnlyList<string> candidates = JevConfigurationLoader.CandidateFileNames("Local");

        Assert.Equal(["appSettings.json", LiveSettings.LocalFileName], candidates);
    }

    [Fact]
    public void WithoutTheLocalToken_TheLocalFileIsNotACandidate()
    {
        // The other half of the mechanism, and the reason the token is passed explicitly: with a
        // machine name of anything else, appSettings.Local.json is simply not read. Anyone who assumes
        // a "Local" file is picked up automatically is wrong, and this pins that.
        IReadOnlyList<string> candidates = JevConfigurationLoader.CandidateFileNames("SOME-OTHER-MACHINE");

        Assert.DoesNotContain(LiveSettings.LocalFileName, candidates);
    }

    [Fact]
    public void TheLocalFile_OutranksTheGenericOne()
    {
        // Precedence, proven on real files in a temporary directory: the machine-specific file wins.
        // Done with real files rather than a stub because the point is that the loader reads them.
        string directory = Path.Combine(Path.GetTempPath(), $"jev-integration-{Guid.NewGuid():N}");

        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(
                Path.Combine(directory, JevEnvironment.GenericSettingsFile),
                """{ "Jev": { "ApiKey": "generic-key" } }""");

            File.WriteAllText(
                Path.Combine(directory, LiveSettings.LocalFileName),
                """{ "Jev": { "ApiKey": "local-key" } }""");

            IConfigurationRoot configuration = JevConfigurationLoader.Build(directory, "Local");

            Assert.Equal("local-key", JevOptionsBinding.FromConfiguration(configuration).ApiKey);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TheEnvironment_OutranksTheFile()
    {
        // The documented precedence, and the reason a key in the environment is the safer workflow:
        // it cannot be shadowed by a file. Asserted with an injected lookup so the real environment is
        // not mutated by a test run.
        string directory = Path.Combine(Path.GetTempPath(), $"jev-integration-{Guid.NewGuid():N}");

        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(
                Path.Combine(directory, LiveSettings.LocalFileName),
                """{ "Jev": { "ApiKey": "file-key" } }""");

            IConfigurationRoot configuration = JevConfigurationLoader.Build(directory, "Local");
            string? fileKey = JevOptionsBinding.FromConfiguration(configuration).ApiKey;

            Assert.Equal("file-key", fileKey);

            // The environment wins over the section when both are present, because the section only
            // fills a value the environment did not already supply.
            JevClientOptions fromEnvironment = JevOptionsBinding.FromConfiguration(configuration, apiKey: "environment-key");

            Assert.Equal("environment-key", fromEnvironment.ApiKey);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void NoSettingsFile_LeavesTheKeyUnset()
    {
        // The honest empty case: with no file present the loader still builds, and reports no key
        // rather than throwing. This is what makes "no key configured" a skip instead of a failure.
        string directory = Path.Combine(Path.GetTempPath(), $"jev-integration-{Guid.NewGuid():N}");

        Directory.CreateDirectory(directory);

        try
        {
            IConfigurationRoot configuration = JevConfigurationLoader.Build(directory, "Local");

            Assert.True(string.IsNullOrWhiteSpace(JevOptionsBinding.FromConfiguration(configuration).ApiKey));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("REPLACE ME")]
    [InlineData("replace me")]
    [InlineData("  REPLACE ME  ")]
    [InlineData("Replace Me")]
    public void PlaceholderCasingAndWhitespace_AllCountAsThePlaceholder(string value)
    {
        // The guard that keeps a copied-but-unedited example file from making the live tests run and
        // fail with an authentication error. Case and surrounding whitespace are ignored because
        // placeholder text gets retyped by hand.
        Assert.True(LiveSettings.IsPlaceholder(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("jev_real_key_value")]
    public void AnythingThatIsNotThePlaceholder_IsNotTreatedAsOne(string? value)
    {
        // Blank is a separate case from the placeholder: blank is "not supplied", the placeholder is
        // "supplied but unedited". Both must leave the live tests skipped, but only one of them is a
        // placeholder, and conflating them would make the skip message wrong.
        Assert.False(LiveSettings.IsPlaceholder(value));
    }

    [Fact]
    public void TheResolvedKey_IsNeverThePlaceholder()
    {
        // Whatever the resolution produced, it must be either absent or a real value. A test that
        // asserted the key merely exists would pass on an unedited example file.
        if (LiveSettings.IsConfigured)
        {
            Assert.False(LiveSettings.IsPlaceholder(LiveSettings.ApiKey));
            Assert.False(string.IsNullOrWhiteSpace(LiveSettings.ApiKey));
        }
    }

    [Fact]
    public void TheSkipMessage_NamesBothPlacesAKeyCanComeFrom()
    {
        // A skip that does not say how to fix itself is only slightly better than a failure.
        string message = LiveSettings.WhereWeLooked();

        Assert.Contains(JevEnvironment.ApiKeyVariable, message, StringComparison.Ordinal);
        Assert.Contains(LiveSettings.LocalFileName, message, StringComparison.Ordinal);
    }
}
