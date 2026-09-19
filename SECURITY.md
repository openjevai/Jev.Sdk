# Security

## Reporting a vulnerability

Report suspected vulnerabilities privately. Do not open a public issue for a security problem.

Include enough to reproduce: the affected version, a description of the issue, and a minimal
repro if you have one. You will get an acknowledgement, and an assessment of severity and
scope.

## What this library handles

`Jev.Sdk` is an HTTP client for the TypeSafe AI System One API. The security-relevant facts
about it are:

- **The API key is a bearer credential.** It is sent in the `Authorization` header and nowhere
  else. It is never written to a log, a metric tag, a trace tag, or an exception message. The
  one place it appears is the request that carries it.
- **The `state` value is caller content and is treated as sensitive.** It is whatever the caller
  asks to be evaluated: a support ticket, a customer message, an application record. It may be
  regulated. This library never logs it, never uses it as a metric tag, and never attaches it to
  a span, at any level, including trace. If you find a code path that violates this, that is a
  security bug, not a logging preference.
- **The library performs no file I/O.** It does not read configuration from disk, does not write
  caches, and does not watch files. Configuration is resolved from an explicit value, the
  environment, or a settings object the host has already constructed. This keeps the credential
  out of any file this library could be induced to open.
- **Errors carry the raw response body.** `JevApiException.ResponseBody` holds whatever the
  server returned, because it is the most useful thing to have when diagnosing a rejection. It
  can echo parts of the caller's request. Treat it as you would the request itself: do not log it
  verbatim in a multi-tenant system.

## Configuration guidance

- Prefer the environment variable or a secret store over a settings file. The settings-file
  support exists for convenience on a workstation; a checked-in file is a checked-in credential.
- `.gitignore` excludes `appsettings.Local.json`, `appsettings.*.Local.json`, `secrets.json`,
  `*.secrets.json`, `.env`, and `.env.*`.
- If you use `Jev.Sdk.DependencyInjection`, remember that the machine-specific file
  (`appSettings.{MACHINE_NAME}.json`) takes precedence over the generic one. A file named after
  a container's random hostname is effectively a no-op, which is what the `MACHINE_NAME`
  environment override exists to fix.

## Supported versions

This library is pre-1.0 and has not been published. Until a 1.0 release, only the current `main`
branch is supported.
