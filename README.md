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

`GET /v1/models` is defined in the OpenAPI specification but absent from the prose reference. It is
the only way to learn which models your account can use, so the client implements it.

The model set is **open** — the API can add models at any time — so there is deliberately no enum.
Both of the vendor's SDKs type the model as a plain string for the same reason. The default is
`jev-latest`, which is their alias for the current flagship and resolves server-side, so a caller
gets a sensible model without naming one and it never goes stale.

```csharp
// Always a fresh request.
IReadOnlyList<ModelMetadata> models = await client.GetModelsAsync(cancellationToken);
```

### Cached reads

Asking "is model X available?" or "pick the best model" rarely needs a live answer, so there is a
lazily populated per-client cache:

```csharp
// First call fetches; later calls are served from the cache.
IReadOnlyList<ModelMetadata> models = await client.GetAvailableModelsAsync(cancellationToken);

if (await client.IsModelAvailableAsync("jev-2026-08", cancellationToken)) { /* ... */ }

string model = await client.ResolveModelAsync(preferred: null, cancellationToken);
```

Three properties are deliberate:

- **Nothing is fetched at construction.** `new JevClient()` performs no I/O and cannot fail on a
  network problem, a bad key, or a rate limit — in a place where you have no way to handle it.
- **The cache is per client, not process-wide.** `/v1/models` returns the models available to *your
  account*, so a shared cache would serve one account's list to another.
- **Concurrent first reads share one request.** Twenty callers asking at once produce one call, not
  twenty.

The cache lives for an hour by default; tune or disable it with `ModelCacheDuration`:

```csharp
new JevClientOptions { ModelCacheDuration = TimeSpan.FromMinutes(15) };  // shorter TTL
new JevClientOptions { ModelCacheDuration = TimeSpan.Zero };             // no caching
```

Invalidate it yourself when you know the server-side list changed:

```csharp
client.InvalidateModelCache();
```

### Warming the cache at startup

If your first request matters more than startup latency — a service whose first user should not pay
for a discovery call — warm it explicitly, where you can await and handle failure:

```csharp
using Jev.Sdk.DependencyInjection;

bool warmed = await JevClientWarmup.WarmAsync(client, logger, cancellationToken);
```

Or straight on the client, which reports failure as `false` rather than throwing, so best-effort
warmup needs no try/catch:

```csharp
if (!await client.WarmModelsAsync(cancellationToken))
{
    // Not fatal: the client will fetch on first use.
}
```

## Errors

Every error this library raises derives from `JevException`, so one catch covers them all:

| Exception | Raised when |
| --- | --- |
| `JevConfigurationException` | no API key could be resolved, or an option value is invalid |
| `JevRequestValidationException` | the request is locally invalid; no network call is made |
| `JevBadRequestException` | HTTP 400 — the request was malformed |
| `JevAuthenticationException` | HTTP 401 — the key is missing or invalid |
| `JevPermissionDeniedException` | HTTP 403 — the key is valid but not permitted |
| `JevNotFoundException` | HTTP 404 |
| `JevValidationException` | HTTP 422 — the API rejected the body; carries per-field details |
| `JevRateLimitException` | HTTP 429, after retries are exhausted; carries `Retry-After` |
| `JevServerException` | any 5xx, after retries are exhausted |
| `JevOverloadedException` | HTTP 529 specifically; derives from `JevServerException` |
| `JevApiException` | any other status |
| `JevConnectionException` | the exchange failed, or the response was unreadable |

`JevOverloadedException` derives from `JevServerException`, so one catch covers every server-side
condition. `JevRateLimitException` deliberately does not: a rate limit is the server declining to
serve rather than failing, and collapsing the two would make you report an outage when you should
slow down.

Every API exception carries `RequestId` (from the `x-typesafe-request-id` response header) and
`Endpoint`. Success responses carry `RequestId` too, via `JevResponse.RequestId` — logging a
successful call needs the same handle as reporting a failed one. Quote it when escalating to
TypeSafe support, since it is how they find the call.

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

## Throttling and retries

The defaults match TypeSafe's own SDKs, so a machine running the Python or JavaScript client
throttles the same way this one does:

| Setting | Default | Notes |
| --- | --- | --- |
| `MaxRetries` | 2 | after the initial attempt; 0 disables retrying |
| `InitialRetryDelay` | 500 ms | doubled each attempt |
| `MaxRetryDelay` | 5 s | ceiling on the computed backoff |
| `MaxRetryAfter` | 60 s | ceiling on a server-supplied delay |
| `RetryJitterFraction` | 0.25 | subtractive jitter |
| `Timeout` | 10 s | per attempt, not a whole-call deadline |

