# AGENTS.md — Jev.Sdk operating instructions for an AI agent

You are an AI agent working with **Jev.Sdk**, a .NET 10 client for the TypeSafe AI **System One**
API. Your job is to *use* this library correctly, not to reimplement it. Read this file before
writing any code against it.

**The one-sentence model:** you send a body of content plus a set of named, typed questions, and you
get back a named, typed, probability-backed answer for each — in a single HTTP call.

---

## 0. Rules that override everything else

1. **Every I/O method is `async` and requires a `CancellationToken` with no default.** Pass one.
   There is no sync surface. `new JevClient()` performs **no I/O**.
2. **Never pass a `CancellationToken.None`** unless you are genuinely writing a fire-and-forget
   script. Propagate the caller's token.
3. **Never put an API key in `Headers`.** `Authorization` is rejected at construction by design.
   Use `ApiKey`, `TYPESAFE_API_KEY`, or `IApiKeyProvider`.
4. **Never log, trace, or tag the `state` you send.** It is caller content and may be regulated.
   The library already refuses to do this; do not undo it.
5. **Question ids are dictionary keys and are passed through byte-identical.** Do not normalise,
   re-case, or rewrite them. The library rejects a JSON naming policy that would.
6. **One client, shared across all concurrent callers.** Register it as a singleton. Do not create
   one per request.
7. **Do not invent members, overloads, or exceptions.** Everything public is enumerated in §3, §4
   and §8 below. If it is not there, it does not exist.

---

## 1. What the library is and is not

| | |
| --- | --- |
| Package id | `Jev.Sdk` (core), `Jev.Sdk.DependencyInjection` (DI + file config) |
| Namespace | `Jev.Sdk` |
| Target | `net10.0` only |
| Base address | `https://api.typesafe.ai/v1/` |
| Endpoints | `POST /v1/systemone`, `GET /v1/models` — **two, and that is all** |
| Auth | `Authorization` header, from `TYPESAFE_API_KEY` or explicit key |
| Serialization | `System.Text.Json`, source-generated; trimming and Native AOT work |
| Telemetry | BCL only: `ILogger`, `ActivitySource`, `Meter` — no OpenTelemetry dependency |
| Not affiliated | Independent client. Not endorsed by TypeSafe AI. |

**Two endpoints.** If you need anything else, it does not exist in this API surface. Do not attempt
a workaround that scrapes or fabricates.

---

## 2. Minimal correct usage

```csharp
using Jev.Sdk;

using var client = new JevClient();          // no I/O; reads TYPESAFE_API_KEY at call time

SystemOneResponse response = await client.SystemOneAsync(
    state: "Hi, I've been trying to connect my Stripe account for 3 days and it keeps failing.",
    questions: new Dictionary<string, Question>
    {
        ["is_urgent"]   = Question.Noul("Does this convey urgency?"),
        ["department"]  = Question.Choice(
            "Which team should handle this?",
            new Dictionary<string, string?>
            {
                ["billing"]   = "Payments, invoicing, refunds",
                ["technical"] = "Bugs, outages, integrations",
                ["sales"]     = "Pricing, upgrades, new accounts",
            }),
        ["frustration"] = Question.Score(
            "How frustrated is the customer?", "Calm", "Frustrated", "Very angry"),
    },
    model: null,                              // null -> client default ("jev-latest")
    cancellationToken: cancellationToken);

double urgency     = response.Noul("is_urgent");        // 0.92   (probability)
string department  = response.Choice("department");     // "technical"
double frustration = response.Score("frustration");     // 1.6
double? conf       = response.Confidence("department"); // distribution-derived
string modelUsed   = response.Model;                    // what actually answered
int tokensIn       = response.Usage?.InputTokens ?? 0;  // what you pay for
```

**The single most important design choice: send many questions in one call.** Questions are
evaluated independently and in parallel against the same state. A speculative question costs almost
nothing. Put the whole decision tree in one request and branch on the answers in your own code:

