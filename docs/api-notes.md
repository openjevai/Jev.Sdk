# TypeSafe System One API — verified notes

These notes were verified against the live service, not copied from prose.

Sources:

- `https://docs.typesafe.ai/api.md` — the published API reference
- `https://api.typesafe.ai/openapi.json` — the live OpenAPI specification,
  retrieved with HTTP 200. Title `TypeSafe`, version `0.2.0`, OpenAPI 3.1.0.
- `https://docs.typesafe.ai/llms.txt` — the documentation index

Where the two disagree, the **OpenAPI specification is authoritative**; the
discrepancies are listed in section 6.

---

## 1. Endpoints

The specification defines exactly two paths:

| Method | Path | Purpose |
| --- | --- | --- |
| `POST` | `/v1/systemone` | Evaluate a state against typed questions |
| `GET` | `/v1/models` | List available models and aliases |

Neither path declares server parameters. Authentication is HTTP bearer
(`components.securitySchemes.HTTPBearer`), though the specification declares no
top-level `security` requirement — bearer auth is documented in prose, not
enforced by the spec.

```
POST https://api.typesafe.ai/v1/systemone
Authorization: Bearer <API_KEY>
Content-Type: application/json
```

Base URL for the client: `https://api.typesafe.ai/v1`.

### Response codes declared by the specification

`POST /v1/systemone` declares only `200` and `422`. `401`, `429`, and `529` are
**documented in prose only** and are absent from the specification. Any
client-side handling of those three is therefore inferred from documentation and
has not been verified against live behaviour.

---

## 2. Request

```
SystemOneRequest
  state      required   string | object | array
  model      required   string            (e.g. "jev-latest")
  questions  required   object            minProperties: 1
                        additionalProperties -> Question
```

`state` accepts a string for plain text, or an object/array carrying structured
context — conversation history, records, or application state.

Question map keys are chosen by the caller, are returned as-is in the response,
and are **not** sent to the model and **not** used in inference.

---

## 3. Questions

`Question` is a `oneOf` discriminated on the `type` property:

| `type` | Required | Optional |
| --- | --- | --- |
| `noul` | `type` | `instructions`, `criteria` |
| `choice` | `type`, `criteria` | `instructions` |
| `score` | `type`, `criteria` | `instructions` |

### Instructions

Wherever `instructions` appears, its type is
`string | object | array | null`. In the specification it is **optional on all
three kinds**. `api.md` states it is required. See section 6.

### Noul

```json
{
  "type": "noul",
  "instructions": "Does this convey urgency?",
  "criteria": { "true": "Explicitly time-sensitive", "false": "No urgency expressed" }
}
```

`criteria` is optional and clarifies what counts as yes and no. Both members
accept `string | object | array | null`.

### Choice

```json
{
  "type": "choice",
  "instructions": "Which team should handle this?",
  "criteria": {
    "billing": "Payments, invoicing, refunds",
    "technical": "Bugs, outages, integrations",
    "sales": null
  }
}
```

`criteria` is a map of option → description, each value
`string | object | array | null`. A null description means the option is
interpreted by its name alone.

### Score

```json
{
  "type": "score",
  "instructions": "How frustrated is the customer?",
  "criteria": ["Calm", "Frustrated", "Very angry"]
}
```

`criteria` is an ordered array; each item is `string | object | array`. Position
determines the level, starting at zero. The specification sets `minItems: 1`;
prose says "at least two levels".

---

## 4. Response

```
SystemOneResponse
  model    required   string
  answers  required   object   additionalProperties -> Answer
  usage    required   Usage  { input_tokens: int, output_tokens: int }
```

`model` may differ from the alias supplied in the request — for example, an alias
such as `jev-latest` may resolve to a dated model name. `output_tokens` are
currently not billed.

`Answer` is a `oneOf` discriminated on `type`, mirroring the question kind.

### Noul answer

```json
{ "type": "noul", "noul": 0.92 }
```

`noul` is the probability of yes, from 0 to 1. **A Noul answer has no
`confidence` member** — deliberately, rather than a zero that could be mistaken
for a real value. Do not synthesise one.

