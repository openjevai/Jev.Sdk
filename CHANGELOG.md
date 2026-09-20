# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `Jev.Sdk`, a .NET client for the TypeSafe AI System One API.
  - `SystemOneAsync` for `POST /v1/systemone`, with overloads for plain text, raw JSON, and a
    caller-owned object serialized through the caller's own `JsonTypeInfo<TState>`.
  - `GetModelsAsync` for `GET /v1/models`, the model-discovery endpoint that the prose API
    reference omits but the OpenAPI specification defines.
  - Typed questions (`Noul`, `Choice`, `Score`) and typed answers, with convenience readers for
    each kind.
  - Forward compatibility: an unmodelled question or answer kind is preserved rather than
    rejected, so a new API feature does not break an existing integration.
  - Typed errors for 401, 422, 429, and 529, with per-field details on a validation failure,
    and `Retry-After` honoured on a rate limit.
  - Retry with exponential backoff and optional jitter for 429, 529, and transport failures.
    A 401 or 422 is never retried.
  - Source-generated `System.Text.Json` serialization, with no reflection fallback.
  - BCL telemetry only: `ILogger`, `ActivitySource`, and `Meter`, with no exporter dependency.
  - Extensibility seams (`ITypeSafeTransport`, `IApiKeyProvider`, `ILogger<T>`) with working
    defaults, so the client is testable and replaceable without the DI package.
- `Jev.Sdk.DependencyInjection`, integration with `Microsoft.Extensions.DependencyInjection`
  and `Microsoft.Extensions.Configuration`.
  - `AddJevClient`, with `TryAdd` semantics so a caller's own registration wins.
  - `JevConfigurationLoader`, which builds the documented precedence: environment, then
    `appSettings.{MACHINE_NAME}.json`, then `appSettings.json`. Files are read once and never
    watched.
  - `JevOptionsBinding` for reading options from the `Jev` configuration section.
- `Jev.Sdk.Sample`, a minimal console client that exercises both endpoints by hand.
- 439 tests covering serialization, forward compatibility, retries, error mapping, validation,
  configuration precedence, dependency injection, telemetry, and redaction.

### Notes

- Targets `net10.0` only. .NET 8 support ended in November 2026, so multi-targeting would serve
  a runtime already out of support.
- The packages are not published to nuget.org. The build workflow produces the `.nupkg` files as
  downloadable artifacts on every push to `main`.

[Unreleased]: https://github.com/robchartier/Jev.Sdk
