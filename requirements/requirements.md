# Jev.Sdk — Requirements & Design Decisions

Status: **discussion phase, not signed off**
Created: 2026-09-18
Scope: a .NET client library for the TypeSafe AI System One API.

This document records the agreed requirements and locked decisions for the
`Jev.Sdk` client library, plus the items still open. It is the source of truth
for what the library must do and why.

---

## 1. Purpose

Build a .NET client for the TypeSafe AI System One API
(`https://api.typesafe.ai`). The API evaluates a `state` against a map of typed
`questions` and returns structured `answers`, one per question.

Two official SDKs exist (Python, JavaScript/TypeScript). No official .NET SDK
exists. Two independent community .NET clients were found during discovery;
neither is affiliated with TypeSafe AI. This project is an independent client.

---

## 2. Scope

**In scope**

- `POST /v1/systemone` — the evaluation endpoint.
- `GET /v1/models` — model and alias discovery. (Documented in the OpenAPI spec
  but absent from `api.md`; it is required to discover models.)
- The three question kinds (`noul`, `choice`, `score`) and their three answer
  kinds.
- Typed error mapping for 401, 422, 429, and 529.
- Retry with exponential backoff and `Retry-After` handling.
- BCL telemetry (logging, tracing, metrics).
- Optional dependency-injection integration in a separate package.

**Out of scope**

- Anything requiring an account, billing, or management API — none is documented.
- Streaming. The API is request/response; there is no event feed to subscribe to.
- Any second API version. Only `v1` was observed.

---

## 3. Functional Requirements

### R1 — Evaluation

`SystemOneAsync` sends a state and a named map of questions, and returns the
answers keyed by the caller's own question ids. Answer ids must survive
round-trip byte-identical; the client must not normalise, re-case, or rewrite
question keys.

### R2 — Model discovery

The client exposes model listing so callers can discover available model names and
aliases rather than hardcoding `jev-latest`. Returns model name, description, and
release date.

There is deliberately no model enum. The set is open — the API can add models at
any time — and both vendor SDKs type the model as a plain string. An enum would
model a closed set and would fail on a new model without a new library version.
The default remains `jev-latest`, the vendor's alias for their current flagship,
which resolves server-side and therefore cannot go stale.

A lazily populated, per-client cache serves repeated availability questions without
a request each. Three constraints shape it:

1. Nothing is fetched at construction. The constructor performs no I/O and cannot
   fail on a network problem, a bad key, or a rate limit, in a place a caller has
   no way to handle.
2. The cache is scoped to one client. `/v1/models` returns the models available to
   the authenticated account, so a process-wide cache would serve one account's
   model list to another.
3. Concurrent first reads share a single request, so a service that fans out on
   startup does not issue one request per caller.

The cache has a configurable lifetime (default one hour, zero disables it), can be
invalidated explicitly, and a failed refresh propagates without discarding a
previously good list.

Warming at startup is opt-in rather than automatic. A host that wants it calls
`WarmModelsAsync`, or `JevClientWarmup.WarmAsync` from the DI package, at its own
startup where it can await and handle failure. The client never does it itself.

### R3 — Question types

Three question kinds, each with its own criteria shape:

| Kind | `criteria` shape | Answer carries |
| --- | --- | --- |
| `noul` | optional `{ true, false }` descriptions | `noul` probability only — **no confidence** |
| `choice` | required map of option → description-or-null | `choice`, `probabilities`, `confidence` |
| `score` | required ordered array of level descriptions | `score`, `legend`, `probabilities`, `confidence` |

### R3a — Null as a first-class shape

A JSON `null` inside a collection is a value, not an absence, and must never crash a read.

`JsonConverter<T>.HandleNull` defaults to `false` for reference types, which makes the serializer
handle a null token itself and skip the converter entirely. The consequence is that a null inside a
map or array becomes a C# null rather than the library's own null value, and reading it throws.
Every converter here therefore declares `HandleNull => true` and takes responsibility for its own
null tokens.

The same property is what makes `JsonElementBox` meaningful: without it, a member that is present and
null is indistinguishable from one that is absent, which is the distinction the type exists to
preserve. `ValidationError.input` has no declared type, so null is a legal value for it.

Computed members — `Usage.TotalTokens`, `ModelMetadata.ParsedReleaseDate`,
`ErrorDetails.LocationPath` — are `[JsonIgnore]`, because they are calculated here rather than
reported by the API, and a payload that echoes invented fields is a protocol deviation.