### Choice answer

```json
{
  "type": "choice",
  "choice": "technical",
  "probabilities": { "billing": 0.08, "technical": 0.85, "sales": 0.07 },
  "confidence": 0.82
}
```

Required: `choice`, `confidence`, `probabilities`.

### Score answer

```json
{
  "type": "score",
  "score": 1.6,
  "legend": { "0": "Calm", "1": "Frustrated", "2": "Very angry" },
  "probabilities": { "0": 0.05, "1": 0.3, "2": 0.65 },
  "confidence": 0.78
}
```

Required: `score`, `confidence`, `legend`, `probabilities`.

`score` is the probability-weighted average of the levels and **may fall between
integers**. Do not round it.

`legend` values are typed `string | object | array` in the specification, even
though the example shows plain strings.

`probabilities` keys are level indices **as strings** (e.g. `"0"`, `"1"`),
matching the `legend` keys.

### Confidence

Both Choice and Score answers carry `confidence`, a 0–1 value derived from the
shape of the probability distribution — concentrated means confident, flat means
uncertain. The value is reported verbatim and is never recomputed client-side.
Noul answers carry none.

---

## 5. Errors

| Status | Meaning | Retry? |
| --- | --- | --- |
| `401` | Missing or invalid API key | No |
| `422` | Request validation failure | No |
| `429` | Rate limit exceeded | Yes, with backoff |
| `529` | Service temporarily overloaded | Yes, with backoff |

The `422` body shape:

```
{ "detail": [ { "loc": [string|int], "msg": string, "type": string, "input": any, "ctx": object } ] }
```

`loc` is the path to the offending value — request location followed by field
names and array indices, e.g. `["body", "questions", "urgency", "score", "criteria"]`.
Only `loc`, `msg`, and `type` are required.

Retry guidance: use exponential backoff for `429` and `529`; do not retry
immediately.

---

## 6. Where the documentation and the specification disagree

Every claim in this section is made against a dated capture of the vendor's prose reference, kept in
`requirements/vendor/`. The captures are byte-identical to what the vendor served and are never
overwritten, so a later disagreement can be resolved by diffing two dated copies rather than by
arguing from memory. See `requirements/vendor/PROVENANCE.md` for per-capture hashes.

| Captured (UTC) | File | SHA-256 (first 16) |
| --- | --- | --- |
| 2026-09-19T08:17:31Z | `requirements/vendor/typesafe-api-2026-09-19T081731Z.md` | `b9b205096ac164e2` |

Four discrepancies were found by comparing `api.md` against the live
specification. In each case the specification wins.

1. **`GET /v1/models` is undocumented in prose.** It exists in the
   specification and is the only way to discover model names. `api.md` says only
   to use `"jev-latest"`. A client needs this endpoint.

2. **`instructions` required vs optional.** `api.md` marks it required on all
   three question kinds. The specification requires it on none of them — Noul
   requires only `type`; Choice and Score require only `type` and `criteria`.
   The server therefore accepts an instruction-less question. The client should
   still require it, to fail fast with a useful error rather than send a question
   that produces a confusing result.

3. **Score level count.** Prose says "at least two levels"; the specification
   says `minItems: 1`. A single-level Score returns a constant and is a caller
   bug. The client should validate a minimum of two.

4. **`legend` value type.** `api.md` types legend values as `map<string,string>`;
   the specification types them `string | object | array`. Model them permissively.

---

## 6a. Vendor throttling and restraint guidance

The vendor's SDK documentation specifies throttling behaviour that a client should match, because
a caller moving between the Python, JavaScript, and .NET clients should not get different
behaviour against the same account. Verified from
`docs.typesafe.ai/sdk/python/api/retries`, `/sdk/python/api/constants`,
`/sdk/javascript/api/interfaces/RetryPolicy`, and `/sdk/python/api/exceptions`.

**Retry policy defaults**

