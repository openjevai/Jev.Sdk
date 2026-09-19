// ConfigurationTests.cs
// Part of Jev.Sdk.Tests. The API key ladder is the only configuration a caller must get right,
// so each source and the ordering between them is asserted explicitly.

using Jev.Sdk;
using Jev.Sdk.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Jev.Sdk.Tests;

public class ConfigurationTests
{
    [Fact]
    public async Task ExplicitKey_Wins()
    {
        StaticApiKeyProvider provider = new("explicit-key");

        string? key = await provider.GetApiKeyAsync(CancellationToken.None);

        Assert.Equal("explicit-key", key);
    }

    [Fact]
    public void ExplicitKey_MustNotBeBlank()
    {
        Assert.Throws<ArgumentException>(() => new StaticApiKeyProvider("  "));
    }

    [Fact]
    public async Task EnvironmentProvider_ReadsTheNamedVariable()
    {
        Dictionary<string, string?> environment = new(StringComparer.Ordinal)
        {
            ["TYPESAFE_API_KEY"] = "from-environment",
        };

        EnvironmentApiKeyProvider provider = new(JevEnvironment.ApiKeyVariable, name => environment.GetValueOrDefault(name));

        Assert.Equal(JevEnvironment.ApiKeyVariable, provider.VariableName);
        Assert.Equal("from-environment", await provider.GetApiKeyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task EnvironmentProvider_ReturnsNullWhenUnset()
    {
        EnvironmentApiKeyProvider provider = new(JevEnvironment.ApiKeyVariable, _ => null);

        Assert.Null(await provider.GetApiKeyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task EnvironmentProvider_TreatsWhitespaceAsAbsent()
    {
        EnvironmentApiKeyProvider provider = new(JevEnvironment.ApiKeyVariable, _ => "   ");

        Assert.Null(await provider.GetApiKeyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Chain_ReturnsTheFirstKeyFound()
    {
        ChainedApiKeyProvider chain = new(
            new StaticApiKeyProvider("second"),
            new StaticApiKeyProvider("third"));

        Assert.Equal("second", await chain.GetApiKeyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Chain_SkipsProvidersWithNoKey()
    {
        ChainedApiKeyProvider chain = new(
            new EnvironmentApiKeyProvider("ABSENT_VARIABLE", _ => null),
            new StaticApiKeyProvider("later"));

        Assert.Equal("later", await chain.GetApiKeyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Chain_ReturnsNullWhenNothingSuppliesAKey()
    {
        ChainedApiKeyProvider chain = new(
            new EnvironmentApiKeyProvider("ABSENT_VARIABLE", _ => null));

        Assert.Null(await chain.GetApiKeyAsync(CancellationToken.None));
    }

    [Fact]
    public void Chain_IgnoresNullProviders()
    {
        ChainedApiKeyProvider chain = new(null, null);

        Assert.True(chain.IsEmpty);
        Assert.Empty(chain.Providers);
    }

    [Fact]
    public async Task Chain_HonoursCancellation()
    {
        ChainedApiKeyProvider chain = new(new StaticApiKeyProvider("key"));
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => chain.GetApiKeyAsync(cancellation.Token));
    }

    [Fact]
    public async Task ConfigurationProvider_ReadsTheDocumentedPath()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [$"{JevEnvironment.ConfigurationSection}:{JevEnvironment.ApiKeyKey}"] = "from-config",
            })
            .Build();

        ConfigurationApiKeyProvider provider = new(configuration);

        Assert.Equal("Jev:ApiKey", provider.KeyPath);
        Assert.Equal("from-config", await provider.GetApiKeyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ConfigurationProvider_ReturnsNullWhenAbsent()
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();
        ConfigurationApiKeyProvider provider = new(configuration);

        Assert.Null(await provider.GetApiKeyAsync(CancellationToken.None));
    }

    [Fact]
    public void MachineName_ComesFromTheEnvironmentOverrideWhenSet()
    {
        // A container's machine name is a random id that changes when the container is
        // recreated, so the override is what makes the machine-specific file usable there.
        string resolved = JevConfigurationLoader.ResolveMachineName("BUILD01");

        Assert.Equal("BUILD01", resolved);
    }

    [Fact]
    public void MachineSettingsFileName_SubstitutesTheToken()
    {
        Assert.Equal("appSettings.BUILD01.json", JevConfigurationLoader.MachineSettingsFileName("BUILD01"));
    }

    [Fact]
    public void CandidateFileNames_ListGenericThenMachineSpecific()
    {
        IReadOnlyList<string> names = JevConfigurationLoader.CandidateFileNames("BUILD01");

        Assert.Equal(["appSettings.json", "appSettings.BUILD01.json"], names);
    }

    [Fact]
    public void OptionsBinding_ReadsEverySetting()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Jev:ApiKey"] = "key",
                ["Jev:BaseAddress"] = "https://example.test/v1/",
                ["Jev:DefaultModel"] = "custom-model",
                ["Jev:Timeout"] = "00:00:30",
            })
            .Build();

        JevClientOptions options = JevOptionsBinding.FromConfiguration(configuration);

        Assert.Equal("key", options.ApiKey);
        Assert.Equal(new Uri("https://example.test/v1/"), options.BaseAddress);
        Assert.Equal("custom-model", options.DefaultModel);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Timeout);
    }

    [Fact]
    public void OptionsBinding_LeavesDefaultsWhenSectionIsAbsent()
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();

        JevClientOptions options = JevOptionsBinding.FromConfiguration(configuration);

        Assert.Null(options.ApiKey);
        Assert.Equal(JevClientOptions.DefaultBaseAddress, options.BaseAddress);
        Assert.Equal(JevClientOptions.DefaultModelName, options.DefaultModel);
    }

    [Fact]
    public void OptionsBinding_ExplicitKeyBeatsTheSection()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Jev:ApiKey"] = "from-config",
            })
            .Build();

        JevClientOptions options = JevOptionsBinding.FromConfiguration(configuration, apiKey: "explicit");

        Assert.Equal("explicit", options.ApiKey);
    }

    [Fact]
    public void OptionsBinding_RejectsAnInvalidBaseAddress()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Jev:BaseAddress"] = "not-a-uri",
            })
            .Build();

        Assert.Throws<JevConfigurationException>(() => JevOptionsBinding.FromConfiguration(configuration));
    }

    [Fact]
    public void OptionsBinding_RejectsAnInvalidTimeout()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Jev:Timeout"] = "not-a-duration",
            })
            .Build();

        Assert.Throws<JevConfigurationException>(() => JevOptionsBinding.FromConfiguration(configuration));
    }
}