### R3b — Nullable wire members

Any member the API may send as JSON null must have a never-null accessor for reading, and must
still be written on serialization.

Two members have this shape and both are handled the same way now:

| Member | Never-null read | Wire behaviour | Absence on a 200 |
| --- | --- | --- | --- |
| `ModelListResponse.Models` | `ModelsOrEmpty` | written from the nullable view | tolerated: the model methods document a benign fallback |
| `SystemOneResponse.Answers` | `AnswersOrEmpty` | written from `AnswersForWire`, so a required member is always emitted | protocol error |

Members the specification declares **required and non-nullable** are enforced on a successful response:
a 200 that omits or nulls one is reported as `JevConnectionException` with `IsProtocolError: true`,
which is the same treatment an empty or non-JSON body already receives. This client never sends a
request with no questions, so an empty answer map can only mean the server did not answer the
questions that were sent — a contract violation, not an empty result. The model-list path is the one
documented exception, because those methods promise a usable answer either way.

`model` is checked for emptiness rather than null, because the property is a non-nullable string
initialised to `""`, so an absent member deserializes to `""`. A nullable property would push a null
check onto every caller for a case that cannot occur through this client.

The rule exists because a null from either produced a failure outside the documented set. For
`answers` it was worse than an exception: because `WhenWritingNull` is in force, re-serializing a
null dropped the member, emitting a body that omits a field the specification declares required.

When adding a member of this shape, apply the guard to every member of the shape rather than to the
one that was reported. The first pass fixed `models` and missed `answers`.

### R3c — Dated captures of the vendor documents

Any claim that the library diverges from the vendor's documentation must be checkable against the
exact document text it was based on.

`requirements/vendor/` holds byte-identical dated captures of the vendor's prose reference
(`https://docs.typesafe.ai/api.md`), each with a provenance sidecar recording the fetch time, response
headers, byte count, and SHA-256. Captures are additive: an existing file is never overwritten or
corrected, so the set remains a record of what was documented on each date, and a later change is
visible as a diff between two dated copies rather than as a silently updated file.

The machine-readable specification (`https://api.typesafe.ai/openapi.json`) governs where the two
disagree, because it is the artifact the service validates against. The prose reference is captured
because it carries behavioural guidance the specification omits, because it is what a human reviewer
reads, and because it is the document the vendor is most likely to edit.

### R3d — Live integration tests

Behaviour the unit suite cannot verify must be verified against the live service.

The unit suite tests the client against a stub, so it confirms only that the client agrees with our model
of the service. It cannot tell us the model is wrong. `tests/Jev.Sdk.IntegrationTests` exists to test the
model itself, and is the only project in the repository that calls the real API.

- Live tests **skip** when no API key is configured, naming both places a key can come from. They never
  fail for a missing key, and never pass without one: a green tick for a call that never happened would
  report coverage of behaviour that was never exercised.
- The key comes from the library's own configuration path — `TYPESAFE_API_KEY`, then
  `appSettings.Local.json` — resolved by passing the machine token `Local` to
  `JevConfigurationLoader.Build`, so the familiar file name works through the documented machine-override
  mechanism rather than a special case.
- A configured value that is still the placeholder `REPLACE ME` counts as not configured, so an
  unedited copy of the template is harmless.
- Assertions state behaviour that must hold, and each is written to fail on drift rather than merely on
  an exception. Where an obvious case exists, it is pinned, so a model that stopped understanding a
  question is a failure rather than a passing range check.

The suite is deliberately small: it spends real quota, so it detects drift rather than re-testing every
branch against a paid endpoint.

### R4 — Structured values

`state`, question `instructions`, Choice option descriptions, Score levels, Noul
criteria members, and `legend` values all accept JSON structure — a string, an
object, or an array — not just text. The client supports every shape on both the
request and the response side.

The full declared footprint is reproduced property by property in
[docs/api-notes.md](../docs/api-notes.md) section 6b, derived from the live
OpenAPI specification. A conformance suite exercises each declared shape so that
an unsupported one fails in the tests rather than in a caller's code.

### R5 — Caller-owned state

Callers must be able to pass their own strongly typed state object and have it
serialize without the library reflecting over their types. Required for trimming
and Native AOT.

### R6 — Forward compatibility

An unknown question or answer `type` must not fail the response. Unmodelled
fields must be preserved and reachable, and a new answer kind must not affect the
other answers in the same payload.

### R7 — Error mapping

