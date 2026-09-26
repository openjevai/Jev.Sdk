# OpenJEV support

This fork of [Jev.Sdk](https://github.com/nothingmn/Jev.Sdk) adds **optional** support for
[OpenJEV](https://openjev.sh), a free community gateway to the same Jev model that TypeSafe AI
provides. **TypeSafe stays the default** — anyone with a TypeSafe key sees zero behaviour change.

Jev is built by [TypeSafe AI](https://typesafe.ai). OpenJEV is a community gateway to it, not a
replacement.

## What changed

The OpenJEV endpoint, model, and key are added *alongside* TypeSafe. Nothing TypeSafe is renamed,
removed, or re-defaulted.

| | TypeSafe (default, unchanged) | OpenJEV (optional, added) |
|---|---|---|
| Endpoint | `https://api.typesafe.ai/v1/systemone` | `https://api.openjev.sh/v1/systemone` |
| Model | `jev-latest` | `openjev` |
| Key env var | `TYPESAFE_API_KEY` | `OPENJEV_API_KEY` |
| Overload status | 529 | 503 (already covered by the 5xx retry set) |

Files added or modified:

- `src/Jev.Sdk/Configuration/JevEnvironment.cs` — added `JEV_PROVIDER` (`ProviderVariable`) and
  `OPENJEV_API_KEY` (`OpenjevApiKeyVariable`) environment variable names.
- `src/Jev.Sdk/Configuration/JevClientOptions.cs` — added `OpenjevBaseAddress`
  (`https://api.openjev.sh/v1/`) and `OpenjevModelName` (`openjev`) defaults.
- `src/Jev.Sdk/Configuration/EnvironmentOptionsProvider.cs` — added provider selection: when
  OpenJEV is chosen, the base address and default model switch to the OpenJEV defaults (only when
  the caller has not set them explicitly).
- `src/Jev.Sdk/JevClient.cs` — the default key chain now tries the OpenJEV key when
  `JEV_PROVIDER=openjev`, otherwise tries `TYPESAFE_API_KEY` first with `OPENJEV_API_KEY` as a
  fallback. The "no API key found" error mentions `OPENJEV_API_KEY`.
- `src/Jev.Sdk.DependencyInjection/JevServiceCollectionExtensions.cs` — the DI key chain mirrors
  the core library's selection.
- `README.md`, `samples/Jev.Sdk.Sample/.env.example`, `samples/Jev.Sdk.Sample/appSettings.json` —
  short OpenJEV notes next to the existing TypeSafe documentation.

## Provider selection rule

Applied in `EnvironmentOptionsProvider.Apply`, only when the option is still at its TypeSafe
default (an explicit choice in code always wins):

1. **`JEV_PROVIDER=openjev`** forces OpenJEV — base address, model, and key all use OpenJEV.
2. Otherwise, if `TYPESAFE_API_KEY` is set → **TypeSafe** (the unchanged default).
3. Otherwise, if only `OPENJEV_API_KEY` is set → **OpenJEV**.

The key chain mirrors this: with `JEV_PROVIDER=openjev` the `OPENJEV_API_KEY` environment provider
is preferred; otherwise `TYPESAFE_API_KEY` is tried first and `OPENJEV_API_KEY` is a fallback. To
pin OpenJEV from a settings file, set `Jev:BaseAddress` to the OpenJEV URL and
`Jev:DefaultModel` to `openjev`.

## How to configure

```bash
# TypeSafe (default — unchanged):
export TYPESAFE_API_KEY=ts-...

# OpenJEV, used only when no TypeSafe key is present:
export OPENJEV_API_KEY=ojev-...

# Force OpenJEV even when a TypeSafe key is set:
export JEV_PROVIDER=openjev
export OPENJEV_API_KEY=ojev-
```

## How it was verified

- The diff was read; no TypeSafe default, name, or text was removed or re-defaulted.
- A live `POST https://api.openjev.sh/v1/systemone` request with the `openjev` model, a `noul`
  question, and state `ping` returned HTTP 200.
- A `grep` confirmed no hardcoded `api.typesafe.ai` default remains in the code that was added —
  the OpenJEV endpoint is the only new host, and TypeSafe's defaults are untouched.

The project's own tests/build were **not** executed (third-party code is never run by the porter).

## Upstream

Original project: https://github.com/nothingmn/Jev.Sdk by @nothingmn (MIT license).