| Setting | Value |
| --- | --- |
| `max_retries` | 2 after the initial attempt; 0 disables retrying |
| `backoff_initial` | 0.5 s |
| `backoff_max` | 5.0 s |
| `backoff_jitter` | 0.25, subtracted from the delay rather than replacing it |
| `http_statuses` | 408, 429, and 500-599 |
| `respect_retry_after` | true |
| `max_retry_after_ms` | 60000; a longer server delay falls back to the computed backoff |
| API connection error | retried, including an interrupted response body |
| API timeout error | retried |

Note the retryable set carefully: it is **408, 429, and every 5xx**, not merely the 429 and 529
the prose API reference mentions. Retrying only 429 and 529 would leave 500, 502, and 503
un-retried, turning a transient server condition into a caller-visible failure.

**Client defaults**

| Setting | Value |
| --- | --- |
| Base URL | `https://api.typesafe.ai` |
| Model | `jev-latest` |
| Timeout | 10.0 s, per HTTP operation |
| API key | `TYPESAFE_API_KEY` |
| Base URL override | `TYPESAFE_BASE_URL` |
| Model override | `TYPESAFE_DEFAULT_MODEL` |
| Log level | `TYPESAFE_LOG_LEVEL` |

**Request identifier**

Responses carry an `x-typesafe-request-id` header, surfaced by the vendor's SDKs as `request_id`
on both results and errors. It is the handle their support asks for, so a client that discards it
leaves a caller unable to escalate a specific call. It is a bounded server-supplied identifier,
which makes it safe to log and safe to attach to a span, unlike the caller's state.

**Exception surface**

`400`, `401`, `403`, `404`, `422`, `429`, and `5xx` each map to a distinct type, plus separate
connection and timeout types. `429` is modelled as a rate limit rather than as a server error,
because a caller acting on one wants to slow down, not to report an outage.

**Logging**

The vendor's SDKs log to the `typesafe_sdk` logger and redact secret headers, but explicitly note
that **request and response bodies are not redacted**. That is a weaker position than this library
takes: see section 7. Matching their throttle behaviour is worthwhile; matching their body-logging
behaviour is not.

## 6b. Full declared type footprint

Every property in the specification, with the shape it declares and where this client models it.
Verified property by property against `components.schemas` in the live specification.

| Schema | Property | Declared type | Modelled as |
| --- | --- | --- | --- |
| `SystemOneRequest` | `state` | `string \| object \| array` | `StructuredValue` |
| | `model` | `string` | `string` |
| | `questions` | `map<Question>` | `IDictionary<string, Question>` |
| `NoulQuestion` | `type` | `const "noul"` | `Question.Type` |
| | `instructions` | `string \| object \| array \| null` | `StructuredValue?` |
| | `criteria` | `NoulCriteria \| null` | `NoulCriteria?` |
| `NoulCriteria` | `true`, `false` | `string \| object \| array \| null` | `StructuredValue?` each |
| `ChoiceQuestion` | `criteria` | `map<string \| object \| array \| null>` | `IDictionary<string, StructuredValue?>` |
| `ScoreQuestion` | `criteria` | `array<string \| object \| array>` | `IList<StructuredValue>` |
| `NoulAnswer` | `noul` | `number` | `double` |
| `ChoiceAnswer` | `choice` | `string` | `string` |
| | `confidence` | `number` | `double?` on the base |
| | `probabilities` | `map<number>` | `IDictionary<string, double>` |
| `ScoreAnswer` | `score` | `number` | `double` |
| | `legend` | `map<string \| object \| array>` | `IDictionary<string, StructuredValue>` |
| `SystemOneResponse` | `answers` | `map<Answer>` | `IDictionary<string, Answer>` |
| | `usage` | `Usage` | `Usage?` |
| `Usage` | `input_tokens`, `output_tokens` | `integer` | `int` |
| `ModelMetadataList` | `models` | `array<ModelMetadata>` | `IList<ModelMetadata>` |
| `ModelMetadata` | `name`, `description`, `release_date` | `string` | `string` each, plus a parsed `DateOnly?` |
| `HTTPValidationError` | `detail` | `array<ValidationError>` | `IList<ErrorDetails>` |
| `ValidationError` | `loc` | `array<string \| integer>` | `IList<object>` |
| | `msg`, `type` | `string` | `string` each |
| | `input` | *(no declared type)* | `JsonElementBox?` |
| | `ctx` | `object` | `JsonElementBox?` |