| Status | Surface |
| --- | --- |
| 401 | authentication failure |
| 422 | validation failure, with per-field details (`loc`, `msg`, `type`) |
| 429 | rate limited, with `Retry-After` when present |
| 529 | service overloaded |

A malformed or unparseable response body is a protocol error, distinct from a
422 validation failure.

Statuses map to these types, matching the vendor's exception surface:

| Status | Type |
| --- | --- |
| 400 | `JevBadRequestException` |
| 401 | `JevAuthenticationException` |
| 403 | `JevPermissionDeniedException` |
| 404 | `JevNotFoundException` |
| 422 | `JevValidationException`, with per-field details |
| 429 | `JevRateLimitException`, with `RetryAfter` |
| 5xx | `JevServerException`; 529 additionally as `JevOverloadedException`, which derives from it |
| transport / timeout | `JevConnectionException` |

`JevOverloadedException` derives from `JevServerException`, so one catch covers
every server-side condition. `JevRateLimitException` deliberately does **not**:
a rate limit is the server declining to serve rather than failing, and collapsing
the two would make a caller report an outage when it should slow down.

Every API exception carries `RequestId`, from the `x-typesafe-request-id`
response header, and `Endpoint`, the method and URL without credentials. The
request id is also attached to successful responses via `JevResponse.RequestId`,
because a caller logging a success needs the same handle as one reporting a
failure. It is a bounded server-supplied identifier, so unlike caller content it
is safe to log and safe to attach to a span.

### R8 — Retries

Retryable statuses are **408, 429, and every 5xx**. TypeSafe's documented 529
(Overloaded) is covered by the 5xx range rather than named separately; retrying
only 429 and 529 would have left 500, 502, and 503 un-retried, which is how a
transient outage becomes a caller-visible failure. A 4xx is never retried: 400,
401, 403, 404, and 422 cannot be improved by repetition.

Backoff is exponential from `InitialRetryDelay`, bounded by `MaxRetryDelay`,
with **subtractive** jitter: each computed delay is reduced by a random fraction
no greater than `RetryJitterFraction`. This is deliberately not "full jitter",
which replaces the delay with a uniform random value from zero.

A server-supplied `Retry-After` or `retry-after-ms` takes precedence over the
computed backoff, and is bounded by `MaxRetryAfter`. A server delay longer than
that is discarded and the computed backoff used instead, because a caller
waiting minutes inside one call would rather fail and retry at its own level.

Retry delay must be cancellable. Defaults match the vendor's SDKs so that a
caller moving between the Python, JavaScript, and .NET clients gets the same
throttling behaviour.

### R9 — Client-side validation

Fail fast, before any network call, on: missing required fields, a Score with
fewer than two levels, an empty question map, and a missing API key. A failure of
this kind must not be reported as a server error.

### R10 — Configuration

API key resolution, highest priority first:

1. API key passed to the constructor or set in options — optional; its absence is normal, not an error.
2. `TYPESAFE_API_KEY` environment variable.
3. `appSettings.{MACHINE_NAME}.json`.
4. `appSettings.json`.
5. None present → configuration exception at construction, naming the sources checked.

Two further environment variables are honoured, matching the vendor's SDKs so a
machine already configured for the Python or JavaScript client needs no change:

| Variable | Effect |
| --- | --- |
| `TYPESAFE_BASE_URL` | Overrides the default base address |
| `TYPESAFE_DEFAULT_MODEL` | Overrides the default model |

These supply defaults only. A value set explicitly in code is never overridden by
the environment.

Environment beats every file. Machine-specific beats generic. If a file is used,
it is read once at host startup and never watched for changes.

### R11 — Telemetry

The library emits BCL telemetry only:

- `ILogger<T>`, constructor-injected, optional, silent when absent.
- `ActivitySource` — one span per evaluation call, carrying model, question
  counts, status, retry count, and token usage.
- `Meter` — call counts by outcome, retry counts, call duration, and token
  consumption.

The server's request id is attached to the span as `jev.request_id`, because it
is the one value that lets an operator find a specific call in TypeSafe's own
logs. It is a bounded server-supplied identifier, so unlike caller state it is
safe to record.

No dependency on OpenTelemetry or any exporter. No public event or subscription
surface.

### R11a — Request headers

Callers may supply additional HTTP headers, sent with every request. Both of the
vendor's SDKs accept extra request headers, and without an equivalent a caller
behind a proxy or gateway that requires its own header could not use this client at
all. Caller headers are applied after the library's own, so a default such as
`Accept` can be overridden. `Authorization` cannot be set this way: attempting it
is a configuration error, because a credential must arrive through `ApiKey` or an
`IApiKeyProvider` rather than as an ordinary string.

