# Jev.Sdk.DependencyInjection

Integration with `Microsoft.Extensions.DependencyInjection` and
`Microsoft.Extensions.Configuration` for [Jev.Sdk](../README.md), the .NET client for the
TypeSafe AI System One API.

## Install

```
dotnet add package Jev.Sdk.DependencyInjection
```

## Register the client

```csharp
using Jev.Sdk.DependencyInjection;

builder.Services.AddJevClient(builder.Configuration);
```

That is the whole setup. The client resolves its API key from the priority ladder below and
uses its defaults for everything else.

## Configuration

The API key is resolved from the highest-priority source that supplies one:

| Priority | Source |
| --- | --- |
| 1 | An explicit key passed to the client or set on `JevClientOptions` |
| 2 | The `TYPESAFE_API_KEY` environment variable |
| 3 | `appSettings.{MACHINE_NAME}.json` |
| 4 | `appSettings.json` |

The environment beats every file, and a machine-specific file overrides the generic one.

```json title="appSettings.json"
{
  "Jev": {
    "ApiKey": "your-api-key",
    "BaseAddress": "https://api.typesafe.ai/v1/",
    "DefaultModel": "jev-latest",
    "Timeout": "00:01:40"
  }
}
```

Every value is optional. A file containing only the key is fine, and so is no file at all.

### Loading the settings files

`AddJevClient` reads the files the host has already loaded into `IConfiguration`. To build that
configuration with the documented file precedence, use `JevConfigurationLoader`:

```csharp
using Jev.Sdk.DependencyInjection;

IConfiguration configuration = JevConfigurationLoader.Build();

builder.Services.AddJevClient(configuration);
```

`Build` looks for `appSettings.json` and then `appSettings.$MACHINE_NAME.json` in the working
directory, and adds environment variables on top. Files are read once and are deliberately not
watched for changes: a client holds a resolved key, and reloading underneath a live client
would make its behaviour depend on when a file last changed.

The machine token comes from the `MACHINE_NAME` environment variable when set, falling back to
`Environment.MachineName`. The override matters in containers, where the machine name is a
random identifier that changes when the container is recreated.

## Replacing a seam

Registration uses `TryAdd`, so anything registered before `AddJevClient` wins:

```csharp
// A custom key source, such as a secret store.
builder.Services.AddSingleton<IApiKeyProvider, KeyVaultApiKeyProvider>();

builder.Services.AddJevClient(builder.Configuration);
```

The same applies to `ITypeSafeTransport` and `ILogger<JevClient>`.

## Tests

The client can be built directly with substituted collaborators, without this package at all:

```csharp
var client = new JevClient(
    options: new JevClientOptions { ApiKey = "test" },
    transport: new StubTransport(),
    apiKeyProvider: new StaticApiKeyProvider("test"),
    logger: NullLogger.Instance);
```

A stub transport makes every code path in the client reachable with no network and no server.

## License

MIT. See [LICENSE](../LICENSE).