The permissive members are the ones worth naming. `state`, every `instructions`, every Choice option
description, every Score level, every Noul criteria member, and every `legend` value accept a plain
string *or* arbitrary JSON. They are all carried as `StructuredValue`, which preserves whichever
shape arrived rather than flattening it to text, and is exercised in every shape by the conformance
suite.

**Extension points.** The specification does not declare extension members, but the vendor's SDK
documentation describes unrecognised fields as a forward-compatibility escape hatch (`extra_body` on
the request, ignored fields on the response). This client exposes that on both sides:
`Question.AdditionalProperties`, `SystemOneRequest.AdditionalProperties`, and
`JevResponse.AdditionalProperties`.

On unknown answer kinds this client is *better* than the vendor's position rather than equal to it.
Their SDKs log a warning and skip an unrecognised answer kind, leaving the caller to dig it out of a
raw HTTP response. Here it arrives as an `UnknownAnswer` with its original JSON intact, so no raw
response is needed and the other answers in the same payload are unaffected.

## 6c. A header the specification does not declare

The specification declares **no response headers on any endpoint**. `x-typesafe-request-id` is
documented only in the vendor's SDK pages, as `request_id` on results and errors. It is therefore
inferred from documentation rather than confirmed by the specification, in the same category as the
401, 429, and 529 status codes noted in section 6.

Both endpoints declare `application/json` for their request and response bodies, and the only
declared security scheme is `HTTPBearer` with `scheme: bearer`. The specification sets no top-level
`security` requirement, so bearer auth is documented in prose rather than enforced by the spec.

## 6d. Three defects found by probing semantics rather than names

A name-based diff of the specification against the implementation reported no missing properties
across all 40 properties and 16 schemas. It would not have found any of the following, because each
is about behaviour rather than about which members exist. All three were found by writing probes
that exercised the declared shapes, and all three were confirmed by execution before being fixed.

**1. A JSON null inside a collection threw on read.** `JsonConverter<T>.HandleNull` defaults to
`false` for reference types. That means the serializer handles a null token itself and never calls
the converter, so a JSON null *inside a map or array* became a C# null rather than a
`StructuredValue.Null`. Because the declared types are `map<string | object | array | null>` and
`array<string | object | array>`, a null is a shape the server may legally send — and reading one
threw `NullReferenceException`. The fix is `HandleNull => true` on both converters. This is the more
dangerous class of bug of the three: it is a crash on a valid response.

**2. The same defect defeated `JsonElementBox`.** The box exists so that an *absent* member is
distinguishable from one that is present and null. With `HandleNull` unset, a member declared as
JSON null came back as a null box, which is indistinguishable from absence — the one outcome the
type was written to prevent. `ValidationError.input` has no declared type, so null is a legal value
for it.

**3. Computed members were serialized.** Parsing a response and writing it back emitted three fields
the API does not define: `total_tokens` on `Usage`, `parsed_release_date` on `ModelMetadata`, and
`location_path` on `ValidationError`. A client that echoes a payload with invented fields is a
protocol deviation, and it invites the reader to believe the server sent them. All three are now
`[JsonIgnore]`.

**4. A declared log message was never emitted.** `JevLog.SerializingRequest` had no call site. Its
logger-message generator wrote `ILogger` extension members that nothing reached, and the coverage
numbers were counting them. Removed.

The lesson is worth recording: a property-name diff, however thorough, verifies *shape* and not
*behaviour*. The three fixes above changed no public signature and no schema mapping — a diff would
have stayed green through all of them.

## 6e. A second verification pass, and what it found

A second independent adversarial pass, run against the live specification, found three defects using
execution rather than reading. The first is the same class as one already fixed, which is the point
worth recording: a fix applied to one member does not protect its siblings.