```csharp
if (response.Choice("department") == "billing" && response.Noul("refund_requested") > 0.7)
{
    RouteToBilling(ticketId, flagForReview: true);
}
```

Do **not** make one call per question. That is the mistake this API shape exists to prevent.

---

## 3. Question kinds — pick the right one

Three kinds. The kind determines the answer's shape. Choose deliberately:

| Kind | Use when | Answer type | Answer shape |
| --- | --- | --- | --- |
| **Noul** | A yes/no proposition, or a probability you will threshold | `NoulAnswer` | `double Noul` (0–1) |
| **Choice** | Selecting exactly one from a set you define | `ChoiceAnswer` | `string Choice`, `IDictionary<string,double> Probabilities`, `double? Confidence` |
| **Score** | Rating along ordered levels you define | `ScoreAnswer` | `double Score`, `Legend`, `Probabilities`, `double? Confidence` |

### Factories (all on `Question`)

```csharp
// Noul
Question.Noul(string instructions)
Question.Noul(StructuredValue? instructions, NoulCriteria? criteria = null)

// Choice — Dictionary<string, string?>  (name -> description, null description allowed)
Question.Choice(string instructions, IDictionary<string, string?> options)
Question.Choice(StructuredValue? instructions, IDictionary<string, StructuredValue?> criteria)

// Score — levels in ascending order; position IS the level number, starting at 0
Question.Score(string instructions, params string[] levels)
Question.Score(StructuredValue? instructions, params StructuredValue[] criteria)
Question.Score(StructuredValue? instructions, IEnumerable<StructuredValue> criteria)
```

### Choosing rules an agent gets wrong

- **Noul's answer has no confidence, deliberately.** `response.Confidence(id)` returns `null` for a
  Noul answer. The probability *is* the certainty. Do not add a second threshold and treat them as
  interchangeable. If you need a threshold, threshold on the probability.
- **`Noul` = 0.5 means "equal weight", never "medium".** For a spectrum, use Score with defined levels.
- **Score levels: position is the level number.** Two or more levels are required; a single level is
  rejected locally because it returns a constant.
- **Choice: include an `"other"` option** when your option list might not cover every input. Without
  it the model is forced to pick a wrong option rather than signal uncertainty.
- **Choice criteria order is not significant** — it is not a ranked list. Use Score for ranking.
- **Score returns a probability-weighted average** and may fall *between* levels, e.g. `1.6`.
  `scoreAnswer.NearestLevelDescription()` gives the closest level description for display. The score
  itself remains authoritative.

---

## 4. Complete public surface

### `JevClient` — construction

```csharp
new JevClient()                                                   // env key, defaults
new JevClient(string? apiKey)
new JevClient(JevClientOptions? options)
new JevClient(JevClientOptions?, ITypeSafeTransport?, IApiKeyProvider?, ILogger?)  // seams
```

Properties: `DefaultModel`, `BaseAddress`, `MaxRetries`, `ModelCacheDuration`.
`Dispose()` — releases the transport; safe to call repeatedly.

### `JevClient` — evaluation (all `Task`, all require a token)

```csharp
Task<SystemOneResponse> SystemOneAsync(StructuredValue? state, IDictionary<string,Question>, string? model, CancellationToken)
Task<SystemOneResponse> SystemOneAsync(string state,           IDictionary<string,Question>, string? model, CancellationToken)
Task<SystemOneResponse> SystemOneAsync(JsonElement state,      IDictionary<string,Question>, string? model, CancellationToken)
Task<SystemOneResponse> SystemOneAsync<TState>(TState state, JsonTypeInfo<TState>,         IDictionary<string,Question>, string? model, CancellationToken)

// Typed response variants — deserialize into YOUR type instead of SystemOneResponse
Task<TResponse> SystemOneAsync<TResponse>(StructuredValue?, IDictionary<string,Question>, JsonTypeInfo<TResponse>, string? model, CancellationToken)
Task<TResponse> SystemOneAsync<TResponse>(string,           IDictionary<string,Question>, JsonTypeInfo<TResponse>, string? model, CancellationToken)
Task<TResponse> SystemOneAsync<TState,TResponse>(TState, JsonTypeInfo<TState>, IDictionary<string,Question>, JsonTypeInfo<TResponse>, string? model, CancellationToken)
```