The timeout is enforced by the client with its own deadline rather than by mutating your
`HttpClient`. That matters if you pass a pooled, singleton, or factory-managed client:
`HttpClient.Timeout` cannot be assigned once the client has served a request, so a library that
sets it would fail on exactly the clients you are most likely to hand it. Yours is left untouched,
and an already-used client is accepted.

The body read is bounded too, so a server that returns headers promptly and then stalls the body
cannot hold a call open past the timeout. Cancelling your own `CancellationToken` still surfaces as
a cancellation, not as a timeout, and vice versa.

**Retryable statuses are 408, 429, and every 5xx.** TypeSafe's documented `529 Overloaded` is
covered by the 5xx range — retrying only 429 and 529 would leave a 500, 502, or 503 un-retried,
and that is how a transient outage becomes your user's outage. Nothing in the 4xx range is
retried: 400, 401, 403, 404, and 422 cannot be improved by repetition.

Jitter here is **subtractive**: each computed delay is reduced by up to a quarter, so a
1-second backoff lands between 750 ms and 1 s. It is deliberately not "full jitter", which
replaces the delay with a uniform random value from zero — that would shorten waits far more than
the vendor's SDKs do.

A server-supplied `Retry-After` or `retry-after-ms` takes precedence over the computed backoff.
The millisecond form wins when both are present, being more precise. A server delay beyond
`MaxRetryAfter` is discarded and the computed backoff used instead: waiting minutes inside one
call is worse than failing and letting you retry at your own level.

```csharp
using var client = new JevClient(new JevClientOptions
{
    MaxRetries = 5,
    InitialRetryDelay = TimeSpan.FromMilliseconds(250),
    MaxRetryDelay = TimeSpan.FromSeconds(10),
    MaxRetryAfter = TimeSpan.FromMinutes(2),
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

Two further variables are read, matching the vendor's SDKs:

| Variable | Effect |
| --- | --- |
| `TYPESAFE_BASE_URL` | overrides the default base address |
| `TYPESAFE_DEFAULT_MODEL` | overrides the default model |

These supply defaults only. A value set explicitly in code is never overridden by the
environment.

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

## Concurrency and performance

One client instance is safe to share across any number of concurrent callers, and that is the
intended usage.

```csharp
// Register once; share everywhere.
builder.Services.AddJevClient(builder.Configuration);
```

| Concern | How it is handled |
| --- | --- |
| Model cache | an immutable snapshot, read atomically, with no lock on the hit path |
| Cache refresh | a semaphore, so 20 concurrent first reads issue **one** request |
| Disposal | an `Interlocked` transition, so only one thread disposes an owned client |
| Serialization metadata | resolution cached per caller type, not rebuilt per call |
| Options | snapshotted and frozen at construction; no mutable state is shared |

Measured in-process against a stub transport, one instance: **21.7k calls/s sequentially** at 46 µs
per call, scaling to **200k calls/s at 64-way concurrency**. A bare `HttpClient` doing the same
exchange costs ~1.1 KB per call; a full `SystemOneAsync` adds ~4.6 KB on top, dominated by the
serialized request and the parsed response rather than by client overhead.

A note on honesty: two of the threading changes are **defensive**, not fixes for demonstrated
defects, and the requirements say so. Reintroducing the races does not fail the tests, because their
failure modes are not observable — a torn cache read costs at most one extra fetch. They are kept
because they are correct by construction. One change *is* a measured win: type-information
resolution used to build serializer options on every typed call.

## What this library does not do

- No synchronous API. Every I/O call is asynchronous, takes a required `CancellationToken`, and
  honours it through every await including retry delays.
- No streams, `IAsyncEnumerable`, or lazy sequences returned to callers. Every call materialises
  its result, so nothing disposable escapes.
- No file I/O in the core library. File-based configuration is loaded by the DI package at host
  startup, never inside a client constructor.
- No public event or subscription surface. Telemetry covers in-process observation.
- No network I/O at construction. Nothing is fetched until you call something, so building a client
  is infallible and instant.

## Repository layout

| Path | Contents |
| --- | --- |
| `src/Jev.Sdk/` | The client library |
| `src/Jev.Sdk.DependencyInjection/` | DI and configuration integration |
| `tests/Jev.Sdk.Tests/` | 206 tests, no network required |
| `samples/Jev.Sdk.Sample/` | A minimal console client |
| `samples/Game/` | Escape the Room: a demo game showing `Choice`, `Score` and `Noul` in one call, with its own tests |
| `samples/World/` | A schema-driven world engine: the same pattern as data. Loads a world from markdown, JSON, or a zip package, and plays it |
| `samples/HomeAssistant/` | A CLI sample that uses Jev to form a bounded command plan, then invokes a user-confirmed raw Home Assistant REST service call |
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