### R12 — Extensibility seams

The client's collaborators are interfaces with working defaults, so the library
is testable and replaceable without the DI package:

| Seam | Default |
| --- | --- |
| `ITypeSafeTransport` | the built-in HTTP implementation |
| `IApiKeyProvider` | the priority ladder above |
| `ILogger<T>` | none (silent) |

Callers may construct the client with defaults, or inject any seam explicitly.
The DI package is a convenience layer, not the only route.

---

## 4. Cross-Cutting Requirements

### R11b — Thread safety and scalability

A single client instance may be used concurrently by any number of callers.

| Concern | Approach |
| --- | --- |
| Model cache | One immutable snapshot published by a single reference write, read atomically. No lock on the hit path. |
| Cache refresh | A semaphore so N concurrent first reads issue one request. Only the acquired path releases it, so a caller cancelled while queued cannot corrupt the gate. |
| Disposal | An `Interlocked` transition, so only the thread that wins disposes the owned client. |
| Serialization | Type-information resolution is cached per caller resolver and response type. A `JsonTypeInfo` is immutable and thread-safe once configured. |
| Client state | Options are snapshotted and frozen at construction. No mutable per-call state is shared. |
| Transport | `HttpClient` is designed for concurrent use; the transport adds no lock around send. |

Measured on this machine, one client instance, in-process stub transport: sequential
throughput 21.7k calls/s at 46 µs per call, scaling to 200k calls/s at 64-way
concurrency. A bare `HttpClient` performing the same exchange costs 1,112 bytes per
call; a full `SystemOneAsync` costs roughly 4.6 KB above that, dominated by the
serialized request, the parsed response dictionary, and the answer union — not by
client overhead.

Two of the threading changes are **defensive rather than fixes for demonstrated
defects**, and this is recorded rather than implied:

- The model cache previously wrote its list and its timestamp as two independent
  fields, so a reader could observe a torn pair. Every consequence is benign — one
  list served slightly longer than configured, or one harmless extra fetch. A test
  that reintroduces the race does not fail, because the failure is not observable.
  The snapshot is kept because it is correct by construction rather than by arguing
  about which interleavings survive.
- Disposal previously used a plain bool with a check-then-act. `HttpClient.Dispose`
  is idempotent and a racing send raises `ObjectDisposedException`, which is a
  documented outcome. `Interlocked` is still the right shape, but no test here proves
  a defect existed.

One change is a measured win: binding a response to a caller's own type previously
built a fresh `JsonSerializerOptions` per response, discarding the serializer's
metadata cache on every call. That resolution is now cached.

### R12a — Timeouts

The per-attempt timeout is enforced by the client, not by mutating an
`HttpClient` the caller owns.

`HttpClient.Timeout` cannot be assigned once the client has started a request. A
pooled, singleton, or factory-managed client is a normal thing for a caller to
supply — it is what `IHttpClientFactory` hands back — so assigning that property in
the transport constructor would throw `InvalidOperationException` on any client that
had already served a request. This was a real defect, found by probing rather than
by reading.

Instead each attempt runs under a linked `CancellationTokenSource` with its own
deadline. That gives four properties, each of which is asserted by a test against
observed wall-clock behaviour rather than against configuration:

1. The deadline fires. Setting a value nothing reads is the classic version of this
   bug.
2. It is **per attempt**, not a whole-call budget. Three retries of two seconds each
   is a six-second call.
3. The caller's cancellation stays distinguishable from a timeout. A caller that
   cancelled must not be told it hit a network failure, and vice versa.
4. A caller-supplied `HttpClient` is never mutated, so its own timeout is left alone
   and an already-used client is accepted.

The body read is bounded by the same deadline. With `ResponseHeadersRead` the send
returns as soon as headers arrive, so a server that sends headers promptly and then
stalls the body would otherwise hold the call open past the configured timeout.

A zero or negative timeout means no per-attempt deadline, leaving the client's own
timeout in effect, rather than a token that cancels immediately and fails every call.

### R13 — Asynchronous I/O only