**MAJOR — a JSON null for `answers` escaped the documented failure set.** `SystemOneResponse.Answers`
went to a C# null, with three consequences from that one fact. The indexer threw
`NullReferenceException` where it documents `KeyNotFoundException`. The success path dereferenced the
null dictionary while logging the answer count, so a 200 whose body was
`{"answers":null}` produced a raw `NullReferenceException` from a call whose documented failures are
all `JevException` types. And because `WhenWritingNull` was in force, re-serializing dropped the
member entirely — emitting a body that omits a field the specification declares **required**.

This is precisely the defect fixed for `ModelListResponse.Models` one commit earlier, and it was
missed for `answers`. The lesson is that a guard written for one nullable member should be applied by
searching for every member of the same shape, not by fixing the one that was reported.

**Resolved on the contract, not by tolerance.** The first attempt at the fix returned normally with an
empty answer map. That was wrong, and worth recording because it was a plausible-looking
half-measure. The specification marks `model`, `answers` and `usage` as **required and non-nullable**
on `SystemOneResponse`, and the library already documents that a body which cannot be honoured
surfaces as `JevConnectionException` with `IsProtocolError` — the same treatment it gives an empty or
non-JSON body. Returning an empty map instead made that documented contract false, and it also erased
a distinction that matters: this client refuses to send a request with no questions, so "empty
answers" can only ever mean the server failed to answer the questions that *were* sent. Reporting a
protocol error is the honest outcome.

The one asymmetry, recorded deliberately: `ModelMetadataList.models` is also declared required, but
the four model-list methods each document and implement a benign fallback for an unavailable list,
pinned by tests from the first verification pass. Those callers asked a best-effort question and were
promised a usable answer either way, so the tolerance stays there on purpose. The SystemOne path
promises no such tolerance, so it gets none.

`model` is tested for emptiness rather than null, because the property is a non-nullable string
initialised to `""`, so an absent member deserializes to `""`. Making it nullable would force a null
check on every caller for a case that cannot occur through this client.

**MEDIUM — two methods documented a fallback they did not always provide.** `ResolveModelAsync` and
`WarmModelsAsync` are documented as returning a value rather than throwing when a listing fails, but
caught only `JevException`. The transport is a documented substitution point, so a host implementation
throwing its own exception type escaped both, and a caller who followed the documentation — no
try/catch — saw the exception it was told not to expect. Both now catch any non-cancellation
exception.

**LOW — whitespace-only instructions passed local validation.** `Noul("   ")` produces a text-shaped
`StructuredValue` that a null check does not catch, so the request was sent and rejected by the
server — the exact server round trip local validation exists to prevent. The same file already
rejected a blank question id and a blank option name, so this was an inconsistency rather than a
decision. Structured instructions are deliberately still accepted, because an empty JSON object is
content even though its string view is null.

## 6f. What only the live service revealed

The unit suite tests the client against a stub, so it can only ever confirm that the client agrees with
what we believe the service sends. A live integration suite was added to test the belief itself, and it
found two things on its first run.

### The specification documents a `release_date` format the service does not send

The specification declares `release_date` as a string, described as "Model release date, formatted as
YYYY-MM-DD", with the example `2026-09-15`. Every unit test used that shape, so `ParsedReleaseDate`
passed every test it had — against input the service never sends.

The live service returns a full ISO-8601 timestamp:

```
2026-09-10T18:38:01.391457+00:00
```

So `ParsedReleaseDate`, which only tried `yyyy-MM-dd`, returned **null for every model the service
actually reports**, while the documentation on the property claimed it parsed "when it is well formed"
and the whole unit suite stayed green.

`ParsedReleaseDate` now reads both shapes: a plain date, and a timestamp, whose date component is taken
from the payload's own offset rather than converted to local time — a release date is a calendar day, and
converting would report the wrong day for a caller west of UTC. `ReleaseDate` itself is still returned
verbatim, so a caller comparing against the API's own output sees no normalization the library applied
on their behalf.

This one is worth remembering as a category: a stub confirms the client matches our model of the service.
It cannot tell us the model is wrong. Only a real call can.

### The resolved model is not in the model list

