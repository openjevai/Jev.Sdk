// DependencyInjectionTests.cs
// Part of Jev.Sdk.Tests. These tests exercise the library through the container rather than
// through direct construction, which is how a host wires it. They are the reason the DI package
// is not merely a convenience: they prove the seams resolve correctly when a provider builds
// them, including the substitution path a host uses to replace a collaborator.

using Jev.Sdk;
using Jev.Sdk.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jev.Sdk.Tests;

public class DependencyInjectionTests
{
    private static IConfiguration ConfigurationWithKey(string apiKey) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [$"{JevEnvironment.ConfigurationSection}:{JevEnvironment.ApiKeyKey}"] = apiKey,
            })
            .Build();

    [Fact]
    public void AddJevClient_RegistersTheClientAndItsSeams()
    {
        ServiceCollection services = new();
        IConfiguration configuration = ConfigurationWithKey("di-key");

        services.AddSingleton<ITypeSafeTransport>(new StubTransport());
        services.AddJevClient(configuration);

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<JevClient>());
        Assert.NotNull(provider.GetService<JevClientOptions>());
        Assert.NotNull(provider.GetService<IApiKeyProvider>());
    }

    [Fact]
    public async Task AddJevClient_ResolvesTheKeyFromConfiguration()
    {
        ServiceCollection services = new();
        services.AddJevClient(ConfigurationWithKey("di-key"));

        using ServiceProvider provider = services.BuildServiceProvider();

        IApiKeyProvider keyProvider = provider.GetRequiredService<IApiKeyProvider>();
        string? key = await keyProvider.GetApiKeyAsync(CancellationToken.None);

        Assert.Equal("di-key", key);
    }

    [Fact]
    public async Task AddJevClient_ClientIsUsableThroughTheContainer()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());

        ServiceCollection services = new();
        services.AddSingleton<ITypeSafeTransport>(transport);
        services.AddJevClient(ConfigurationWithKey("di-key"));

        using ServiceProvider provider = services.BuildServiceProvider();
        using JevClient client = provider.GetRequiredService<JevClient>();

        SystemOneResponse response = await client.SystemOneAsync(
            "text", TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        Assert.Equal("jev-latest", response.Model);
        Assert.Equal(1, transport.RequestCount);
    }

    [Fact]
    public void AddJevClient_HonoursACallerSuppliedTransport()
    {
        // TryAdd means a registration made before the extension wins, which is how a host
        // replaces a seam without the library knowing about it.
        StubTransport transport = new StubTransport();
        ServiceCollection services = new();

        services.AddSingleton<ITypeSafeTransport>(transport);
        services.AddJevClient(ConfigurationWithKey("di-key"));

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Same(transport, provider.GetRequiredService<ITypeSafeTransport>());
    }

    [Fact]
    public async Task AddJevClient_HonoursACallerSuppliedKeyProvider()
    {
        ServiceCollection services = new();
        services.AddSingleton<IApiKeyProvider>(new StaticApiKeyProvider("replaced"));
        services.AddJevClient(ConfigurationWithKey("di-key"));

        using ServiceProvider provider = services.BuildServiceProvider();

        IApiKeyProvider keyProvider = provider.GetRequiredService<IApiKeyProvider>();
        Assert.Equal("replaced", await keyProvider.GetApiKeyAsync(CancellationToken.None));
    }

    [Fact]
    public void AddJevClient_AppliesTheConfigureActionLast()
    {
        ServiceCollection services = new();
        services.AddJevClient(
            ConfigurationWithKey("di-key"),
            options =>
            {
                options.DefaultModel = "overridden-model";
                options.MaxRetries = 7;
            });

        using ServiceProvider provider = services.BuildServiceProvider();
        JevClientOptions options = provider.GetRequiredService<JevClientOptions>();

        Assert.Equal("overridden-model", options.DefaultModel);
        Assert.Equal(7, options.MaxRetries);
    }

    [Fact]
    public void AddJevClient_UsesTheRegisteredConfigurationWhenNoneIsPassed()
    {
        ServiceCollection services = new();
        services.AddSingleton(ConfigurationWithKey("from-registered-config"));
        services.AddJevClient();

        using ServiceProvider provider = services.BuildServiceProvider();
        JevClientOptions options = provider.GetRequiredService<JevClientOptions>();

        Assert.Equal("from-registered-config", options.ApiKey);
    }

    [Fact]
    public void AddJevClient_ResolvesWithoutAnyConfiguration()
    {
        ServiceCollection services = new();
        services.AddJevClient();

        using ServiceProvider provider = services.BuildServiceProvider();
        JevClientOptions options = provider.GetRequiredService<JevClientOptions>();

        Assert.Null(options.ApiKey);
        Assert.Equal(JevClientOptions.DefaultModelName, options.DefaultModel);
    }

    [Fact]
    public async Task AddJevClient_LogsThroughTheRegisteredLogger()
    {
        StubTransport transport = new StubTransport().EnqueueJson(TestClient.SuccessJson());
        RecordingLoggerProvider recorder = new();

        ServiceCollection services = new();
        services.AddSingleton<ITypeSafeTransport>(transport);
        services.AddLogging(builder => builder
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(recorder));
        services.AddJevClient(ConfigurationWithKey("di-key"));

        using ServiceProvider provider = services.BuildServiceProvider();
        using JevClient client = provider.GetRequiredService<JevClient>();

        await client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        Assert.Contains(recorder.Messages, message => message.Contains("systemone", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Warmup_FetchesTheModelListThroughTheContainer()
    {
        // The host-startup path: the client is resolved from the container, warmed, and a later
        // cached read is then free.
        StubTransport transport = new StubTransport().EnqueueJson(
            """{"models":[{"name":"jev-latest","description":"d","release_date":"2026-09-15"}]}""");

        ServiceCollection services = new();
        services.AddSingleton<ITypeSafeTransport>(transport);
        services.AddLogging();
        services.AddJevClient(ConfigurationWithKey("di-key"));

        using ServiceProvider provider = services.BuildServiceProvider();
        using JevClient client = provider.GetRequiredService<JevClient>();

        bool warmed = await JevClientWarmup.WarmAsync(
            client,
            provider.GetService<ILogger<JevClient>>(),
            CancellationToken.None);

        Assert.True(warmed);
        Assert.Equal(1, transport.RequestCount);

        IReadOnlyList<ModelMetadata> models = await client.GetAvailableModelsAsync(CancellationToken.None);

        Assert.Single(models);
        Assert.Equal(1, transport.RequestCount);
    }

    [Fact]
    public async Task Warmup_ReportsFailureWithoutThrowing()
    {
        StubTransport transport = new StubTransport()
            .EnqueueJson("""{"error":"bad key"}""", System.Net.HttpStatusCode.Unauthorized);

        ServiceCollection services = new();
        services.AddSingleton<ITypeSafeTransport>(transport);
        services.AddJevClient(ConfigurationWithKey("di-key"));

        using ServiceProvider provider = services.BuildServiceProvider();
        using JevClient client = provider.GetRequiredService<JevClient>();

        Assert.False(await JevClientWarmup.WarmAsync(client, logger: null, CancellationToken.None));
    }

    [Fact]
    public async Task Warmup_LogsTheOutcomeWhenALoggerIsSupplied()
    {
        // Both branches are exercised: success logs at debug, failure at information, so an operator
        // can see whether startup discovery worked.
        RecordingLoggerProvider recorder = new();
        ILoggerFactory factory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(recorder));
        ILogger logger = factory.CreateLogger("Jev.Sdk.Tests");

        StubTransport ok = new StubTransport().EnqueueJson(
            """{"models":[{"name":"jev-latest","description":"d","release_date":"2026-09-15"}]}""");

        using JevClient okClient = TestClient.Create(ok);
        Assert.True(await JevClientWarmup.WarmAsync(okClient, logger, CancellationToken.None));
        Assert.Contains(recorder.Messages, m => m.Contains("served locally", StringComparison.Ordinal));

        recorder.Messages.Clear();

        StubTransport failing = new StubTransport()
            .EnqueueJson("""{"error":"bad key"}""", System.Net.HttpStatusCode.Unauthorized);

        using JevClient failingClient = TestClient.Create(failing);
        Assert.False(await JevClientWarmup.WarmAsync(failingClient, logger, CancellationToken.None));
        Assert.Contains(recorder.Messages, m => m.Contains("retry on first use", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Warmup_RejectsANullClient()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => JevClientWarmup.WarmAsync(null!, logger: null, CancellationToken.None));
    }

    [Fact]
    public void JevConfigurationLoader_BuildsFromADirectory()
    {
        string directory = Directory.CreateTempSubdirectory("jev-config-test").FullName;

        try
        {
            File.WriteAllText(
                Path.Combine(directory, "appSettings.json"),
                """{"Jev":{"ApiKey":"generic-key","DefaultModel":"generic-model"}}""");

            File.WriteAllText(
                Path.Combine(directory, "appSettings.BUILD01.json"),
                """{"Jev":{"ApiKey":"machine-key"}}""");

            IConfigurationRoot configuration = JevConfigurationLoader.Build(directory, "BUILD01");

            // The machine-specific file overrides the generic one, which is what makes it an
            // override rather than a file that only applies when the generic one is absent.
            Assert.Equal("machine-key", configuration["Jev:ApiKey"]);

            // A setting absent from the machine file still comes from the generic one, so the
            // files layer rather than replace.
            Assert.Equal("generic-model", configuration["Jev:DefaultModel"]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void JevConfigurationLoader_EnvironmentBeatsEveryFile()
    {
        string directory = Directory.CreateTempSubdirectory("jev-config-test").FullName;
        string variableName = $"{JevEnvironment.ConfigurationSection}__{JevEnvironment.ApiKeyKey}";
        string? previous = Environment.GetEnvironmentVariable(variableName);

        try
        {
            File.WriteAllText(
                Path.Combine(directory, "appSettings.json"),
                """{"Jev":{"ApiKey":"file-key"}}""");

            Environment.SetEnvironmentVariable(variableName, "environment-key");

            IConfigurationRoot configuration = JevConfigurationLoader.Build(directory, "BUILD01");

            // Environment variables are added last, so they win. This is what makes a container
            // work with nothing on disk.
            Assert.Equal("environment-key", configuration["Jev:ApiKey"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, previous);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void JevConfigurationLoader_ToleratesAnEmptyDirectory()
    {
        string directory = Directory.CreateTempSubdirectory("jev-config-test").FullName;

        try
        {
            IConfigurationRoot configuration = JevConfigurationLoader.Build(directory, "BUILD01");

            Assert.Null(configuration["Jev:ApiKey"]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

/// <summary>
/// Captures log messages so a test can assert that the client actually logged.
/// </summary>
internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    /// <summary>Every message logged, in order.</summary>
    public List<string> Messages { get; } = [];

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new RecordingLogger(Messages);

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private sealed class RecordingLogger : ILogger
    {
        private readonly List<string> _messages;

        internal RecordingLogger(List<string> messages) => _messages = messages;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            _messages.Add(formatter(state, exception));
        }
    }
}