### `JevClient` — model discovery

```csharp
Task<IReadOnlyList<ModelMetadata>> GetModelsAsync(CancellationToken)            // always fresh
Task<IReadOnlyList<ModelMetadata>> GetAvailableModelsAsync(CancellationToken)   // cached, 1h TTL
Task<bool>  IsModelAvailableAsync(string modelName, CancellationToken)          // ordinal, case-insensitive
Task<string> ResolveModelAsync(string? preferred, CancellationToken)            // never throws for a listing failure
Task<bool>  WarmModelsAsync(CancellationToken)                                  // best-effort; false, never throws
void        InvalidateModelCache()
```

### Answer readers (`SystemOneResponseExtensions`)

```csharp
double  response.Noul(string questionId)         // probability
string  response.Choice(string questionId)       // selected option
double  response.Score(string questionId)        // scale position
double? response.Confidence(string questionId)   // null for Noul

// Raw access
Answer  response[string questionId]              // throws KeyNotFoundException with the returned ids listed
IDictionary<string,Answer> response.AnswersOrEmpty  // never null
IDictionary<string,Answer>? response.Answers        // raw, may be null
```

Casts on `Answer`: `AsNoul()`, `AsChoice()`, `AsScore()` — each throws `InvalidCastException` naming
the actual kind if it does not match.

### `StructuredValue` — the anything-value

The API accepts JSON structure wherever it accepts prose: the state, a question's instructions, a
Choice option description, and a Score level description can each be a plain string **or** a JSON
object/array.

```csharp
StructuredValue.FromString(string?)               // null -> StructuredValue.Null
StructuredValue.FromJson(JsonElement)             // clones; safe after the document is disposed
StructuredValue.FromObject<T>(T?, JsonTypeInfo<T>)// your type, your source-generated context
StructuredValue.Null

value.Shape        // JsonShape: Null | Text | Record | Sequence | Number | Flag
value.IsNull
value.AsString()   // text when Shape==Text, else null
value.ToRawText()  // the string, or its raw JSON for other shapes

// Two implicit conversions, so you can often pass a string or JsonElement directly:
StructuredValue v = "plain text";
StructuredValue w = jsonElement;
```

---

## 5. Structured state — the highest-leverage feature

The API takes JSON structure as readily as prose. Prefer structure when your input already has it.

```csharp
// Your own type, serialized through YOUR source-generated context.
// The library never reflects over your types — this is what makes trimming/AOT work.
await client.SystemOneAsync(
    state,
    CallerStateContext.Default.CallerState,
    questions,
    model: null,
    cancellationToken);
```

```csharp
// Structured *instructions*: the question itself can be a document.
["invoice_number_is_correct"] = Question.Noul(StructuredValue.FromJson(
    JsonDocument.Parse("""
        { "field":  { "name": "invoice_number", "type": "string" },
          "expect": "matches the invoice header" }
        """).RootElement))
```

**Do not flatten structured input into prose before sending it.** That discards information the API
was designed to receive.

---

## 6. Typed responses

Use the `TResponse` overloads when you want a concrete type rather than the `answers` dictionary:

```csharp
sealed class BillingVerdict
{
    public Dictionary<string, Answer> Answers { get; set; } = new();
    public Usage? Usage { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

var verdict = await client.SystemOneAsync<BillingVerdict>(
    state, questions, BillingVerdictContext.Default.BillingVerdict, model: null, ct);
```

