# Jev.Sdk

A .NET client library for the [TypeSafe AI](https://typesafe.ai) System One API
(`https://api.typesafe.ai`).

Ask typed questions about a body of content and get structured, probability-backed answers back.
Every question is declared with a kind and answered within the constraints you supplied, so
nothing has to be parsed out of prose.

## Not affiliated

This is an independent client library. It is **not** affiliated with, sponsored by, or endorsed
by TypeSafe AI. "TypeSafe" and "System One" are used only to describe the API this library talks
to. For the API, the service, the models, or anything about accounts, billing, or uptime, contact
TypeSafe AI directly.

## Install

```
dotnet add package Jev.Sdk
```

For `Microsoft.Extensions.DependencyInjection` and configuration integration, also install:

```
dotnet add package Jev.Sdk.DependencyInjection
```

Neither package is published to nuget.org. The build workflow produces the `.nupkg` files as
downloadable artifacts on every push to `main`; see [Building](#building).

## Quick start

Set `TYPESAFE_API_KEY` in your environment, then:

```csharp
using Jev.Sdk;

using var client = new JevClient();

SystemOneResponse response = await client.SystemOneAsync(
    state: "Hi, I've been trying to connect my Stripe account for 3 days and it keeps failing.",
    questions: new Dictionary<string, Question>
    {
        ["is_urgent"] = Question.Noul("Does this convey urgency?"),
        ["department"] = Question.Choice(
            "Which team should handle this?",
            new Dictionary<string, string?>
            {
                ["billing"]   = "Payments, invoicing, refunds",
                ["technical"] = "Bugs, outages, integrations",
                ["sales"]     = "Pricing, upgrades, new accounts",
            }),
        ["frustration"] = Question.Score(
            "How frustrated is the customer?",
            "Calm", "Frustrated", "Very angry"),
    },
    model: null,                       // null uses "jev-latest"
    cancellationToken: cancellationToken);

double urgency     = response.Noul("is_urgent");        // 0.92
string department  = response.Choice("department");     // "technical"
double frustration = response.Score("frustration");     // 1.6
```

Three question kinds, each with the answer shape that kind produces:

| Question | Answers with |
| --- | --- |
| `noul` | a probability, 0 to 1 |
| `choice` | the selected option, every option's probability, and a confidence |
| `score` | a position on your scale, the rubric, probabilities, and a confidence |

A Noul answer deliberately carries **no** confidence: the probability already expresses the
model's certainty, and a second number beside it would invite treating the two as
interchangeable.

## Send many questions, not many requests

Questions in one request are evaluated against the same state, independently and in parallel.
Adding a speculative question whose answer only matters for some inputs costs almost nothing, so
put the whole decision tree in one call and let your code pick what it needs:

```csharp
if (response.Choice("department") == "billing"
    && response.Noul("refund_requested") > 0.7)
{
    RouteToBilling(ticketId, flagForReview: true);
}
```

## Confidence is for gating

`choice` and `score` answers report a confidence derived from their probability distribution:
concentrated means certain, flat means uncertain. Threshold on it to decide whether to act or to
ask a human. The value is reported exactly as received and is never recomputed.

```csharp
var answer = response["department"].AsChoice();

if (answer.Confidence < 0.6)
{
    EscalateForReview(ticketId);
}
else
{
    Route(answer.Choice);
}
```

## Structured state and instructions

The API accepts JSON structure wherever it accepts prose. Pass an object as the state, or
structure inside a question:

```csharp
// A caller-owned type, serialized through the caller's own source-generated context, so the
// library never reflects over your types.
await client.SystemOneAsync(
    state,
    CallerStateContext.Default.CallerState,
    questions,
    model: null,
    cancellationToken);
```

```csharp
// Structured instructions: the question itself can be a document.
["invoice_number_is_correct"] = Question.Noul(StructuredValue.FromJson(
    JsonDocument.Parse("""
        { "field": { "name": "invoice_number", "type": "string" },
          "expect": "matches the invoice header" }
        """).RootElement))
```

## Model discovery

`GET /v1/models` is defined in the OpenAPI specification but absent from the prose reference. It
is the only way to learn which models your account can use, so the client implements it:

```csharp
IReadOnlyList<ModelMetadata> models = await client.GetModelsAsync(cancellationToken);

foreach (ModelMetadata model in models)
{
    Console.WriteLine($"{model.Name} ({model.ParsedReleaseDate:yyyy-MM-dd}): {model.Description}");
}
```

## Errors

Every error this library raises derives from `JevException`, so one catch covers them all:

| Exception | Raised when |
| --- | --- |
| `JevConfigurationException` | no API key could be resolved, or an option value is invalid |
| `JevRequestValidationException` | the request is locally invalid; no network call is made |
| `JevAuthenticationException` | HTTP 401 — the key is missing or invalid |
| `JevValidationException` | HTTP 422 — the API rejected the body; carries per-field details |
| `JevRateLimitException` | HTTP 429, after retries are exhausted; carries `Retry-After` |
| `JevOverloadedException` | HTTP 529, after retries are exhausted |
| `JevApiException` | any other status |
| `JevConnectionException` | the exchange failed, or the response was unreadable |

```csharp
try
{
    var response = await client.SystemOneAsync(state, questions, null, cancellationToken);
}
catch (JevValidationException validation)
{
    foreach (ErrorDetails detail in validation.Details)
    {
        Console.WriteLine(detail);   // body.questions.urgency.score.criteria: Field required (missing)
    }
}
catch (JevRateLimitException rateLimited)
{
    Console.WriteLine($"Still limited; the server asked for {rateLimited.RetryAfter}");
}
```

Local validation runs before any network call and reports every problem at once, so a mistake
costs one round of feedback rather than several. A Score question with a single level is rejected
even though the API accepts it, because a one-level scale returns a constant.

## Retries

429, 529, and transport failures are retried with exponential backoff and jitter, bounded by
`MaxRetries`, and honouring `Retry-After` when the server sends one. 401 and 422 are never
retried: repeating them cannot change the answer.

```csharp
using var client = new JevClient(new JevClientOptions
{
    MaxRetries = 5,
    InitialRetryDelay = TimeSpan.FromMilliseconds(250),
    MaxRetryDelay = TimeSpan.FromSeconds(10),
});
```

## Dependency injection

```csharp
using Jev.Sdk.DependencyInjection;

builder.Services.AddJevClient(builder.Configuration);
```

The API key resolves from the highest-priority source that supplies one:

| Priority | Source |
| --- | --- |
| 1 | a key passed to the client or set on `JevClientOptions` |
| 2 | the `TYPESAFE_API_KEY` environment variable |
| 3 | `appSettings.{MACHINE_NAME}.json` |
| 4 | `appSettings.json` |

Environment beats every file; a machine-specific file overrides the generic one. The machine
token comes from the `MACHINE_NAME` environment variable when set, falling back to
`Environment.MachineName` — the override matters in containers, where the machine name is a
random id that changes when the container is recreated. Files are read once, at host startup, and
never watched, because the core library performs no file I/O.

Every seam is an interface with a working default, and registration uses `TryAdd`, so anything
you register first wins:

```csharp
builder.Services.AddSingleton<ITypeSafeTransport, MySyntheticTransport>();
builder.Services.AddJevClient(builder.Configuration);
```

The client can also be built directly with substituted collaborators, with no DI package at all:

```csharp
var client = new JevClient(
    options: new JevClientOptions { ApiKey = "test" },
    transport: new StubTransport(),
    apiKeyProvider: new StaticApiKeyProvider("test"),
    logger: NullLogger.Instance);
```

## Telemetry

The library emits Base Class Library telemetry only — `ILogger`, `ActivitySource`, and `Meter`.
It takes no dependency on OpenTelemetry or any exporter, because that would make it unusable to
consumers who chose a different one. Wire up whatever you already use:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource("Jev.Sdk"))
    .WithMetrics(metrics => metrics.AddMeter("Jev.Sdk"));
```

| Signal | Name |
| --- | --- |
| `ActivitySource` and `Meter` | `Jev.Sdk` |
| Spans | `systemone.call`, `models.list` |
| Counters | `jev.calls`, `jev.retries`, `jev.tokens.input`, `jev.tokens.output` |
| Histogram | `jev.call.duration` |

The caller's `state` is never logged, never a metric tag, and never attached to a span, at any
level. Question ids never become metric tags either: they are caller-supplied and unbounded, so
one would blow up a metrics backend.

## Serialization

`System.Text.Json` with source generation. Trimming and Native AOT work, and a missing type is a
build error rather than a runtime surprise.

Question ids are dictionary keys and are passed through **byte-identical** — no naming policy is
applied to them. This matters more than it sounds: a snake_case dictionary-key policy would
rewrite a caller's `isUrgent` to `is_urgent`, the API would answer under the new name, and the
caller would look for an answer that was never returned. The client refuses that configuration
rather than letting it corrupt a response.

Customisation is available and off by default:

```csharp
new JevClientOptions
{
    ConfigureJson = options => options.Converters.Add(new MyConverter()),
}
```

## Forward compatibility

The API will gain question and answer kinds. When it does, a working integration should not
break. An unmodelled kind arrives as `RawQuestion` or `UnknownAnswer` with its original JSON
intact, and the answers this library does understand are unaffected:

```csharp
if (response["sentiment"] is UnknownAnswer unknown)
{
    Console.WriteLine($"Unmodelled kind '{unknown.Type}': {unknown.RawJson}");
}
```

## What this library does not do

- No synchronous API. Every I/O call is asynchronous, takes a required `CancellationToken`, and
  honours it through every await including retry delays.
- No streams, `IAsyncEnumerable`, or lazy sequences returned to callers. Every call materialises
  its result, so nothing disposable escapes.
- No file I/O in the core library. File-based configuration is loaded by the DI package at host
  startup, never inside a client constructor.
- No public event or subscription surface. Telemetry covers in-process observation.

## Repository layout

| Path | Contents |
| --- | --- |
| `src/Jev.Sdk/` | The client library |
| `src/Jev.Sdk.DependencyInjection/` | DI and configuration integration |
| `tests/Jev.Sdk.Tests/` | 206 tests, no network required |
| `samples/Jev.Sdk.Sample/` | A minimal console client |
| `requirements/` | Requirements and locked design decisions |
| `docs/` | Verified API notes |

The client is split across partial classes by function, each file holding one concern:
`JevClient.cs` (construction), `.SystemOne.cs` (evaluation), `.Models.cs` (discovery),
`.Validation.cs` (local validation), `.Pipeline.cs` (send, retry, error mapping),
`.Conversions.cs` (typed readers). `HttpTypeSafeTransport` is split the same way.

## Building

```sh
dotnet build Jev.Sdk.slnx
dotnet test  Jev.Sdk.slnx
dotnet pack  Jev.Sdk.slnx --configuration Release --output ./artifacts/packages
```

Requires the .NET 10 SDK. The project targets `net10.0` only.

## Documentation

- [requirements/requirements.md](requirements/requirements.md) — requirements and locked decisions
- [docs/api-notes.md](docs/api-notes.md) — verified API facts, including four places where the vendor's prose docs and the OpenAPI specification disagree
- [CHANGELOG.md](CHANGELOG.md) — what has changed
- [SECURITY.md](SECURITY.md) — credential handling and what this library treats as sensitive

## Attribution

Authored by **deepseek-v4.1-flash** (via Hermes Agent), under deep human review.

The model drafted the requirements, the decision ledger, the API analysis, and the
implementation. Every decision recorded in [requirements/requirements.md](requirements/requirements.md)
was reviewed, challenged, and explicitly approved by a human before being locked. Where the model
made a recommendation the human disagreed with, the human's call is what the document records.

## License

MIT. See [LICENSE](LICENSE).