The first draft of the integration suite asserted that the model named in a response appears in
`GET /v1/models`. It failed, and the assertion was wrong rather than the library.

`GET /v1/models` advertises **aliases** — on this account, `jev-latest` and `jev-preview`. Those resolve
server-side to a concrete version, `jev-1.13.0`, which the list does not itself contain:

```
advertised aliases: jev-latest, jev-preview
requested 'jev-latest'  -> response model 'jev-1.13.0' | in advertised list: False
requested 'jev-preview' -> response model 'jev-1.13.0' | in advertised list: False
requested '(default)'   -> response model 'jev-1.13.0' | in advertised list: False
```

The library's behaviour is correct: it reports what answered, and the request named an alias. The test now
asserts the property that actually holds — the requested alias is one the service offers, and every
advertised alias is accepted — instead of one that merely looked reasonable.

## 7. Client design consequences

- **Model discovery is required**, not optional — it is the only documented way
  to learn what models exist.
- **`DictionaryKeyPolicy` must be null** in the serializer. Question ids are
  dictionary keys; a snake_case key policy would rewrite a caller's `isUrgent`
  to `is_urgent` and the answer would return under a key the caller never used.
  This is a silent, data-losing failure mode.
- **Union deserialization must tolerate unknown discriminators.** TypeSafe will
  add a fourth question kind. The default `System.Text.Json` polymorphic
  behaviour throws on an unknown discriminator, which would fail the entire
  response because of one new kind. Fall back to the nearest ancestor and
  capture unmodelled fields.
- **Numbers stay `double`.** `noul`, `score`, `confidence`, and all
  probabilities. Never round, never convert to `decimal`, never recompute
  confidence.
- **The `state` value is sensitive by default.** It is caller content — support
  tickets, customer messages, possibly regulated data. Never logged, never a
  metric tag, never attached to a span. The vendor's SDKs redact secret headers
  but not request or response bodies; this client does not log bodies at all.
- **Match the vendor's throttling, but not their logging.** The retry defaults and
  retryable status set are mirrored so behaviour is consistent across SDKs. The
  body-logging behaviour is deliberately not mirrored.

---

## 8. Model information

`GET /v1/models` returns:

```
ModelMetadataList
  models  required  array of ModelMetadata
    name          string   e.g. "jev-latest"
    description   string
    release_date  string   YYYY-MM-DD, e.g. "2026-09-15"
```

---

## 9. Existing clients

| Language | Package | Affiliation |
| --- | --- | --- |
| Python | `typesafe-sdk` | Official |
| JavaScript / TypeScript | `@typesafe-ai/sdk` | Official |
| .NET | `TypeSafe.Sdk` 0.1.0-alpha.2 — github.com/saibimajdi/typesafeai-dotnet-sdk | Community, unaffiliated |
| .NET | `TypeSafeAI` 0.2.0 — github.com/Hawxy/TypeSafeAI.Net | Community, unaffiliated |

Neither .NET client is affiliated with TypeSafe AI. This project (`Jev.Sdk`) is
likewise independent. The `Jev.Sdk`, `Jev.Sdk.DependencyInjection`, and `Jev`
package ids were verified as unclaimed on nuget.org.

Both official SDKs read the API key from `TYPESAFE_API_KEY`, which is the reason
this client uses the same variable name.

---

## 10. Vendor guidance worth following

TypeSafe publishes an agent skill at `github.com/typesafe-ai/skills` describing
how they intend workflows to be built. The points that shape a client's API:

- Send many questions in one request, including speculative ones whose answers
  only matter for some inputs — they are evaluated in parallel and adding
  questions barely changes latency. This is why the client takes a map of
  questions rather than one at a time.
- Ask for one snap judgment per question. A question that needs slow reasoning
  should be broken into several questions combined in calling code.
- Questions within one request are independent; a question cannot reference
  another's answer. Genuine dependencies need a second request.
- Prefer the question kind whose answer the calling code can act on directly.
- Confidence is meant for gating: the answer says what, the confidence says
  whether to act on it.
- The request token budget is roughly 32,000 tokens, shared between state and
  questions.