Notes an agent must know:
- Local request validation **still runs** (it depends only on the questions). **No response
  validation** is possible — the library does not know your shape.
- Add `[JsonExtensionData]` to your own model or you lose the forward-compatibility pass-through.
- Each answer's kind mirrors its question's kind.

---

## 7. Errors — the complete ladder

Every error derives from `JevException`. One catch covers everything.

| Exception | When | Retried? |
| --- | --- | --- |
| `JevConfigurationException` | no key resolvable; an option value invalid; bad `BaseAddress` | no |
| `JevRequestValidationException` | **locally** invalid; no network call made. `.Problems` lists every issue | no |
| `JevBadRequestException` | 400 | no |
| `JevAuthenticationException` | 401 | no |
| `JevPermissionDeniedException` | 403 | no |
| `JevNotFoundException` | 404 | no |
| `JevValidationException` | 422; `.Details` is `IReadOnlyList<ErrorDetails>` | no |
| `JevRateLimitException` | 429 after retries exhausted; `.RetryAfter` | yes (then throws) |
| `JevServerException` | any 5xx after retries | yes (then throws) |
| `JevOverloadedException` | 529 specifically; **derives from `JevServerException`** | yes (then throws) |
| `JevApiException` | any other status | no |
| `JevConnectionException` | transport failure, unreadable body, or a missing required member | connection: yes / malformed: no |
| `OperationCanceledException` | you cancelled | — |

**Key distinctions an agent must respect:**
- `JevRateLimitException` is **not** a `JevServerException`. A rate limit is the server declining to
  serve, not failing. Do not report it as an outage — slow down.
- `JevOverloadedException` **is** a `JevServerException`, so `catch (JevServerException)` covers
  both 529 and every other 5xx.
- `JevRequestValidationException` (local, a bug in your calling code) is distinct from
  `JevValidationException` (server, your view of the contract is stale). Never conflate them.
- `JevConnectionException.IsProtocolError` tells a contract violation (server responded with
  something unreadable, or omitted `model`/`answers`/`usage`) from a genuine connection failure.
  A protocol error is **not** retried.

**On every API exception:** `.RequestId` (from `x-typesafe-request-id`), `.Endpoint`, `.StatusCode`,
`.ResponseBody`. On a success, `JevResponse.RequestId` is populated too. Quote `RequestId` when
escalating to TypeSafe support — it is how they find the call, and it is **safe to log**.

```csharp
try
{
    var response = await client.SystemOneAsync(state, questions, null, ct);
}
catch (JevValidationException validation)          // server rejected the body
{
    foreach (ErrorDetails d in validation.Details)
        Console.WriteLine($"{d.LocationPath}: {d.Message} ({d.ErrorType})");
}
catch (JevRequestValidationException local)        // you built a bad request; nothing was sent
{
    foreach (string problem in local.Problems) Console.WriteLine(problem);
}
catch (JevRateLimitException limited)
{
    await Task.Delay(limited.RetryAfter ?? TimeSpan.FromSeconds(30), ct);
}
catch (JevServerException server)                  // covers 529 and all other 5xx
{
    logger.LogError("TypeSafe failed: {Status} {RequestId}", server.StatusCode, server.RequestId);
}
```

Local validation is fail-fast and reports **every** problem at once, so a fix costs one round trip,
not several.

---

## 8. Configuration

### Options (`JevClientOptions`) with defaults