- Every public I/O method returns `Task<T>` and accepts a `CancellationToken`.
- The token is honoured through every await, **including retry delays**.
- No synchronous counterparts and no sync wrappers. Exactly one way to call.
- No sync-over-async anywhere: no `.Result`, no `.Wait()`, no `GetAwaiter().GetResult()`.
- No streams, no `IAsyncEnumerable`, no lazy sequences returned to callers.
- The core library performs **no file-system I/O**. Its only I/O is network.
- File-based configuration is loaded by the DI package at host startup, never
  inside the client constructor. A constructor cannot `await`, and a synchronous
  file read there would violate this requirement.

### R14 — Serialization

- `System.Text.Json`, with source generation (`JsonSerializerContext`).
- `JsonSerializerOptions` is created once and frozen. It is never created per
  call and never exposed by reference.
- snake_case property naming.
- `DictionaryKeyPolicy` is deliberately **left null**, so question ids pass
  through unmodified. Any dictionary-key policy would silently rewrite caller
  ids and lose answers.
- Serialization is synchronous and CPU-bound; only network I/O is async.
- Deserialization reads from the response stream asynchronously.
- No reflection-based serialization fallback; analyzers flag it as an error.

### R15 — JSON customisation

Customisation is available but off by default. The client works with no
configuration hook at all. When supplied, the hook is applied to a private copy
of the options at construction and then frozen.

### R16 — Target framework

`net10.0` only. No multi-targeting. Adding a second target later is additive and
non-breaking; removing one would not be.

### R17 — Project structure and file discipline

- Decompose by function first: one type per concern, one concern per file.
- When a single type is still too large, split it into partial classes along its
  functional sub-areas — never arbitrarily and never mid-method.
- Every type split across files carries a header comment listing all of its
  partials, so the type is discoverable from any one file.
- No file may exceed **100 KB**. This is a CI-enforced ceiling.
- Working targets, which will bind long before the ceiling: **400 lines soft,
  800 lines hard**.

### R18 — Naming

- Root namespace and package id: `Jev.Sdk`.
- DI package: `Jev.Sdk.DependencyInjection`.
- Acronyms of three or more letters are Pascal-cased, per .NET convention:
  `Sdk`, not `SDK`.
- Async methods take the `Async` suffix. The `CancellationToken` parameter is
  **required** — no default value — so cancellation is non-optional at every
  call site.

### R19 — Security and redaction

- The `state` value is caller content and may be sensitive or regulated. It is
  never logged, never used as a metric tag, and never attached to a span, at any
  level.
- The API key is never logged and the authorization header is always redacted.
- Metric tags are bounded. Question ids never become metric tags.

---

## 5. Locked Decisions

| # | Decision |
| --- | --- |
| D1 | Root namespace and package id: `Jev.Sdk` |
| D2 | Async methods suffixed `Async`; `CancellationToken` required, no default |
| D3 | CancellationToken honoured through every await, including retry delay |
| D4 | Partial classes by function, then by size; header comment lists partials; 100 KB CI ceiling |
| D5 | All I/O asynchronous; no sync surface, no sync-over-async, no file I/O in the core |
| D6 | Serialization: System.Text.Json, source-generated, frozen options |
| D7 | JSON customisation available, off by default |
| D8 | Telemetry: BCL only — `ILogger`, `ActivitySource`, `Meter` |
| D9 | Works out of the box: env-resolved key, sensible defaults, built-in retry |
| D10 | API key: `TYPESAFE_API_KEY`, loaded once, never watched |
| D11 | Key priority: explicit > environment > machine file > generic file |
| D12 | Constructor accepts an optional API key |
| D13 | File-based configuration is loaded by the DI package at host startup |
| D14 | Core seams are interfaces with built-in defaults |
| D15 | Target framework: `net10.0` only |
| D16 | Test framework: xunit (latest v3 line), pending a package-set verification probe |
| D17 | Licence: MIT |
| D18 | Repository is local-only. Nothing is written to the Obsidian vault |
| D19 | Throttling defaults match the vendor's SDKs: 2 retries, 500 ms initial backoff, 5 s backoff ceiling, 60 s Retry-After ceiling, 0.25 subtractive jitter, 10 s per-attempt timeout, retryable set 408/429/5xx |
| D20 | The vendor's exception surface and request-id are mirrored: distinct types per status, `RequestId` and `Endpoint` on every API exception, and `RequestId` on successful responses |
| D21 | The full declared type footprint is supported: every permissive `string \| object \| array \| null` member, the map-of-permissive Choice criteria, the array-of-permissive Score levels, the mixed string-or-integer error path, and caller-supplied request headers |
| D22 | Unmodelled fields are reachable in both directions on every wire model, matching the vendor's `extra_body` escape hatch on the request and exceeding their skip-and-warn behaviour on unknown answer kinds |
| D23 | No model enum. The model set is open, the default is the `jev-latest` alias, and repeated availability questions are served by a lazily populated per-client cache that never fetches at construction and never shares across accounts |
| D24 | The per-attempt timeout is enforced with a linked cancellation token rather than by assigning `HttpClient.Timeout`, because that property is immutable once a client has served a request and callers legitimately pass pooled or factory-managed clients |
| D25 | Every converter declares `HandleNull => true`, so a JSON null inside a collection is a value rather than a C# null, and an absent member stays distinguishable from a null one |
| D26 | Computed members are `[JsonIgnore]`; a serialized response never carries a field the API does not define |
| D27 | A client instance is safe for concurrent use: no lock on the model-cache hit path, one request per burst of concurrent first reads, atomic disposal, and cached type-information resolution |
| D28 | Defensive threading changes are labelled as defensive in the requirements; a green test is never presented as proof of a defect that was not observable |
| D29 | Every member the API may null has a never-null read accessor and a wire view that always emits it; a null wire member is never the reason a call fails outside the documented exception set |
| D30 | Methods documented to fall back rather than throw catch every non-cancellation exception, not only `JevException`, because the transport is a documented substitution point |
| D31 | Local validation rejects whitespace-only instructions, matching its existing treatment of blank question ids and option names; structured instructions are never treated as blank |
| D32 | A required, non-nullable member absent from a successful response is a protocol error, not a tolerated empty value; documented best-effort fallbacks (the model-list methods) are the explicit exception |
| D33 | A defect fix is not complete until every sibling of the same shape is checked; the `models` fix that missed `answers` is the recorded example |
| D34 | A unit suite confirms the client matches our model of the service; only a live call can reveal the model is wrong, so behaviour the stub cannot verify is verified against the live service |
| D35 | A live test skips when unconfigured rather than failing or passing; a green result for a call that never happened is worse than a red one |
| D36 | Where the specification and the live service disagree, the service is the fact and the divergence is recorded in `docs/api-notes.md`; `release_date` is the recorded example |

