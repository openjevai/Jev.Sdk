// CoverageEdgeCaseTests.cs
// Part of Jev.Sdk.Tests. The remaining branches that a happy path never reaches: a response body
// that fails while being read, a converter asked to write a kind it does not support, and the
// log messages for the failure paths. Each of these is a real code path a caller can hit, which
// is why they are tested rather than excluded from coverage.

using System.Net;
using System.Text.Json;
using Jev.Sdk;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Jev.Sdk.Tests;

public class CoverageEdgeCaseTests
{
    [Fact]
    public async Task SendAsync_ConvertsABodyReadFailureToConnectionException()
    {
        // The connection succeeded and the headers arrived, so this is not a failure to reach
        // the server. The body could not be read, which is a transport failure all the same.
        StubHttpMessageHandler handler = new StubHttpMessageHandler().Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ThrowingContent(),
        });

        System.Net.Http.HttpClient httpClient = new(handler, disposeHandler: false);
        using HttpTypeSafeTransport transport = new(httpClient, new StaticApiKeyProvider("k"), timeout: null);

        JevConnectionException exception = await Assert.ThrowsAsync<JevConnectionException>(
            () => transport.SendAsync(
                new TransportRequest(HttpMethod.Get, new Uri("https://api.test/v1/models")),
                CancellationToken.None));

        Assert.False(exception.IsProtocolError);

        // HttpClient wraps an IOException from the content in its own type, which the transport
        // already maps. Either way a caller sees one catchable type for a broken exchange.
        Assert.True(
            exception.InnerException is IOException or System.Net.Http.HttpRequestException,
            $"Unexpected inner type: {exception.InnerException?.GetType().Name}");
    }

    [Fact]
    public async Task FailurePaths_AreLoggedWithoutSensitiveContent()
    {
        RecordingLoggerProvider recorder = new();
        ILoggerFactory factory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(recorder));
        ILogger logger = factory.CreateLogger("Jev.Sdk.Tests");

        StubTransport transport = new StubTransport()
            .EnqueueJson("""{"error":"busy"}""", HttpStatusCode.TooManyRequests)
            .EnqueueJson("""{"error":"bad key"}""", HttpStatusCode.Unauthorized);

        using JevClient client = TestClient.Create(transport, logger: logger);

        await Assert.ThrowsAsync<JevAuthenticationException>(
            () => client.SystemOneAsync("SENSITIVE-CONTENT", TestClient.ThreeQuestions(), model: null, CancellationToken.None));

        // The retry warning and the failure error are both emitted, and neither names the state.
        Assert.Contains(recorder.Messages, m => m.Contains("Retrying", StringComparison.Ordinal));
        Assert.Contains(recorder.Messages, m => m.Contains("failed with status", StringComparison.Ordinal));

        foreach (string message in recorder.Messages)
        {
            Assert.DoesNotContain("SENSITIVE-CONTENT", message, StringComparison.Ordinal);
            Assert.DoesNotContain(TestClient.TestApiKey, message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task RetryAfterATransportFailure_IsLogged()
    {
        RecordingLoggerProvider recorder = new();
        ILoggerFactory factory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(recorder));
        ILogger logger = factory.CreateLogger("Jev.Sdk.Tests");

        StubTransport transport = new StubTransport()
            .EnqueueFailure()
            .EnqueueJson(TestClient.SuccessJson());

        using JevClient client = TestClient.Create(transport, logger: logger);

        await client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None);

        Assert.Contains(
            recorder.Messages,
            m => m.Contains("transport failure", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnreadableBody_IsLogged()
    {
        RecordingLoggerProvider recorder = new();
        ILoggerFactory factory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(recorder));
        ILogger logger = factory.CreateLogger("Jev.Sdk.Tests");

        StubTransport transport = new StubTransport().EnqueueJson("{ not json");

        using JevClient client = TestClient.Create(transport, logger: logger);

        await Assert.ThrowsAsync<JevConnectionException>(
            () => client.SystemOneAsync("text", TestClient.ThreeQuestions(), model: null, CancellationToken.None));

        Assert.Contains(recorder.Messages, m => m.Contains("could not be interpreted", StringComparison.Ordinal));
    }

    [Fact]
    public void AnswerConverter_RejectsAnUnsupportedConcreteType()
    {
        // A caller cannot legitimately construct this, but the converter must fail clearly
        // rather than silently writing nothing if one ever exists.
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Serialize<Answer>(new CustomAnswer(), JevJsonContext.Default.Options));
    }

    [Fact]
    public void QuestionConverter_RejectsAnUnsupportedConcreteType()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Serialize<Question>(new CustomQuestion(), JevJsonContext.Default.Options));
    }

    [Fact]
    public void QuestionConverter_WritesEveryModelledKind()
    {
        Question[] questions =
        [
            Question.Noul("is it?"),
            Question.Choice("pick", new Dictionary<string, string?>(StringComparer.Ordinal) { ["a"] = null }),
            Question.Score("rate", "low", "high"),
        ];

        foreach (Question question in questions)
        {
            string written = JsonSerializer.Serialize(question, JevJsonContext.Default.Options);

            using JsonDocument document = JsonDocument.Parse(written);
            Assert.Equal(question.Type, document.RootElement.GetProperty("type").GetString());
        }
    }

    [Fact]
    public void AnswerConverter_WritesEveryModelledKind()
    {
        Answer[] answers =
        [
            new NoulAnswer { Noul = 0.5 },
            new ChoiceAnswer
            {
                Choice = "a",
                Probabilities = new Dictionary<string, double>(StringComparer.Ordinal) { ["a"] = 1.0 },
                Confidence = 1.0,
            },
            new ScoreAnswer
            {
                Score = 1.0,
                Legend = new Dictionary<string, StructuredValue>(StringComparer.Ordinal) { ["0"] = StructuredValue.FromString("low") },
                Probabilities = new Dictionary<string, double>(StringComparer.Ordinal) { ["0"] = 1.0 },
                Confidence = 1.0,
            },
        ];

        foreach (Answer answer in answers)
        {
            string written = JsonSerializer.Serialize(answer, JevJsonContext.Default.Options);

            using JsonDocument document = JsonDocument.Parse(written);
            Assert.Equal(answer.Type, document.RootElement.GetProperty("type").GetString());
        }
    }

    [Fact]
    public void SerializingANoulAnswer_OmitsConfidenceEntirely()
    {
        // Omission rather than zero: a zero confidence would be indistinguishable from a real
        // one, and a caller thresholding on it would act on a value the API never reported.
        string written = JsonSerializer.Serialize(new NoulAnswer { Noul = 0.92 }, JevJsonContext.Default.Options);

        Assert.DoesNotContain("confidence", written, StringComparison.Ordinal);
    }

    [Fact]
    public void StructuredValue_FromJsonOfANullElementIsNull()
    {
        StructuredValue value = StructuredValue.FromJson(JsonDocument.Parse("null").RootElement);

        Assert.True(value.IsNull);
        Assert.Equal(StructuredValue.JsonShape.Null, value.Shape);
    }

    [Theory]
    [InlineData("true", StructuredValue.JsonShape.Flag)]
    [InlineData("42", StructuredValue.JsonShape.Number)]
    [InlineData("[1,2]", StructuredValue.JsonShape.Sequence)]
    [InlineData("\"text\"", StructuredValue.JsonShape.Text)]
    [InlineData("{}", StructuredValue.JsonShape.Record)]
    public void StructuredValue_DetectsEveryShape(string json, StructuredValue.JsonShape expected)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        Assert.Equal(expected, StructuredValue.FromJson(document.RootElement).Shape);
    }

    [Fact]
    public void EnvironmentProvider_DefaultsToTheApiKeyVariable()
    {
        EnvironmentApiKeyProvider provider = new();

        Assert.Equal(JevEnvironment.ApiKeyVariable, provider.VariableName);
    }

    [Fact]
    public void EnvironmentProvider_RejectsABlankVariableName()
    {
        Assert.Throws<ArgumentException>(() => new EnvironmentApiKeyProvider("  "));
    }

    [Fact]
    public void EnvironmentProvider_RejectsANullReader()
    {
        Assert.Throws<ArgumentNullException>(() => new EnvironmentApiKeyProvider("NAME", null!));
    }

    [Fact]
    public async Task EnvironmentProvider_HonoursCancellation()
    {
        EnvironmentApiKeyProvider provider = new("NAME", _ => "value");
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetApiKeyAsync(cancellation.Token));
    }

    [Fact]
    public void ConfigurationProvider_RejectsABlankKeyPath()
    {
        Microsoft.Extensions.Configuration.IConfiguration configuration =
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();

        Assert.Throws<ArgumentException>(
            () => new Jev.Sdk.DependencyInjection.ConfigurationApiKeyProvider(configuration, "  "));
    }

    [Fact]
    public async Task ConfigurationProvider_HonoursCancellation()
    {
        Microsoft.Extensions.Configuration.IConfiguration configuration =
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();

        Jev.Sdk.DependencyInjection.ConfigurationApiKeyProvider provider = new(configuration);
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetApiKeyAsync(cancellation.Token));
    }

    [Fact]
    public async Task ConfigurationProvider_TreatsWhitespaceAsAbsent()
    {
        Microsoft.Extensions.Configuration.IConfiguration configuration =
            new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Jev:ApiKey"] = "   ",
                })
                .Build();

        Jev.Sdk.DependencyInjection.ConfigurationApiKeyProvider provider = new(configuration);

        Assert.Null(await provider.GetApiKeyAsync(CancellationToken.None));
    }
}

/// <summary>A content whose read fails, used to reach the body-read failure path.</summary>
internal sealed class ThrowingContent : HttpContent
{
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        throw new IOException("the body could not be read");

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}

/// <summary>An answer kind the library does not model, used to reach the converter's default arm.</summary>
internal sealed class CustomAnswer : Answer
{
    public override string Type => "custom";
}

/// <summary>A question kind the library does not model, used to reach the converter's default arm.</summary>
internal sealed class CustomQuestion : Question
{
    public override string Type => "custom";
}