| Property | Default | Notes |
| --- | --- | --- |
| `ApiKey` | `null` | highest-priority key source; absence is normal |
| `BaseAddress` | `https://api.typesafe.ai/v1/` | overridable by `TYPESAFE_BASE_URL` |
| `DefaultModel` | `"jev-latest"` | overridable by `TYPESAFE_DEFAULT_MODEL` |
| `Timeout` | 10 s | **per attempt**, not a whole-call deadline |
| `MaxRetries` | 2 | after the first attempt; 0 disables |
| `InitialRetryDelay` | 500 ms | doubled each attempt |
| `RetryBackoffMultiplier` | 2.0 | |
| `MaxRetryDelay` | 5 s | cap on *computed* backoff |
| `MaxRetryAfter` | 60 s | cap on a *server-supplied* delay |
| `RetryJitterFraction` | 0.25 | **subtractive** jitter: 1 s → 0.75–1 s |
| `ValidateRequests` | `true` | local validation before any network call |
| `ModelCacheDuration` | 1 h | 0 disables caching |
| `Headers` | empty | `Authorization` is refused here |
| `ConfigureJson` | `null` | may add converters; **must not** change dictionary-key naming |

The timeout is enforced with a linked token, **not** by setting `HttpClient.Timeout` — so you may
pass a pooled, singleton, or factory-managed `HttpClient` (an already-used one is accepted).

### Retry behaviour

Retryable: **408, 429, and every 5xx.** Never retried: 400, 401, 403, 404, 422 (repetition cannot
improve them). A server `Retry-After` / `retry-after-ms` wins over the computed backoff; the
millisecond form wins when both exist; a delay beyond `MaxRetryAfter` is discarded in favour of the
computed backoff.

### API key resolution (highest priority first)

| # | Source |
| --- | --- |
| 1 | key passed to the constructor / set on `JevClientOptions` |
| 2 | `TYPESAFE_API_KEY` environment variable |
| 3 | `appSettings.{MACHINE_NAME}.json` — **DI package only** |
| 4 | `appSettings.json` — **DI package only** |

The core library does **no file I/O**. The DI package loads files once at host startup, never
watched. `MACHINE_NAME` overrides the machine token — necessary in containers, where
`Environment.MachineName` is a random per-start id.

Configuration keys live under the **`Jev`** section: `Jev:ApiKey`, `Jev:BaseAddress`,
`Jev:DefaultModel`, `Jev:Timeout`.

```json
{ "Jev": { "ApiKey": "…", "BaseAddress": "https://api.typesafe.ai/v1/", "DefaultModel": "jev-latest" } }
```

### Dependency injection

```csharp
using Jev.Sdk.DependencyInjection;

builder.Services.AddJevClient(builder.Configuration);        // or AddJevClient() for env only
```

Registration is `TryAdd` throughout, so **anything you register first wins**:

```csharp
builder.Services.AddSingleton<ITypeSafeTransport, MySyntheticTransport>();
builder.Services.AddJevClient(builder.Configuration);       // respects your transport
```

Warm the model cache at **your** startup where you can await and handle failure — never automatic:

```csharp
bool warmed = await JevClientWarmup.WarmAsync(client, logger, cancellationToken);
// both `logger` and `cancellationToken` are required; pass null for the logger if you have none
```

---

## 9. Model discovery

`GET /v1/models` is in the OpenAPI spec but absent from the prose reference. It is the only way to
learn which models your account can use.

- **There is deliberately no model enum.** The set is open; both vendor SDKs type it as a string.
- Default `"jev-latest"` is an alias resolving server-side, so it never goes stale. Prefer it.
- The cache is **per client, not process-wide**, because the list is per *account*. A shared cache
  would serve one account's models to another.
- The cache is **lazy** — nothing is fetched at construction, ever.
- Concurrent first reads share **one** request (double-checked under a semaphore).
- A failed refresh does **not** poison the cache: the previous list stays servable and the exception
  propagates.
- `ModelMetadata.ReleaseDate` is kept **verbatim** from the API. The spec documents `YYYY-MM-DD` but
  the live service returns a full ISO-8601 timestamp. Use `ParsedReleaseDate` (a `DateOnly?`) to read
  the date; it handles both shapes and returns `null` rather than throwing. Only the date component
  is used, so the offset never shifts the day.

