# Integration tests

These tests call the **real** TypeSafe API. They are the only tests in this repository that do.

The unit suite under `tests/Jev.Sdk.Tests` proves the client sends and parses the right shapes
against a stub. It cannot prove the service agrees. That is what this project is for: authentication
against the live service, the real answer schema, model resolution, and the three question kinds
answered by the actual model.

## They skip without a key, and that is deliberate

With no key configured every live test **skips** and says where a key can come from. It does not
fail, and it does not silently pass.

A green tick for a call that never happened would be worse than a red one: it would report coverage
of behaviour that was never exercised. A skip is visible in the report, names what is missing, and
cannot be mistaken for an assertion that passed.

This is also why `ConfigurationWiringTests` runs **without** a key. Those tests do not call the API;
they prove the configuration plumbing works, so "did my settings file actually get read?" has an
answer rather than being a matter of faith.

## Putting in a key

Two ways, in the library's documented precedence order. The environment wins.

**1. Environment variable** — nothing to configure:

```sh
TYPESAFE_API_KEY=your-key-here dotnet test tests/Jev.Sdk.IntegrationTests
```

**2. `appSettings.Local.json`** — copy the template and edit one value:

```sh
cp tests/Jev.Sdk.IntegrationTests/appSettings.Local.json.template \
   tests/Jev.Sdk.IntegrationTests/appSettings.Local.json
# then put your real key in appSettings.Local.json
```

`appSettings.Local.json` is **git-ignored**, so a real key cannot be committed by accident. The
`.template` file is tracked so the expected shape is discoverable.

A value still reading `REPLACE ME` is treated as **not configured** — the tests skip rather than fail
with a confusing authentication error. That guard exists precisely so an unedited copy is harmless.

### Why a file called "Local" works

The library itself reads exactly two files: `appSettings.json` and `appSettings.{MACHINE_NAME}.json`.
There is no special "Local" file. This project passes the machine token `Local` to the library's own
loader:

```csharp
JevConfigurationLoader.Build(AppContext.BaseDirectory, "Local");
```

which makes the machine-specific candidate resolve to `appSettings.Local.json`. So the familiar name
works **through the library's documented machine-override mechanism** rather than through a special
case. `ConfigurationWiringTests` asserts that the token and the file name agree, so the two cannot
drift apart.

## Running them

```sh
# Everything. Live tests skip if no key is present.
dotnet test tests/Jev.Sdk.IntegrationTests

# Only the live ones, with a key in the environment.
TYPESAFE_API_KEY=your-key-here dotnet test tests/Jev.Sdk.IntegrationTests

# Only the ones that need no key.
dotnet test tests/Jev.Sdk.IntegrationTests --filter "FullyQualifiedName~ConfigurationWiringTests"
```

## What is covered

`ModelEndpointTests` — `GET /v1/models`

- at least one model, every model named and described
- every release date parses via the library's own `ParsedReleaseDate`
- names are unique, because a name is the identifier a caller passes back in `model`
- the documented `jev-latest` alias is actually offered by the service
- `IsModelAvailableAsync` is true for the default and false for nonsense
- the cache serves repeated calls, and still works after invalidation
- 16 concurrent readers on one client all get the same list
- cancellation is honoured

`EvaluationEndpointTests` — `POST /v1/systemone`

- all three question kinds answered, with a value in the documented range
- each kind checked on an **obvious case**, so a model that stopped understanding the question
  fails rather than passing on a range check alone
- a choice answer is one of the offered options, and its probabilities sum to about one
- the answer kind matches the question kind for every question
- several questions in one call all come back answered
- `model` and `usage` are reported, and the model is one the service advertises
- structured `state` and structured `instructions` are accepted by the service
- caller-supplied headers do not break the call
- an invalid request is rejected locally, without spending a round trip
- the typed generic overload binds the live payload to a caller-owned type

`ConfigurationWiringTests` — no key needed

- the machine token used here resolves to `appSettings.Local.json`
- the local file outranks the generic one, using real files
- the environment outranks the file
- with no file, the key is unset rather than an error
- placeholder and blank values count as not configured
- the skip message names both places a key can come from

## Cost and courtesy

These tests spend real API calls and quota, so the suite is **small and deliberate** rather than
exhaustive — the point is to detect drift in behaviour the unit suite cannot see, not to re-test
every branch against a paid endpoint.

Every live test shares one client with `MaxRetries = 1`. Retrying four times against a throttled
account turns one failure into several seconds of waiting and hides the throttle; one retry proves
the path works without turning the suite into a load generator.

Nothing here logs, echoes, or includes an API key in a failure message.