---

## 6. Testing Requirements

### R20 — Test structure

- xunit, latest v3 line. Package set to be verified by a scratch restore before
  it is written into the project files.
- `Microsoft.NET.Test.Sdk` for `dotnet test` support, if v3 does not supply its
  own runner wiring.
- No mocking framework. Test doubles are hand-written stubs of the client's own
  seams; a stub of `ITypeSafeTransport` makes the entire client testable.
- No assertion library. Built-in asserts only.
- The unit suite performs **no network I/O** and requires **no live credentials**.
- Live tests against `api.typesafe.ai` live in a separate project, are explicitly
  tagged, and are skipped when no API key is present.

### R21 — What must be tested

- Wire shape: captured real payloads asserted byte-for-byte through the
  source-generated serialization path.
- Question id round-trip: ids survive unmodified, including mixed case.
- Union handling: all three kinds in one payload; an unknown kind falls back
  without failing the response.
- Retry behaviour: deterministic 429/529 sequences, `Retry-After` honoured,
  backoff cancellable.
- Error mapping: 401, 422, 429, 529, and malformed-body-as-protocol-error.
- Validation: each fail-fast case, asserted to occur before any request is sent.
- Configuration precedence: all four sources and their order, plus the
  none-present failure.
- Redaction: the `state` value never reaches a log, span, or metric tag.

---

## 7. Open Items

| # | Item | Recommendation |
| --- | --- | --- |
| O1 | JSON section name for file configuration: `Jev:ApiKey` vs flat `ApiKey` | `Jev:ApiKey` — namespaced sections avoid collision in a host's shared config |
| O2 | Machine token source: `Environment.MachineName` vs a `MACHINE_NAME` env override | Use the env override when set, else `Environment.MachineName`; container hostnames are random per start, so the override keeps containers workable |
| O3 | Exception type naming: `TypeSafe*` vs `Jev*` now that the namespace is `Jev.Sdk` | `Jev*`, for consistency with the namespace |
| O4 | xunit v3 exact package set for a `net10.0` test project | Verify with a scratch restore before use |
| O5 | NuGet publication | Build publish-ready (metadata, XML docs as errors, public-API baseline, SemVer from 0.1.0); do not publish |

---

## 8. Verified API Facts

See [../docs/api-notes.md](../docs/api-notes.md) for the wire-level reference,
including the four places where the published documentation and the live OpenAPI
specification disagree.