```csharp
IReadOnlyList<ModelMetadata> models = await client.GetAvailableModelsAsync(ct);
if (await client.IsModelAvailableAsync("jev-2026-08", ct)) { /* … */ }
string model = await client.ResolveModelAsync(preferred: null, ct);
```

`ResolveModelAsync` and `WarmModelsAsync` **do not throw** for a listing failure — they return a
usable default / `false`. Cancellation still propagates, because a cancelled host wants to know.

---

## 10. Forward compatibility (do not defeat this)

The API will gain question and answer kinds and response fields. A working integration must not
break, and this library is built so it does not:

```csharp
if (response["sentiment"] is UnknownAnswer unknown)
{
    Console.WriteLine($"Unmodelled kind '{unknown.Type}': {unknown.RawJson}");
}

if (response.AdditionalProperties?.TryGetValue("new_field", out JsonElement raw) == true) { /* … */ }
```

- An unmodelled **answer** kind arrives as `UnknownAnswer` with its full JSON intact; the answers
  you do understand are unaffected.
- An unmodelled **response field** is preserved in `AdditionalProperties` (`[JsonExtensionData]`) on
  `JevResponse`, `Answer` and `Question`.
- An unmodelled **question** arrives as `RawQuestion` — and **cannot be sent**, because the library
  does not model that kind. Local validation rejects it with a clear message. Do not try to
  round-trip a `RawQuestion` into a request.

**Do not** write `switch` expressions that assume only three answer kinds exist, and do not treat an
`UnknownAnswer` as an error.

---

## 11. Concurrency and performance

One instance is safe for any number of concurrent callers and that is the intended usage.

| Concern | Handling |
| --- | --- |
| Model cache | immutable snapshot, atomic publish, no lock on the hit path |
| Cache refresh | semaphore → 20 concurrent first reads issue **one** request |
| Disposal | `Interlocked`-style single transition |
| Serialization metadata | resolved and cached per caller type, not rebuilt per call |
| Options | snapshotted and frozen at construction |

Measured in-process against a stub transport: **21.7k calls/s sequentially** (46 µs/call), scaling to
**200k calls/s at 64-way concurrency**.

Rules: **register once, share everywhere.** Do not create a client per request. Do not mutate
`JevClientOptions` after constructing a client — the client holds a frozen copy and your mutation has
no effect.

---

## 12. Telemetry

BCL only, so you wire up whatever sink you already use. The library is silent when nobody listens.

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource("Jev.Sdk"))
    .WithMetrics(m => m.AddMeter("Jev.Sdk"));
```

| Signal | Name |
| --- | --- |
| `ActivitySource` / `Meter` | `Jev.Sdk` |
| Spans | `systemone.call`, `models.list` |
| Counters | `jev.calls`, `jev.retries`, `jev.tokens.input`, `jev.tokens.output` |
| Histogram | `jev.call.duration` |
| Span tags | `jev.model`, `jev.questions.count`, `jev.retries`, `jev.status_code`, `jev.tokens.input`, `jev.tokens.output`, `jev.request_id` |
| Outcome tag values | `success`, `error`, `cancelled` |

**Never recorded, at any level:** the caller's `state`, question ids (caller-supplied and unbounded —
one would explode metrics cardinality), the API key, or the `Authorization` header. What *is*
recorded is shape and outcome. `RequestId` is safe to record and is the handle for support.

---

## 13. Serialization rules

`System.Text.Json` with source generation. **Question ids are dictionary keys and are passed through
byte-identical** — no naming policy is applied to them. A snake_case dictionary-key policy would
rewrite your `isUrgent` to `is_urgent`, the API would answer under the new name, and you would look
for an answer that was never returned. The client **refuses** that configuration rather than letting
it corrupt a response.

`ConfigureJson` is available and off by default; it may add converters and change property naming
for the client's own types, but not dictionary-key naming.

Your own state types are serialized through **your** `JsonTypeInfo<T>` — the library never reflects
over your types. That is what keeps it trim- and AOT-safe.

---

## 14. Things this library does not do

Do not attempt any of these; they are deliberate omissions, not gaps to work around.

- **No synchronous API.** Everything is `async`, every I/O call takes a required `CancellationToken`.
- **No streams, `IAsyncEnumerable`, or lazy sequences returned.** Every call materialises its result,
  so nothing disposable escapes.
- **No file I/O in the core library.** File config is the DI package's job, at host startup.
- **No public event or subscription surface.** Telemetry covers in-process observation.
- **No network I/O at construction.** Building a client is infallible and instant.
- **No model enum.** The model set is open.
- **Only two endpoints.** `POST /v1/systemone` and `GET /v1/models`.

---

## 15. Agent checklists

### Before sending a request

- [ ] State is non-null, and is **structured** if the input has structure
- [ ] At least one question; no duplicate ids
- [ ] Question ids are meaningful and stable (you read answers back by that exact key)
- [ ] Noul: instructions state the proposition. Threshold on the probability, not a "confidence"
- [ ] Choice: every option listed, plus `"other"` if the list might not cover the input
- [ ] Score: **≥ 2 levels**, in ascending order
- [ ] A `CancellationToken` is passed and is the caller's, not `None`
- [ ] `model: null` unless you have a specific reason to pin one

### After receiving a response

- [ ] Read answers by the **same ids** you sent
- [ ] `response.Model` — record it; the alias may have resolved to a dated model
- [ ] `response.Usage?.InputTokens` — input tokens are what you pay for
- [ ] `response.RequestId` — log it so a failure is traceable
- [ ] Handle `UnknownAnswer` gracefully rather than assuming three kinds
- [ ] Never log the state

### Before shipping

- [ ] Client registered as a **singleton**, not per-request
- [ ] `TYPESAFE_API_KEY` from the environment or a secret store — **never** hard-coded, never in `Headers`
- [ ] Retry/backoff left at the defaults unless you measured a reason to change them
- [ ] `JevRateLimitException` handled as slow-down, not as an outage
- [ ] `JevServerException` catch covers 529 (it derives from it)
- [ ] `JevRequestValidationException` treated as a *bug in your code*, distinct from a 422
- [ ] Model cache warmed at startup only if first-request latency matters
- [ ] Telemetry source `Jev.Sdk` wired into your collector

---

## 16. Quick reference — id → answer

| You want | Send | Read |
| --- | --- | --- |
| Is it true? | `Question.Noul("…")` | `response.Noul("id")` → `double` 0–1 |
| Which one? | `Question.Choice("…", options)` | `response.Choice("id")` → `string` |
| How much? | `Question.Score("…", a, b, c)` | `response.Score("id")` → `double` |
| How sure (Choice/Score)? | same | `response.Confidence("id")` → `double?`, `null` for Noul |
| Full distribution | same | `response["id"].AsChoice().Probabilities` |
| Score level's meaning | same | `response["id"].AsScore().Legend`, `.NearestLevelDescription()` |
| Unmodelled kind | — | `is UnknownAnswer u` → `u.Type`, `u.RawJson` |
| Cost | any | `response.Usage?.InputTokens` |

---

## 17. Where to read more

- `README.md` — the full user-facing guide, with the reasoning behind each decision
- `requirements/requirements.md` — requirements and the locked decision ledger (D1–D37)
- `docs/api-notes.md` — verified wire facts, including four places the vendor's prose and the
  OpenAPI specification disagree, and defects found by probing semantics rather than names
- `samples/` — runnable: `Jev.Sdk.Sample` (both endpoints by hand), `Game/` (all three question kinds
  in one call), `World/` (the same pattern as data), `HomeAssistant/` (bounded command plan, then a
  user-confirmed action)
- `CHANGELOG.md`, `SECURITY.md`

**When the live service and the specification disagree, the service is the fact** — and the
divergence belongs in `docs/api-notes.md`. That is a recorded decision (D36), not a preference.
